using PortalLines.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// A heading arrow at the top centre of the HUD while a route is set, with the next target
    /// and its distance underneath. It points relative to the camera, since that is what you
    /// steer by: straight up is "run where you are looking". Beside the portal the route wants
    /// you to step into, the arrow gives way to an "Enter portal" prompt.
    ///
    /// Parented under <c>Hud.m_rootObject</c>, which the game moves off screen to hide the HUD,
    /// so Ctrl+F3, death and cutscenes hide it for free. The raid event bar and a boss health
    /// bar share the top centre; the arrow drops below whichever of them is showing.
    /// </summary>
    internal sealed class HudArrow
    {
        /// <summary>Within this many metres of the portal to enter, prompt instead of pointing.</summary>
        private const float EnterDistance = 4f;
        private const float AlignedAngle = 15f;
        private const float UnalignedAlpha = 0.5f;
        private const float TopMargin = 24f;
        private const float ObstacleGap = 10f;
        private const float ArrowSize = 34f;

        private Hud _hud;
        private RectTransform _root;
        private ArrowGraphic _arrow;
        private TMP_Text _text;
        private string _lastKey;

        public void Update(Minimap map, bool large)
        {
            Hud hud = Hud.instance;
            Player player = Player.m_localPlayer;
            if (hud == null || player == null || map == null)
            {
                Destroy();
                return;
            }
            if (!ReferenceEquals(hud, _hud) || _root == null)
            {
                Destroy();
                if (!Create(hud, map))
                    return;
            }

            if (large || !RouteState.Active || !PluginConfig.RouteEnabled.Value || !PluginConfig.ShowHudArrow.Value
                || InventoryGui.IsVisible())
            {
                Hide();
                return;
            }

            Vector3 pos = player.transform.position;
            Route route = RouteState.Current;
            RouteLeg hop = NextHop(route);
            Vector3 target = RouteState.NextWaypoint();
            float dist = Utils.DistanceXZ(pos, target);
            bool enter = hop != null && (route.Legs[0].Kind == LegKind.Hop || dist <= EnterDistance);

            string key;
            if (enter)
                key = "enter|" + hop.Portal.Key;
            else
                key = (hop != null ? "portal|" + hop.Portal.Key : "dest") + "|" + DistKey(dist);
            if (key != _lastKey)
            {
                _lastKey = key;
                _text.text = enter ? EnterText(hop) : WalkText(hop, dist);
            }

            _arrow.gameObject.SetActive(!enter);
            if (!enter)
            {
                float angle = Heading(pos, target);
                _arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
                _arrow.canvasRenderer.SetAlpha(Mathf.Abs(angle) <= AlignedAngle ? 1f : UnalignedAlpha);
                Color c = PluginConfig.RouteColor.Value;
                c.a = 1f;
                if (_arrow.color != c)
                    _arrow.color = c;
            }

            float scale = PluginConfig.HudArrowScale.Value;
            _root.localScale = new Vector3(scale, scale, 1f);
            Place(hud);

            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root.gameObject);
            _root = null;
            _arrow = null;
            _text = null;
            _hud = null;
            _lastKey = null;
        }

        private bool Create(Hud hud, Minimap map)
        {
            if (hud.m_rootObject == null || map.m_biomeNameLarge == null)
                return false;
            var parent = hud.m_rootObject.transform as RectTransform;
            if (parent == null)
                return false;

            var go = new GameObject("PortalLinesHudArrow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.transform as RectTransform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.sizeDelta = new Vector2(360f, ArrowSize + 30f);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(ArrowGraphic));
            arrowGo.transform.SetParent(_root, false);
            _arrow = arrowGo.GetComponent<ArrowGraphic>();
            _arrow.raycastTarget = false;
            var art = _arrow.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            art.anchoredPosition = new Vector2(0f, -ArrowSize * 0.5f);

            // The map's biome label, like MapPanel, for the game's font and material.
            GameObject textGo = Object.Instantiate(map.m_biomeNameLarge.gameObject, _root);
            textGo.name = "Text";
            foreach (Component c in textGo.GetComponents<Component>())
                if (c != null && c.GetType().Name == "Localize")
                    Object.Destroy(c);
            textGo.SetActive(true);
            _text = textGo.GetComponent<TMP_Text>();
            var trt = textGo.transform as RectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 26f);
            trt.anchoredPosition = new Vector2(0f, -ArrowSize - 2f);
            _text.alignment = TextAlignmentOptions.Top;
            _text.fontSize = 18f;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.richText = true;
            _text.color = Color.white;
            // Over open sky or snow, not the dark map: outline it (on an instanced material).
            _text.outlineWidth = 0.2f;
            _text.outlineColor = new Color32(0, 0, 0, 200);
            _text.raycastTarget = false;
            _text.text = "";

            _hud = hud;
            go.SetActive(false);
            return true;
        }

        /// <summary>The hop that follows the current walking leg, or the leg itself when it is a hop.</summary>
        private static RouteLeg NextHop(Route route)
        {
            if (route == null || route.Legs.Count == 0)
                return null;
            if (route.Legs[0].Kind == LegKind.Hop)
                return route.Legs[0];
            if (route.Legs.Count > 1 && route.Legs[1].Kind == LegKind.Hop)
                return route.Legs[1];
            return null;
        }

        /// <summary>Degrees from the camera's heading to the target, positive to the right.</summary>
        private static float Heading(Vector3 from, Vector3 to)
        {
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform
                : Camera.main != null ? Camera.main.transform : null;
            if (cam == null)
                return 0f;
            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f)
                return 0f;
            return Vector3.SignedAngle(fwd, dir, Vector3.up);
        }

        /// <summary>Changes exactly when <see cref="RouteInput.Dist"/> would print something different.</summary>
        private static int DistKey(float m)
        {
            return m < 1000f ? Mathf.RoundToInt(m) : 100000 + Mathf.RoundToInt(m / 100f);
        }

        private static string WalkText(RouteLeg hop, float dist)
        {
            if (hop == null)
                return "Destination  <alpha=#AA>" + RouteInput.Dist(dist);
            return "Portal " + TagText(hop) + "  <alpha=#AA>" + RouteInput.Dist(dist);
        }

        private static string EnterText(RouteLeg hop)
        {
            Color c = MapMath.BiomeColor(hop.Link.Other(hop.Portal).Biome);
            return "Enter portal " + TagText(hop) + " → <color=#" + ColorUtility.ToHtmlStringRGB(c) + ">"
                + RouteInput.BiomeName(hop.To) + "</color>";
        }

        private static string TagText(RouteLeg hop)
        {
            return hop.Portal.HasTag ? "\"" + hop.Portal.Tag + "\"" : "(no tag)";
        }

        /// <summary>Top centre, dropped below the raid bar or boss health bar when one is up.</summary>
        private void Place(Hud hud)
        {
            var parent = _root.parent as RectTransform;
            float top = parent.rect.yMax - TopMargin - PluginConfig.HudArrowOffsetY.Value;
            float halfWidth = _root.sizeDelta.x * 0.5f * _root.localScale.x;

            if (hud.m_eventBar != null && hud.m_eventBar.activeInHierarchy)
                top = Below(parent, hud.m_eventBar.transform as RectTransform, top, halfWidth);

            EnemyHud enemy = EnemyHud.instance;
            if (enemy != null && enemy.m_huds != null)
                foreach (var kv in enemy.m_huds)
                {
                    if (kv.Key == null || !kv.Key.IsBoss() || kv.Value.m_gui == null || !kv.Value.m_gui.activeInHierarchy)
                        continue;
                    top = Below(parent, kv.Value.m_gui.transform.Find("Health") as RectTransform, top, halfWidth);
                    if (kv.Value.m_name != null)
                        top = Below(parent, kv.Value.m_name.rectTransform, top, halfWidth);
                }

            _root.anchoredPosition = new Vector2(0f, top - parent.rect.yMax);
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>
        /// Lowers <paramref name="top"/> beneath <paramref name="rt"/> if the two share the top
        /// centre, working in the parent's local space: the obstacle may sit on another canvas.
        /// </summary>
        private static float Below(RectTransform parent, RectTransform rt, float top, float halfWidth)
        {
            if (rt == null)
                return top;
            Camera ours = CanvasCamera(parent);
            Camera theirs = CanvasCamera(rt);
            rt.GetWorldCorners(s_corners);
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(theirs, s_corners[i]);
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, ours, out local))
                    return top;
                xMin = Mathf.Min(xMin, local.x);
                xMax = Mathf.Max(xMax, local.x);
                yMin = Mathf.Min(yMin, local.y);
            }
            Rect pr = parent.rect;
            float centre = pr.center.x;
            bool overlapsX = xMax > centre - halfWidth && xMin < centre + halfWidth;
            bool upperHalf = yMin > pr.center.y;
            if (overlapsX && upperHalf && yMin - ObstacleGap < top)
                return yMin - ObstacleGap;
            return top;
        }

        private static Camera CanvasCamera(Transform t)
        {
            Canvas canvas = t.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }

    /// <summary>A dart pointing up its rect, outlined so it reads over snow and sky.</summary>
    public sealed class ArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float h = Mathf.Min(r.width, r.height) * 0.5f - 2f;
            if (h <= 0f)
                return;
            Vector2 c = r.center;
            AddDart(vh, c, h + 2.5f, new Color(0f, 0f, 0f, 0.65f));
            AddDart(vh, c, h, color);
        }

        private static void AddDart(VertexHelper vh, Vector2 c, float h, Color col)
        {
            Vector2 tip = c + new Vector2(0f, h);
            Vector2 left = c + new Vector2(-h * 0.8f, -h);
            Vector2 right = c + new Vector2(h * 0.8f, -h);
            Vector2 notch = c + new Vector2(0f, -h * 0.45f);
            int start = vh.currentVertCount;
            vh.AddVert(LineMesh.Vert(tip, col));
            vh.AddVert(LineMesh.Vert(left, col));
            vh.AddVert(LineMesh.Vert(notch, col));
            vh.AddVert(LineMesh.Vert(right, col));
            vh.AddTriangle(start, start + 2, start + 1);
            vh.AddTriangle(start, start + 3, start + 2);
        }
    }
}
