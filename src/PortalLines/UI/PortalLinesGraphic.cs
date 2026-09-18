using System.Collections.Generic;
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

        /// <summary>Guard against pathological zoom: a 4000 px line at 14 px dashes is 170 quads.</summary>
        private const int MaxDashesPerLine = 512;

        public RawImage MapImage;
        public bool IsLarge = true;

        private Rect _lastUv;
        private Rect _lastRect;
        private int _lastVersion = -1;
        private int _lastHover = -1;
        private bool _styleDirty = true;

        private static readonly List<UIVertex> s_verts = new List<UIVertex>(4);

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
            int hover = IsLarge ? HoverState.Version : 0;

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
            if (!linesOn && focus == null)
                return;
            float focusDim = PluginConfig.FocusDim.Value;

            Rect uv = MapImage.uvRect;
            Rect rect = MapImage.rectTransform.rect;
            float width = PluginConfig.LineWidth.Value * (IsLarge ? 1f : 0.66f);
            float alpha = PluginConfig.LineAlpha.Value;
            bool outline = PluginConfig.Outline.Value;
            bool showPresumed = PluginConfig.ShowPresumed.Value;
            bool single = PluginConfig.ColorMode.Value == LineColorMode.Single;
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
                if (!linesOn && !focused)
                    continue;

                Vector2 a = MapMath.WorldToLocal(map, link.A.Pos, uv, rect);
                Vector2 b = MapMath.WorldToLocal(map, link.B.Pos, uv, rect);
                if (!MapMath.ClipToRect(ref a, ref b, clip))
                    continue;

                Color c = single ? singleColor : MapMath.TagColor(link.Tag);
                c.a = alpha * (dashed ? 0.85f : 1f);
                if (link.AnyRemembered)
                    c.a *= rememberedAlpha;

                float w = width;
                if (focus != null)
                {
                    if (focused)
                    {
                        c.a = Mathf.Max(c.a, 0.95f);
                        w += 1.5f;
                    }
                    else
                    {
                        c.a *= focusDim;
                    }
                }
                Color oc = outlineColor;
                oc.a = 0.55f * c.a;

                if (outline)
                    AddLine(vh, a, b, w + 2f, oc, dashed);
                AddLine(vh, a, b, w, c, dashed);
            }
        }

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color, bool dashed)
        {
            if (!dashed)
            {
                AddQuad(vh, a, b, width, color);
                return;
            }

            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f)
                return;

            Vector2 dir = d / len;
            float period = DashLength + GapLength;
            int dashes = Mathf.Min(MaxDashesPerLine, Mathf.CeilToInt(len / period));
            float t = 0f;
            for (int i = 0; i < dashes && t < len; i++)
            {
                float end = Mathf.Min(len, t + DashLength);
                AddQuad(vh, a + dir * t, a + dir * end, width, color);
                t += period;
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, float width, Color color)
        {
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 0.001f)
                return;

            Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);

            // Extend the ends by half the width so consecutive dashes and joins do not show gaps.
            Vector2 e = d / len * (width * 0.5f);
            p0 -= e;
            p1 += e;

            int start = vh.currentVertCount;
            s_verts.Clear();
            s_verts.Add(Vert(p0 - n, color));
            s_verts.Add(Vert(p0 + n, color));
            s_verts.Add(Vert(p1 + n, color));
            s_verts.Add(Vert(p1 - n, color));
            for (int i = 0; i < 4; i++)
                vh.AddVert(s_verts[i]);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private static UIVertex Vert(Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            return v;
        }
    }
}
