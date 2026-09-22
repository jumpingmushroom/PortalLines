using PortalLines.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// Draws the planned route: dotted walking legs, bright hop lines, a disc at each portal
    /// entered and a ring at the destination. Sits between the portal lines and the pins.
    ///
    /// On the minimap it is a navigation aid instead: only the walking legs, drawn from the live
    /// player position (the route itself recomputes every 5 m, which would visibly lag the
    /// marker), with hop lines left out because they point somewhere you are not walking. The
    /// first leg is cut at an inscribed circle and finished with an arrowhead when the next
    /// waypoint is out of view.
    /// </summary>
    public sealed class RouteGraphic : MaskableGraphic
    {
        public RawImage MapImage;
        public bool IsLarge = true;

        private Rect _lastUv;
        private Rect _lastRect;
        private int _lastVersion = -1;

        private void LateUpdate()
        {
            if (MapImage == null)
                return;
            Rect uv = MapImage.uvRect;
            Rect rect = MapImage.rectTransform.rect;
            if (uv != _lastUv || rect != _lastRect || RouteState.Version != _lastVersion)
            {
                _lastUv = uv;
                _lastRect = rect;
                _lastVersion = RouteState.Version;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Minimap map = Minimap.instance;
            if (map == null || MapImage == null || !RouteState.Active)
                return;

            Rect uv = MapImage.uvRect;
            Rect rect = MapImage.rectTransform.rect;
            var clip = new Rect(-16f, -16f, rect.width + 32f, rect.height + 32f);
            Color walk = PluginConfig.RouteColor.Value;
            var outline = new Color(0f, 0f, 0f, 0.6f);
            float width = PluginConfig.LineWidth.Value;

            if (!IsLarge)
            {
                PopulateSmall(vh, map, uv, rect, clip, walk, outline, width * 0.66f);
                return;
            }

            Route route = RouteState.Current;
            if (route != null)
            {
                for (int i = 0; i < route.Legs.Count; i++)
                {
                    RouteLeg leg = route.Legs[i];
                    Vector2 a = MapMath.WorldToLocal(map, leg.From, uv, rect);
                    Vector2 b = MapMath.WorldToLocal(map, leg.To, uv, rect);
                    if (!MapMath.ClipToRect(ref a, ref b, clip))
                        continue;

                    if (leg.Kind == LegKind.Walk)
                    {
                        LineMesh.AddLine(vh, a, b, width + 2f, outline, outline, 7f, 7f);
                        LineMesh.AddLine(vh, a, b, width, walk, walk, 7f, 7f);
                    }
                    else
                    {
                        Color ca = MapMath.BiomeColor(leg.Link.A.Biome);
                        Color cb = MapMath.BiomeColor(leg.Link.B.Biome);
                        if (PluginConfig.ColorMode.Value != LineColorMode.Biome)
                            ca = cb = walk;
                        // Orient the gradient to the leg direction.
                        bool forward = leg.Link.A.Key == leg.Portal.Key;
                        Color c0 = forward ? ca : cb, c1 = forward ? cb : ca;
                        c0.a = c1.a = 1f;
                        LineMesh.AddLine(vh, a, b, width + 3.5f, outline, outline);
                        LineMesh.AddLine(vh, a, b, width + 1.5f, c0, c1);
                    }
                }

                // A disc where you step in and where you come out.
                for (int i = 0; i < route.Legs.Count; i++)
                {
                    RouteLeg leg = route.Legs[i];
                    if (leg.Kind != LegKind.Hop)
                        continue;
                    foreach (Vector3 p in new[] { leg.From, leg.To })
                    {
                        Vector2 c = MapMath.WorldToLocal(map, p, uv, rect);
                        if (!clip.Contains(c))
                            continue;
                        LineMesh.AddDisc(vh, c, 7f, outline);
                        LineMesh.AddDisc(vh, c, 5f, walk);
                    }
                }
            }

            AddDestination(vh, MapMath.WorldToLocal(map, RouteState.Destination, uv, rect), clip, walk, outline);
        }

        private static void AddDestination(VertexHelper vh, Vector2 d, Rect clip, Color walk, Color outline)
        {
            if (!clip.Contains(d))
                return;
            LineMesh.AddRing(vh, d, 11f, 6f, outline);
            LineMesh.AddRing(vh, d, 11f, 3.5f, walk);
            LineMesh.AddDisc(vh, d, 3f, walk);
        }

        private static void PopulateSmall(VertexHelper vh, Minimap map, Rect uv, Rect rect, Rect clip,
            Color walk, Color outline, float width)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            // The small map is centred on the player every frame, so this is the rect centre.
            Vector2 me = MapMath.WorldToLocal(map, player.transform.position, uv, rect);
            const float arrowLen = 12f;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - arrowLen * 0.5f - 4f;

            Route route = RouteState.Current;
            if (route != null)
            {
                for (int i = 0; i < route.Legs.Count; i++)
                {
                    RouteLeg leg = route.Legs[i];
                    if (leg.Kind != LegKind.Walk)
                        continue;
                    Vector2 a = i == 0 ? me : MapMath.WorldToLocal(map, leg.From, uv, rect);
                    Vector2 b = MapMath.WorldToLocal(map, leg.To, uv, rect);
                    if (i == 0)
                    {
                        // Stop at the circle so the arrowhead caps the line instead of sitting on it.
                        Vector2 d = b - a;
                        float len = d.magnitude;
                        if (len > radius - arrowLen)
                            b = a + d / len * (radius - arrowLen);
                    }
                    if (!MapMath.ClipToRect(ref a, ref b, clip))
                        continue;
                    LineMesh.AddLine(vh, a, b, width + 2f, outline, outline, 7f, 7f);
                    LineMesh.AddLine(vh, a, b, width, walk, walk, 7f, 7f);
                }

                for (int i = 0; i < route.Legs.Count; i++)
                {
                    RouteLeg leg = route.Legs[i];
                    if (leg.Kind != LegKind.Hop)
                        continue;
                    foreach (Vector3 p in new[] { leg.From, leg.To })
                    {
                        Vector2 c = MapMath.WorldToLocal(map, p, uv, rect);
                        if (!clip.Contains(c))
                            continue;
                        LineMesh.AddDisc(vh, c, 5.5f, outline);
                        LineMesh.AddDisc(vh, c, 4f, walk);
                    }
                }
            }

            AddDestination(vh, MapMath.WorldToLocal(map, RouteState.Destination, uv, rect), clip, walk, outline);

            // Next waypoint out of view: an arrow on the rim pointing at it.
            Vector2 next = MapMath.WorldToLocal(map, RouteState.NextWaypoint(), uv, rect) - me;
            float dist = next.magnitude;
            if (dist <= radius || dist < 0.001f)
                return;
            Vector2 dir = next / dist;
            Vector2 tip = me + dir * radius;
            LineMesh.AddArrowHead(vh, tip + dir * 1.5f, dir, arrowLen + 3f, 8f, outline);
            LineMesh.AddArrowHead(vh, tip, dir, arrowLen, 6f, walk);
        }
    }
}
