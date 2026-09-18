# Portal Lines

Draws every portal you know about on the large map, with a line to the portal it is connected to.

- Lines are coloured by tag. Dashed means the pair is not confirmed yet.
- Every known portal gets a pin with its tag. Unconnected portals are tinted red; a tag shared by
  three or more portals (only one pair can ever connect) is tinted orange.
- Pins are never saved and never shared through the cartography table. The map's portal icon
  filter hides them.

Client-side only; nothing to install on the server. The mod knows every portal you have been near
this session and the far end of each, fetched with the same request the game makes for the portal
you are standing next to. The hosting player and singleplayer see the whole world.

Console: `portallines list | links | refresh`.

Requires BepInEx and Jotunn. Source and issues: https://github.com/jumpingmushroom/PortalLines
