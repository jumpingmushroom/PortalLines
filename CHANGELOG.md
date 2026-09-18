# Changelog

## 0.1.0 — first cut

- Lines on the large map between every pair of connected portals the client knows about.
- A portal pin with the tag at each known portal; unconnected portals are tinted.
- Far ends of known portals are fetched from the server with the game's own `RequestZDO`, so a
  link is drawn as soon as you have stood near either end.
- `portallines` console command for listing what is known and forcing a refresh.
