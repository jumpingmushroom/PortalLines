# Land-aware routing — design

Date: 2026-10-08. Supersedes the straight-line water penalty tried earlier the same day (0.9.0 dev
build).

## Problem

Walking legs are straight lines, so the planner can only choose between straight lines. With a
water penalty it picks the least-wet straight line, which is still wrong when the sensible way is a
curved walk round a bay (south past Overgang), and it invents portal round trips just to approach
on a parallel, dry line (west of Eqmoderr: two hops and 1.7 km instead of a 1.0 km walk).

## Goal

Walking parts of a route follow land, bending round fjords, bays and the sea, and the choice of
portals accounts for that. Water stays usable at `WaterPenalty` (default 5) times its length.
While on a ship, water is free and the route is the straight-line plan.

## Components

### `Core/WaterMap` (exists)

12 m bitset of swim water from the minimap height texture (ground below sea level − 1.6 m). Built
in ~43 ms over 32 frames. Gains: a coarse view for the grid (`CellWater(cx, cy)`, true when at
least 2 of the 4 texels of a 24 m cell are water) and accessors for size and pixel size.

### `Core/LandField` (new)

A cost-to-destination field over 24 m cells (1024² for a 2048-texel map).

- Input (immutable, captured on the main thread): destination, water map reference, hop cost,
  water penalty, and the usable links as (entrance position, exit position, portal key) pairs.
- Search: Dijkstra outward from the destination cell with a binary heap. Step to each of the 8
  neighbours costs `cell * (1 or √2) * mean(factor(a), factor(b))`, factor = penalty on water, 1 on
  land. Hop edges are reversed: when the cell holding a link's exit is settled at cost c, the cell
  holding its entrance is relaxed at c + hop cost, marked "take portal k". Cells outside the world
  radius plus one cell are skipped.
- Output: `float[] Cost`, `byte[] Next` (0–7 direction, 8 = take a portal, 255 = destination or
  unreached) and a cell → link index map for portal cells. ~5 MB.
- Runs on a thread-pool thread. A newer job cancels an older one (checked every 4096 pops); the
  main thread only adopts a finished result whose inputs still match. Records elapsed ms and cells
  settled.

### `Core/RoutePlanner` (changed)

- `RouteState` keeps the active field and the inputs it was built from. Each `Update`, when the
  inputs (destination, snapshot version, hop cost, penalty, water map version) differ from the
  field's and no matching job is running, it starts one.
- Every 5 m, or when a field arrives: with a matching field, not on a ship, extract the route by
  following `Next` from the player's cell. Walk cells collect into a polyline, a portal step closes
  the walk leg, adds a hop leg and continues from the exit's cell. Polylines start at the player's
  real position and end at the real portal or destination position.
- Smoothing: greedy string pulling over the cell path; a shortcut from anchor i to j is allowed when
  its swim water (`WaterMap.WaterAlong`) is no more than the cell path's water between them + 12 m.
- No field yet for this destination: a route with no legs, `Planning` set (in-game, the straight
  route flashed the very fjord crossing being avoided for ~0.5 s). Water map not ready,
  `Ship.GetLocalShip() != null`, penalty 1 or a failed search: the 0.8.1 straight-line Dijkstra,
  no water costs. The pair cache from the dev build is removed.
- `RouteLeg` gains `List<Vector3> Path` (From … To; two points for straight legs). `Route` gains
  `Planning` (true while a better field is being computed) and `Straight` (true when from the
  straight planner). `Walking` is the polyline length, `Water` the swim water along it.
- `NextWaypoint()`: the first polyline point of the first leg more than 10 m from the player, else
  the leg end. New `DistanceToLegEnd(pos)`: along the remaining polyline to the next portal or the
  destination.

### Display

- `RouteGraphic` large map: walk legs drawn segment by segment along `Path`.
- `RouteGraphic` minimap: first walk leg starts at the live player position and continues through
  `Path` points; the rim arrow points at `NextWaypoint()`.
- `HudArrow`: heading to `NextWaypoint()`; distance label from `DistanceToLegEnd`; the "Enter
  portal" check uses the same distance.
- Panel: `planning…` suffix while `Planning`; water as before.
- `portallines route show`: adds the field status (`24 m grid, N cells, M ms`, or `computing`, or
  `straight: on a ship`).

### Config

`Route.WaterPenalty` (exists, 1–20, default 5) now weights grid steps. `HopCost` unchanged. Either
changing starts a new field.

## Limits

Generated terrain only (no terraforming, bridges); mountains not costed; features under ~24 m
wide may be missed; portals are entered at their cell.

## Testing

No test project. In game: the three screenshot targets (west of Eqmoderr, south past Overgang, the
original target across the fjord from the Trader), a target needing a real water crossing, a target
while sailing, and the timings from `portallines route show` and the log.
