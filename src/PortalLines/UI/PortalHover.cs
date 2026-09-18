using System;
using System.Text;
using PortalLines.Core;
using PortalLines.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>The portal under the cursor, shared between the hover logic and the line graphic.</summary>
    internal static class HoverState
    {
        public static PortalEntry Entry;
        public static int Version;

        public static void Set(PortalEntry e)
        {
            string was = Entry != null ? Entry.Key : null;
            string now = e != null ? e.Key : null;
            if (was == now)
            {
                Entry = e;
                return;
            }
            Entry = e;
            Version++;
        }
    }

    /// <summary>
    /// Finds the known portal nearest the cursor on the large map and shows a small panel with
    /// its tag, link state, destination biome and distances. While a portal is hovered the line
    /// graphic dims every other line, which is what makes a crowded network readable.
    /// </summary>
    internal sealed class PortalHover
    {
        private Minimap _map;
        private RectTransform _panel;
        private TMP_Text _text;
        private string _lastText;

        public void Update(Minimap map, PortalSnapshot snap)
        {
            if (map == null)
            {
                Clear();
                return;
            }
            if (!ReferenceEquals(map, _map))
            {
                DestroyPanel();
                _map = map;
            }

            if (!PluginConfig.HoverEnabled.Value || map.m_mode != Minimap.MapMode.Large
                || Minimap.InTextInput() || !ZInput.IsMouseActive() || snap.Portals.Count == 0)
            {
                Clear();
                return;
            }

            Vector3 pointer = ZInput.pointerPosition;
            Vector3 world = map.ScreenToWorldPoint(pointer);
            if (world == Vector3.zero)
            {
                Clear();
                return;
            }

            // Hover radius is in screen pixels; convert to metres at the current zoom.
            Rect uv = map.m_mapImageLarge.uvRect;
            Rect rect = map.m_mapImageLarge.rectTransform.rect;
            float metresPerPixel = uv.width * map.m_textureSize * map.m_pixelSize / rect.width;
            float radius = PluginConfig.HoverRadius.Value * metresPerPixel;

            PortalEntry best = null;
            float bestDist = radius;
            for (int i = 0; i < snap.Portals.Count; i++)
            {
                float d = Utils.DistanceXZ(snap.Portals[i].Pos, world);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = snap.Portals[i];
                }
            }

            HoverState.Set(best);
            if (best == null)
            {
                HidePanel();
                return;
            }

            ShowPanel(map, best, pointer);
        }

        public void Clear()
        {
            HoverState.Set(null);
            HidePanel();
        }

        public void Destroy()
        {
            HoverState.Set(null);
            DestroyPanel();
            _map = null;
        }

        private void ShowPanel(Minimap map, PortalEntry e, Vector3 pointer)
        {
            if (_panel == null && !CreatePanel(map))
                return;

            string content = Describe(e);
            if (content != _lastText)
            {
                _lastText = content;
                _text.text = content;
                Vector2 size = _text.GetPreferredValues(content);
                _panel.sizeDelta = new Vector2(Mathf.Ceil(size.x) + 20f, Mathf.Ceil(size.y) + 14f);
            }

            var root = map.m_largeRoot.transform as RectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pointer, null, out local))
                return;

            // Anchor bottom-left of the root, pivot top-left of the panel; keep it on screen by
            // flipping to the other side of the cursor when it would run off an edge.
            Rect rr = root.rect;
            float x = local.x - rr.xMin + 18f;
            float y = local.y - rr.yMin - 18f;
            if (x + _panel.sizeDelta.x > rr.width)
                x = local.x - rr.xMin - 18f - _panel.sizeDelta.x;
            if (y - _panel.sizeDelta.y < 0f)
                y = local.y - rr.yMin + 18f + _panel.sizeDelta.y;
            _panel.anchoredPosition = new Vector2(x, y);

            if (!_panel.gameObject.activeSelf)
                _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
        }

        private void HidePanel()
        {
            if (_panel != null && _panel.gameObject.activeSelf)
                _panel.gameObject.SetActive(false);
        }

        private void DestroyPanel()
        {
            if (_panel != null)
                UnityEngine.Object.Destroy(_panel.gameObject);
            _panel = null;
            _text = null;
            _lastText = null;
        }

        private bool CreatePanel(Minimap map)
        {
            if (map.m_largeRoot == null || map.m_biomeNameLarge == null)
                return false;

            var go = new GameObject("PortalLinesHover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map.m_largeRoot.transform, false);
            _panel = go.transform as RectTransform;
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0f, 1f);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.03f, 0.82f);
            bg.raycastTarget = false;

            // Clone the biome label to inherit the map's font and material, then restyle it.
            GameObject textGo = UnityEngine.Object.Instantiate(map.m_biomeNameLarge.gameObject, _panel);
            textGo.name = "Text";
            foreach (Component c in textGo.GetComponents<Component>())
                if (c != null && c.GetType().Name == "Localize")
                    UnityEngine.Object.Destroy(c);
            _text = textGo.GetComponent<TMP_Text>();
            var trt = textGo.transform as RectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.pivot = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(10f, 7f);
            trt.offsetMax = new Vector2(-10f, -7f);
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.fontSize = 17f;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.richText = true;
            _text.color = Color.white;
            _text.raycastTarget = false;
            _text.text = "";

            go.SetActive(false);
            return true;
        }

        private static string Describe(PortalEntry e)
        {
            var sb = new StringBuilder(160);
            Color c;
            switch (PluginConfig.ColorMode.Value)
            {
                case LineColorMode.Biome: c = MapMath.BiomeColor(e.Biome); break;
                case LineColorMode.Single: c = PluginConfig.SingleColor.Value; break;
                default: c = MapMath.TagColor(e.Tag); break;
            }
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append("><b>")
              .Append(e.HasTag ? e.Tag : "(no tag)").Append("</b></color>");
            if (e.PrefabName.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0)
                sb.Append("  <alpha=#99>stone portal");
            sb.Append('\n');

            if (e.Linked)
            {
                PortalEntry other = e.Link.Other(e);
                sb.Append(e.Link.Kind == LinkKind.Confirmed ? "Linked" : "Presumed link")
                  .Append(" → <color=#").Append(ColorUtility.ToHtmlStringRGB(MapMath.BiomeColor(other.Biome))).Append('>')
                  .Append(BiomeName(other.Pos)).Append("</color>")
                  .Append(", ").Append(Dist(e.Link.Distance));
                if (e.Link.Kind != LinkKind.Confirmed && !string.IsNullOrEmpty(e.Link.Reason))
                    sb.Append("\n<alpha=#99>").Append(e.Link.Reason);
            }
            else if (e.Conflict)
            {
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(PluginConfig.ConflictColor.Value))
                  .Append(">Not connected</color>: tag used by ").Append(e.TagCount)
                  .Append(" portals, only one pair can connect");
            }
            else if (!e.PartnerId.IsNone() && !e.PartnerLoaded)
            {
                sb.Append("Linked, partner not received yet");
            }
            else
            {
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(PluginConfig.UnlinkedColor.Value))
                  .Append(">Not connected</color>");
            }

            Player p = Player.m_localPlayer;
            if (p != null)
                sb.Append("\n<alpha=#99>").Append(Dist(Utils.DistanceXZ(p.transform.position, e.Pos))).Append(" from you");

            if (e.Remembered)
                sb.Append("\n<alpha=#99>Remembered, last seen ").Append(Ago(e.LastSeen));

            return sb.ToString();
        }

        private static string BiomeName(Vector3 pos)
        {
            WorldGenerator wg = WorldGenerator.instance;
            if (wg == null)
                return "?";
            try
            {
                return wg.GetBiomeSector(pos).GetName();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string Dist(float m)
        {
            return m < 1000f ? Mathf.RoundToInt(m) + " m" : (m / 1000f).ToString("0.0") + " km";
        }

        private static string Ago(long unix)
        {
            long s = PortalCache.Now() - unix;
            if (s < 120) return "just now";
            if (s < 7200) return (s / 60) + " min ago";
            if (s < 172800) return (s / 3600) + " h ago";
            return (s / 86400) + " d ago";
        }
    }
}
