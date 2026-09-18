using PortalLines.Core;
using PortalLines.Model;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// One mesh of line quads, parented under the map's pin root so it uses the pins' coordinate
    /// frame and clips under whatever mask the map has. Rebuilt only when the view (uvRect, rect)
    /// or the registry snapshot changes; idle while the map is closed because the root is inactive.
    /// </summary>
    public sealed class PortalLinesGraphic : MaskableGraphic
    {
        private const float DashLength = 14f;
        private const float GapLength = 9f;

        public RawImage MapImage;
        public bool IsLarge = true;

        private Rect _lastUv;
        private Rect _lastRect;
        private int _lastVersion = -1;
        private int _lastHover = -1;
        private bool _styleDirty = true;


        public void MarkStyleDirty()
        {
            _styleDirty = true;
        }

        private void LateUpdate()
        {
            if (MapImage == null)
                return;

            Rect uv = MapImage.uvRect;
            Rect rect = MapImage.rectTransform.rect;
            int version = PortalRegistry.Snapshot.Version;
            int hover = IsLarge ? HoverState.Version + RouteState.Version * 1000 : 0;

            if (_styleDirty || uv != _lastUv || rect != _lastRect || version != _lastVersion || hover != _lastHover)
            {
                _lastUv = uv;
                _lastRect = rect;
                _lastVersion = version;
                _lastHover = hover;
                _styleDirty = false;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Minimap map = Minimap.instance;
            if (map == null || MapImage == null)
                return;

            PortalSnapshot snap = PortalRegistry.Snapshot;
            if (snap.Links.Count == 0)
                return;

            // With lines switched off, hovering a portal still peeks at its own line.
            bool linesOn = PluginConfig.LinesEnabled.Value;
            string focus = IsLarge && HoverState.Entry != null ? HoverState.Entry.Key : null;
            Route route = IsLarge && RouteState.Active ? RouteState.Current : null;
            bool routeUsesPortals = route != null && route.UsesPortals;
            if (!linesOn && focus == null && !routeUsesPortals)
                return;
            float focusDim = PluginConfig.FocusDim.Value;

            Rect uv = MapImage.uvRect;
            Rect rect = MapImage.rectTransform.rect;
            float width = PluginConfig.LineWidth.Value * (IsLarge ? 1f : 0.66f);
            float alpha = PluginConfig.LineAlpha.Value;
            bool outline = PluginConfig.Outline.Value;
            bool showPresumed = PluginConfig.ShowPresumed.Value;
            LineColorMode mode = PluginConfig.ColorMode.Value;
            Color singleColor = PluginConfig.SingleColor.Value;
            float rememberedAlpha = PluginConfig.RememberedAlpha.Value;

            // Local space: pivot (0,0) and a rect matching the pin root, so (0,0) is the map's
            // bottom-left exactly as it is for pin anchoredPositions.
            float pad = width + 2f;
            var clip = new Rect(-pad, -pad, rect.width + 2f * pad, rect.height + 2f * pad);
            var outlineColor = new Color(0f, 0f, 0f, 0.55f * alpha);

            for (int i = 0; i < snap.Links.Count; i++)
            {
                PortalLink link = snap.Links[i];
                bool dashed = link.Kind == LinkKind.Presumed;
                if (dashed && !showPresumed)
                    continue;

                bool focused = focus != null && (link.A.Key == focus || link.B.Key == focus);
                bool onRoute = routeUsesPortals && RouteUses(route, link);
                if (!linesOn && !focused && !onRoute)
                    continue;

                Vector2 a = MapMath.WorldToLocal(map, link.A.Pos, uv, rect);
                Vector2 b = MapMath.WorldToLocal(map, link.B.Pos, uv, rect);
                if (!MapMath.ClipToRect(ref a, ref b, clip))
                    continue;

                // Two colours, one per end; equal unless in Biome mode. Vertex colours interpolate,
                // so the gradient is free.
                Color ca, cb;
                switch (mode)
                {
                    case LineColorMode.Biome:
                        ca = MapMath.BiomeColor(link.A.Biome);
                        cb = MapMath.BiomeColor(link.B.Biome);
                        break;
                    case LineColorMode.Single:
                        ca = cb = singleColor;
                        break;
                    default:
                        ca = cb = MapMath.TagColor(link.Tag);
                        break;
                }

                float la = alpha * (dashed ? 0.85f : 1f);
                if (link.AnyRemembered)
                    la *= rememberedAlpha;

                float w = width;
                if (focus != null || routeUsesPortals)
                {
                    if (focused)
                    {
                        la = Mathf.Max(la, 0.95f);
                        w += 1.5f;
                    }
                    else if (onRoute)
                    {
                        // The route graphic redraws this link brighter; keep the base copy out of the way.
                        la *= focusDim;
                    }
                    else
                    {
                        la *= focusDim;
                    }
                }
                ca.a = la;
                cb.a = la;
                Color oc = outlineColor;
                oc.a = 0.55f * la;

                // The endpoints were clipped to the view; keep the gradient anchored to the real
                // ends so panning does not slide the colours along the line.
                Vector2 fa = MapMath.WorldToLocal(map, link.A.Pos, uv, rect);
                Vector2 fb = MapMath.WorldToLocal(map, link.B.Pos, uv, rect);
                float full = (fb - fa).magnitude;
                float ta = full > 0.001f ? Vector2.Dot(a - fa, (fb - fa) / full) / full : 0f;
                float tb = full > 0.001f ? Vector2.Dot(b - fa, (fb - fa) / full) / full : 1f;
                Color cStart = Color.LerpUnclamped(ca, cb, ta);
                Color cEnd = Color.LerpUnclamped(ca, cb, tb);

                float dash = dashed ? DashLength : 0f, gap = dashed ? GapLength : 0f;
                if (outline)
                    LineMesh.AddLine(vh, a, b, w + 2f, oc, oc, dash, gap);
                LineMesh.AddLine(vh, a, b, w, cStart, cEnd, dash, gap);
            }
        }

        private static bool RouteUses(Route route, PortalLink link)
        {
            for (int i = 0; i < route.Legs.Count; i++)
                if (route.Legs[i].Kind == LegKind.Hop && ReferenceEquals(route.Legs[i].Link, link))
                    return true;
            return false;
        }
    }
}
