# Land-aware routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Walking legs follow land round water, and portal choice accounts for it.

**Architecture:** A background Dijkstra from the destination over a 24 m grid built from
`WaterMap` (portal links as reversed teleport edges) yields a next-step field; the route is
extracted from the player's cell every 5 m and smoothed. The 0.8.1 straight-line planner stays as
the fallback (no field yet, no water map, on a ship).

**Tech Stack:** C# net472, BepInEx 5, Unity 2022 (Valheim 1.0.12), no test project.

**Spec:** `docs/superpowers/specs/2026-10-08-land-routing-design.md`

## Global Constraints

- Build: `./build/deploy.sh` (needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, set by the script).
- Never commit unless the user asks; never add AI attribution to commits.
- Version stays 0.9.0 (already bumped in Plugin.cs, csproj, manifest).
- Match surrounding style: XML `<summary>` on types and non-obvious members, no `var` avoidance.
- Main-thread only: Unity objects, `PortalEntry` reads. The search thread sees only plain data.

---

### Task 1: Water bits snapshot

**Files:** Modify `src/PortalLines/Core/WaterMap.cs`

**Interfaces — Produces:** `sealed class WaterBits { int Size; float Pixel; bool IsWater(int px, int py) }`,
`WaterMap.Bits` (null until ready, immutable once published), `WaterMap.WaterAlong` unchanged.

- [ ] Wrap the bitset, size and pixel size in `WaterBits`; publish it when the copy completes.
- [ ] Build: `dotnet build -c Release src/PortalLines/PortalLines.csproj` → 0 errors.

### Task 2: LandField search

**Files:** Create `src/PortalLines/Core/LandField.cs`

**Interfaces — Produces:**
- `struct FieldKey { Vector3 Dest; int Snapshot; int Water; float HopCost; float Penalty; bool SameDest(FieldKey) }`
- `sealed class FieldLink { Vector3 Entrance; Vector3 Exit; PortalEntry Portal; PortalLink Link; }`
- `sealed class LandField { FieldKey Key; List<FieldLink> Links; int Side; float CellSize; float[] Cost; byte[] Next; Dictionary<int,int> PortalAt; double Ms; int Settled; int CellOf(Vector3); Vector3 Center(int); int Step(int cell, int dir) }`
  with `Next` values 0–7 = direction, `TakePortal` = 8, `End` = 255.
- `static class LandFieldJob { Start(FieldKey, List<FieldLink>, WaterBits); bool Running(FieldKey); LandField TakeResult(); Cancel(); string Status }`

- [ ] Coarse grid: a cell is water when ≥2 of its 2×2 texels are; cells beyond world radius 10524 m are walls.
- [ ] Reverse Dijkstra (binary heap, lazy deletion), 8 neighbours, cost = cell × (1|√2) × mean factor; exits relax entrances at +hop with `Next = TakePortal`.
- [ ] Thread-pool job with generation-based cancel every 4096 pops; result published under a lock.
- [ ] Build → 0 errors.

### Task 3: Planner integration

**Files:** Modify `src/PortalLines/Core/RoutePlanner.cs`, `src/PortalLines/Core/ConsoleCommands.cs`, `src/PortalLines/PluginConfig.cs`

**Interfaces — Consumes:** Task 1–2. **Produces:** `RouteLeg.Path` (`List<Vector3>`, From…To),
`Route.Planning`, `Route.Straight`, `RouteState.NextWaypoint()`, `RouteState.DistanceToLegEnd(Vector3)`,
`RouteState.FieldStatus`.

- [ ] Remove the pair cache and water cost from the straight planner; give straight legs a two-point `Path`.
- [ ] `Update`: compute the key (null on ship / no water map), start a job when the field does not match, adopt finished results, keep a stale field with the same destination while a new one runs.
- [ ] Extraction: follow `Next` from the player's cell, split at portals, smooth by forward string pulling (shortcut allowed if water ≤ path water + 12 m), drop walk legs under 1 m.
- [ ] `route show` prints field status; WaterPenalty description updated.
- [ ] Build → 0 errors.

### Task 4: Display

**Files:** Modify `src/PortalLines/UI/RouteGraphic.cs`, `src/PortalLines/UI/HudArrow.cs`, `src/PortalLines/UI/RouteInput.cs`; docs `README.md`, `CHANGELOG.md`, `PLAN.md`

- [ ] Large map: dotted segments along `Path`. Minimap: first leg from the live position through the remaining points; rim arrow at `NextWaypoint()`.
- [ ] HUD arrow heading to `NextWaypoint()`, distance from `DistanceToLegEnd`.
- [ ] Panel: walk total from `Walking`, `planning…` while `Planning`.
- [ ] Docs describe land-following routes; CHANGELOG 0.9.0 rewritten.
- [ ] `./build/deploy.sh`, deploy to phoenix Default profile, in-game checks from the spec's Testing section.
