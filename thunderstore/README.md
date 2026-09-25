# Portal Lines

Your portals as a network you can see and use from the map: every portal you know about, a line
to the portal it is connected to, and a **route planner** that finds the fastest way to anywhere
through them.

**Shift-click a spot on the map.** The mod works out where to walk, which portal to take and where
you come out, draws the route, and tells you what it saves: *613 m on foot instead of 3.5 km*.
Close the map and the **minimap follows the route**: the leg to walk next, and an arrow on the
rim pointing at it when it is out of view. An **arrow at the top of the screen** points the way
relative to where you are looking, with the distance underneath, and tells you which portal to
enter when you reach it. The route clears itself when you arrive.

![Route](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/route.jpg)

- **Lines** between connected portals, blending from the biome colour of one end to the other so
  each line tells you where it goes. Dashed means the pair is not confirmed yet.
- **Hover** a portal: its line lights up, the rest dim, and a panel shows tag, destination biome,
  distance and link state.
- **Pins** at every known portal with its tag. Unconnected portals are tinted red; a tag shared by
  three or more portals (only one pair can ever connect) is tinted orange. Pins are never saved
  or shared through the cartography table, and the map's portal icon filter hides them.
- A **"Portal lines" checkbox** on the map hides the lines; hovering still peeks at one.
- **Remembers** portals per world between sessions, drawn faded until confirmed.

![Portal lines](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/lines.jpg)

![Hover](https://raw.githubusercontent.com/jumpingmushroom/PortalLines/v0.5.0/docs/images/hover.jpg)

Client-side only; nothing to install on the server. The mod knows every portal you have been near
and the far end of each, fetched with the same request the game makes for the portal you are
standing next to. The hosting player and singleplayer see the whole world.

Console: `portallines list | links | refresh | forget | route <x> <z> | route <tag> | route clear`.

Requires BepInEx and Jotunn. Source and issues: https://github.com/jumpingmushroom/PortalLines
