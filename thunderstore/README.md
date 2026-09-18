# Portal Lines

Draws every portal you know about on the large map, with a line to the portal it is connected to.

- Lines blend from the biome colour of one end to the other, so each tells you where it goes
  (per-tag and single-colour modes too). Dashed means the pair is not confirmed yet.
- Every known portal gets a pin with its tag. Unconnected portals are tinted red; a tag shared by
  three or more portals (only one pair can ever connect) is tinted orange.
- Shift-click anywhere to plan the fastest route there through the portal network: dotted
  walking legs, bright hops, and the total on foot against the direct distance.
- Hover a portal to highlight its line and see tag, destination biome, distance and link state.
  A "Portal lines" checkbox on the map hides the lines.
- Pins are never saved and never shared through the cartography table. The map's portal icon
  filter hides them.

![Portal lines](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/lines.jpg)

**Hover** a portal: its line lights up, the rest dim, and the panel says where it goes.

![Hover](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/hover.jpg)

**Route planner.** Shift-click a spot: 613 m on foot via the Trader portal instead of 3.5 km direct.

![Route](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/route.jpg)

Client-side only; nothing to install on the server. The mod knows every portal you have been near
and the far end of each, fetched with the same request the game makes for the portal you are
standing next to, and remembers them per world between sessions (drawn faded until confirmed).
The hosting player and singleplayer see the whole world.

Console: `portallines list | links | refresh | forget`.

Requires BepInEx and Jotunn. Source and issues: https://github.com/jumpingmushroom/PortalLines
