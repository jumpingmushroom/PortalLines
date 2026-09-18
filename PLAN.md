# Portal Lines — Technical Plan (draft for discussion)

**Goal:** a client-side Valheim mod that draws each portal on the large map and a line to the
portal it is connected to, so you can see the whole network at a glance.

**Target build:** Valheim **1.0.12** (`Version.CurrentVersion = new GameVersion(1, 0, 12)`),
network version **40**. Analysis source: `assembly_valheim.dll` from `../Comfortaudit/lib`
(the client build verified on the rig on 2026-09-15), decompiled with ILSpy 9.1 into the session
scratchpad. Every type and member cited below is present in that assembly.

---

## 1. What the game actually does

### 1.1 A portal is a ZDO with a tag and a connection

- Portal prefabs are whatever `Game.m_portalPrefabs` lists on the shipped `_GameMain` prefab.
  `Game.Awake` hashes their names into `Game.instance.PortalPrefabHash` (`List<int>`). The mod
  reads that list at runtime instead of hardcoding `portal_wood` / `portal_stone`; whatever
  Deep North added is covered automatically.
- The tag is `zdo.GetString(ZDOVars.s_tag)`; the author is `ZDOVars.s_tagauthor`. **An empty
  tag is a valid tag** — two untagged portals connect to each other.
- The link is **not** derived from tags on the client. It is a stored connection:
  `zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal)` returns the partner's `ZDOID`
  or `ZDOID.None`. `TeleportWorld.HaveTarget()` / `TargetFound()` use exactly this.
- **Only the server pairs portals.** `Game.Start` runs `ConnectPortalsCoroutine` every 5 s
  when `ZNet.instance.IsServer()`. `Game.ConnectPortals()` first breaks links whose partner is
  missing or whose tag no longer matches, then for each unconnected portal takes the **first**
  unconnected portal with the same tag (`FindRandomUnconnectedPortal` — despite the name it is
  list order, not random) and sets both connections. Consequences:
  - Three portals sharing a tag: two are linked, the third stays unconnected until one of the
    pair is retagged or destroyed. Worth surfacing as a "conflict".
  - Retagging (`TeleportWorld.RPC_SetTag`) clears both ends immediately; the server re-pairs
    within 5 s. Expect a short "unconnected" flicker after renames.
  - Wood and stone portals pair freely; the prefab is never checked.

### 1.2 What a client knows, and when

`ZDOMan` keeps portals out of the ordinary sector lists and in `m_portalObjects`
(`Dictionary<SectorIndex, List<ZDO>>`), exposed by `ZDOMan.instance.GetPortalList()`.

- **On the server** (dedicated, or the hosting player in a non-dedicated game, and
  singleplayer) that list is every portal in the world.
- **On a client** it is every portal ZDO the server has ever sent this session. The server's
  `CreateSyncList` → `FindSectorObjects` → `FindObjects` includes `m_portalObjects` for sectors
  inside the peer's *near* simulation distance only; `FindDistantObjects` does not look at
  portals at all. So a portal is first learned when you come within a few zones of it.
- **Clients never forget a persistent ZDO.** `ZNetScene.RemoveObjects` destroys the GameObject
  when you walk away but leaves the ZDO in `m_objectsByID` and `m_portalObjects`. Its data goes
  stale (tag, connection) until the server sends it again.
- **A client can pull any ZDO by ID.** `ZDOMan.instance.RequestZDO(id)` is a vanilla routed
  RPC; the server answers with `ZDOPeer.ForceSendZDO`, which sends the ZDO in the next sync if
  the peer's copy is out of date (`ShouldSend` compares revisions), regardless of distance.
  `TeleportWorld.TargetFound()` already does this for the partner of every portal near the
  player, so this traffic pattern is sanctioned by the game itself.

Putting these together: **a client-only mod can learn the far end of every portal it has ever
stood near**, and can refresh what it knows on demand, with zero server-side code. What it
cannot learn is a portal it has never been near *and* that is not linked to one it has.

### 1.3 Session and save boundaries

- `ZDOID`s are session-scoped on the client side (fresh peer table each connection) and the
  save file stores portal links as a hash that `ZDOMan.ConnectPortals()` re-resolves at load.
  **Do not persist ZDOIDs.** Persist positions: portals do not move.
- The world is identified by `ZNet.World.m_name` + `ZNet.World.m_seed`.

### 1.4 How the map places things

`Minimap.UpdatePins` positions every pin with two private helpers:

```csharp
WorldToMapPoint(p, out mx, out my);           // world → [0,1] texture space
MapPointToLocalGuiPos(mx, my, uvRect, rect);  // texture space → pixels in the map RectTransform
```

`WorldToMapPoint` is `p.x / m_pixelSize + m_textureSize/2`, normalised by `m_textureSize`; the
second helper subtracts the current `RawImage.uvRect` origin and scales by the rect. Zoom and
pan are entirely expressed by `m_mapImageLarge.uvRect` (set in `CenterMap`), and pins are
children of `m_pinRootLarge` with `anchoredPosition` in that pixel space. Anything we draw as a
UI `Graphic` under the same root, using the same four lines of math, lands exactly on the map at
every zoom level. Both helpers are trivial to re-implement, so no publicizer dependency is
needed for the geometry.

Vanilla pins: `Minimap.instance.AddPin(pos, PinType, name, save, isChecked)` is public;
`PinData.m_icon` is a public field, so a pin can be given any sprite after creation. The
portal piece's own `Piece.m_icon` (on the prefab in `ZNetScene`) is a ready-made portal icon —
nothing to ship.

Jotunn 2.30.0 offers `MinimapManager` texture overlays. They are the wrong tool for lines:
2048² texture repaints are slow and the result is blurry at high zoom. A mesh-based `Graphic`
is crisp and costs one rebuild per pan/zoom frame.

---

## 2. Proposed design

### 2.1 Portal registry (`Core/PortalRegistry`)

One in-memory table of known portals keyed by **rounded world position** (0.5 m grid), each
entry carrying: position, tag, prefab hash, live `ZDOID` (if seen this session), partner
position (if known), source (`Live`, `Requested`, `Remembered`), last-seen time.

Sources, merged in this order:

1. **Live** — `ZDOMan.instance.GetPortalList()` every scan tick (2 s, only while the map is
   open or once a minute otherwise). Reads tag, connection, position.
2. **Partner fetch** — for each live portal whose partner ZDO is not local,
   `ZDOMan.instance.RequestZDO(partnerId)`, at most once per ID per 10 s. When it arrives it is
   simply a live portal on the next scan.
3. **Refresh** — when the large map opens, and every 10 s while it stays open, re-request
   every known portal ID that is outside the active area, so stale tags and broken links catch
   up. The server sends nothing for unchanged ZDOs; the cost is one tiny RPC per portal.
4. **Remembered** — per-world JSON cache on disk, loaded at world start and saved on change and
   on logout. Entries not confirmed live this session are drawn faded. An entry is dropped when
   its zone is loaded (`ZoneSystem.instance.IsZoneLoaded`) and no live portal sits within 1 m
   of it — i.e. it was demolished.

Link derivation:

- **Confirmed**: both ends live and each names the other as its connection.
- **Presumed**: exactly two known portals share a tag but the connection is not (yet)
  observable — typically one end is only *remembered*. Drawn dashed.
- **Conflict**: three or more known portals share a tag. All flagged; the confirmed pair, if
  any, still gets its line.
- **Orphan**: a portal with no partner known at all.

### 2.2 Overlay (`UI/PortalLinesGraphic : MaskableGraphic`)

- One instance instantiated under `Minimap.instance.m_pinRootLarge` as the **first sibling**,
  so it renders beneath pins. Stretch anchors, pivot `(0,0)`, `raycastTarget = false` so it
  never intercepts map clicks. A second, optional instance under `m_pinRootSmall`.
- `OnPopulateMesh` emits one quad per line segment (plus round-ish caps by overlapping ends).
  Each line is drawn twice: a dark 1-px-wider underlay, then the colour — keeps lines legible
  over snow, sand and meadow. Lines are clipped to the map rect (Liang–Barsky) before quads are
  built so nothing is emitted off-screen.
- Rebuild only when `m_mapImageLarge.uvRect` changed since last frame or the registry version
  bumped. Nothing runs while the map is closed.
- Style: width in px, alpha, colour mode (`PerTag` hashed palette | `Single`), dashed for
  presumed links, faded for remembered ends. Portal-stone links get a distinct hue by default.

### 2.3 Portal pins (`UI/PortalPins`)

Vanilla pins via `AddPin(pos, PinType.None, tag, save: false, isChecked: false)` with
`m_icon` set to the portal piece's icon. Unlinked portals get a tinted icon. A Harmony prefix on
`Minimap.RemovePin(PinData)` returns `false` for our pins so a stray right-click cannot delete
them (they are not saved and would reappear anyway, but the flicker is ugly). Toggleable.

The alternative — drawing our own icons and labels inside the overlay — is fully independent
of vanilla pin code but is more work; keep as an option if vanilla pins fight us.

### 2.4 Hover and detail (v0.3)

While the large map is open, nearest known portal to the cursor within ~24 px: highlight its
line, show a small tooltip: tag, partner tag, straight-line distance, "confirmed / presumed /
unconnected / conflict (n)". Mouse position → world via `Minimap.ScreenToWorldPoint` (public).

### 2.5 Config

`Lines` (enabled, width, alpha, colour mode, show presumed, show on minimap),
`Pins` (enabled, show tags, tint unlinked),
`Data` (refresh interval, remember portals on disk, forget after N days unseen),
`Debug` (verbose, dump command). Console: `portallines list|refresh|forget|dump`.

### 2.6 Compatibility

- **Portal-rewiring mods** (TargetPortal, XPortal, AnyPortal, PotalMap) replace the pairing
  rule; lines under them are meaningless. Detect their GUIDs via
  `BepInEx.Bootstrap.Chainloader.PluginInfos`, log a warning, and default lines off (pins stay).
- **HUDCompass** also pins portals; both can coexist, our pins are off if it is present.
- **Dedicated servers**: no server install for v0.1–0.3. A v0.4 server component (Jotunn
  `NetworkManager.Instance.AddRPC`, one `ZPackage` of `{pos, tag, partnerPos}` per portal,
  pushed on join and on `ZDOMan.SetDirtyPortals`) would give clients the whole world at once.
  The registry treats it as just another source, so nothing in v0.1 needs to change.
- **`noportals` global key**: portals still exist; keep drawing.

### 2.7 Runtime assumptions to verify on first deploy (log at first map open)

1. `m_pinRootLarge.rect` equals `m_mapImageLarge.rectTransform.rect` and pins are anchored at
   its bottom-left (the maths in §1.4 assumes this).
2. Whether the large map has a `Mask`/`RectMask2D` (affects whether our own clipping is
   strictly necessary).
3. Names in `Game.instance.m_portalPrefabs`.
4. That a far-away partner actually arrives after `RequestZDO` from a non-host client.

---

## 3. Phases

| Version | Scope |
|---|---|
| 0.1 | Registry (live + partner fetch + refresh), large-map lines, config, `portallines dump`. Verify §2.7 on the rig. |
| 0.2 | Disk cache, faded remembered portals, presumed links from remembered partner positions. (Conflict/orphan flags and pins shipped in 0.1.) |
| 0.3 | Hover highlight and tooltip, small-map option, colour polish, README/screenshots, Thunderstore release. |
| 0.4 | Biome gradient line colouring (default). |
| later | Route planner; tag-in-use warning; cleanup list; optional server component for full-world knowledge on dedicated servers. |

---

## 4. Decisions (taken 2026-09-18)

1. **Name: PortalLines** (`com.jumpingmushroom.portallines`). `PortalMap` clashes with
   korCaptain's Thunderstore package *PotalMap*, a map-click teleport mod whose README calls
   itself "PortalMap". Directory, project, assembly and GUID all use the new name.
2. **Data source: client-only, learns as you play.** No server install. The registry is written
   so a server broadcast can be added later as one more source.
3. **v0.1 draws lines and pins.**
4. **Small minimap: config option, off by default.**

Implementation note from 0.1: `Minimap.GetClosestPin` only considers pins with `m_save`, so
unsaved pins are already immune to click, check and right-click delete — no `RemovePin` guard is
needed. `AddPin` is not called either: its optional `PlatformUserID` parameter would drag in
`Splatform.dll`, so the pin is built by hand the way `AddPin` builds it.
