using PortalLines.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// Draws the planned route: dotted walking legs, bright hop lines, a disc at each portal
    /// entered and a ring at the destination. Sits between the portal lines and the pins.
    /// </summary>
    public sealed class RouteGraphic : MaskableGraphic
    {
        public RawImage MapImage;

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

            Vector2 d = MapMath.WorldToLocal(map, RouteState.Destination, uv, rect);
            if (clip.Contains(d))
            {
                LineMesh.AddRing(vh, d, 11f, 6f, outline);
                LineMesh.AddRing(vh, d, 11f, 3.5f, walk);
                LineMesh.AddDisc(vh, d, 3f, walk);
            }
        }
    }
}
