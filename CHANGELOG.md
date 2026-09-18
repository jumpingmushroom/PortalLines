# Changelog

## 0.4.0 — biome gradient

- New default line colouring: each end takes the colour of the biome it sits in and the line
  blends between them, so a line tells you where it goes. Per-tag and single colour remain as
  `ColorMode` options. The hover panel names the destination biome in the same colour.
- The nine biome colours are configurable ("Biome colours" section, live in ConfigurationManager).
  Black Forest is a teal-pine rather than the map's green so a Meadows-to-Black-Forest line
  actually changes colour.

## 0.3.0 — map toggle and hover

- A "Portal lines" checkbox on the large map, cloned from the map's own toggles and placed one
  row above them. Also a configurable hotkey (unbound by default).
- Hovering a portal on the large map highlights its line and dims every other line, and shows
  a panel with the tag, link state, destination biome, distance, distance from you, and for a
  remembered portal when it was last seen. With lines switched off, hovering still shows that
  one portal's line.
- Presumed links now say why they are presumed.

## 0.2.0 — remembers portals between sessions

- Portals are remembered on disk per world (`BepInEx/config/PortalLines/<world>-<id>.tsv`), so
  the map is populated from the moment you log in instead of only after walking near each
  portal again. Remembered portals and their lines are drawn faded until a live copy confirms
  them, then switch to full strength.
- Remembered links come from where the pair was last seen connected, falling back to the only
  two known portals with a tag. Both are drawn dashed as presumed until the server confirms.
- A remembered portal whose area is loaded and in range but has no live copy is forgotten after
  a short grace period: it was demolished.
- New config: `RememberPortals`, `ForgetAfterDays` (0 keeps forever), `RememberedAlpha`.
- New console command `portallines forget` clears this world's memory.

## 0.1.1 — verification fixes

- The one-shot map layout report now runs after the first scan, so its portal counts are real.
- Console command output is mirrored to the BepInEx log, so it can be read back from a file.
- `build/deploy.sh` replaces the DLL atomically. Overwriting it in place while the game runs
  corrupts the memory-mapped assembly and breaks the next server connection.

## 0.1.0 — first cut

- Lines on the large map between every pair of connected portals the client knows about.
- A portal pin with the tag at each known portal; unconnected portals are tinted.
- Far ends of known portals are fetched from the server with the game's own `RequestZDO`, so a
  link is drawn as soon as you have stood near either end.
- `portallines` console command for listing what is known and forcing a refresh.
