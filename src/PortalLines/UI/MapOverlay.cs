using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// Owns the line graphics on the large map and, optionally, the minimap. The Minimap object
    /// lives in the game scene and dies on logout, taking our children with it; EnsureAttached
    /// is called every frame and rebuilds when the instance changes or a child has gone.
    /// </summary>
    internal sealed class MapOverlay
    {
        private Minimap _map;
        private PortalLinesGraphic _large;
        private PortalLinesGraphic _small;
        private RouteGraphic _route;
        private RouteGraphic _smallRoute;

        public void EnsureAttached(Minimap map)
        {
            if (map == null)
            {
                Destroy();
                return;
            }

            if (!ReferenceEquals(map, _map) || _large == null)
            {
                Destroy();
                _map = map;
                _large = Create<PortalLinesGraphic>(map.m_pinRootLarge, map.m_mapImageLarge, true, "PortalLines.Large", 0);
                _route = Create<RouteGraphic>(map.m_pinRootLarge, map.m_mapImageLarge, true, "PortalLines.Route", 1);
            }

            bool changed = false;
            bool wantSmall = PluginConfig.ShowOnMinimap.Value;
            if (wantSmall && _small == null)
            {
                _small = Create<PortalLinesGraphic>(map.m_pinRootSmall, map.m_mapImageSmall, false, "PortalLines.Small", 0);
                changed = true;
            }
            else if (!wantSmall && _small != null)
            {
                Object.Destroy(_small.gameObject);
                _small = null;
                changed = true;
            }

            bool wantSmallRoute = PluginConfig.RouteOnMinimap.Value && PluginConfig.RouteEnabled.Value;
            if (wantSmallRoute && _smallRoute == null)
            {
                _smallRoute = Create<RouteGraphic>(map.m_pinRootSmall, map.m_mapImageSmall, false, "PortalLines.RouteSmall", 1);
                changed = true;
            }
            else if (!wantSmallRoute && _smallRoute != null)
            {
                Object.Destroy(_smallRoute.gameObject);
                _smallRoute = null;
            }

            // Lines beneath the route, both beneath every pin. Vanilla appends pins at the end.
            if (changed)
            {
                if (_small != null) _small.transform.SetAsFirstSibling();
                if (_smallRoute != null) _smallRoute.transform.SetSiblingIndex(_small != null ? 1 : 0);
            }
        }

        public void MarkStyleDirty()
        {
            if (_large != null) _large.MarkStyleDirty();
            if (_small != null) _small.MarkStyleDirty();
            if (_route != null) _route.MarkStyleDirty();
            if (_smallRoute != null) _smallRoute.MarkStyleDirty();
        }

        public void Destroy()
        {
            if (_large != null) Object.Destroy(_large.gameObject);
            if (_small != null) Object.Destroy(_small.gameObject);
            if (_route != null) Object.Destroy(_route.gameObject);
            if (_smallRoute != null) Object.Destroy(_smallRoute.gameObject);
            _large = null;
            _small = null;
            _route = null;
            _smallRoute = null;
            _map = null;
        }

        /// <summary>
        /// A graphic filling the pin root, at <paramref name="siblingIndex"/>: 0 for lines, 1 for
        /// the route above them, both beneath every pin.
        /// </summary>
        private static T Create<T>(RectTransform root, RawImage image, bool large, string name, int siblingIndex)
            where T : MapGraphic
        {
            if (root == null || image == null)
                return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.layer = root.gameObject.layer;

            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(siblingIndex);

            var g = go.GetComponent<T>();
            g.MapImage = image;
            g.IsLarge = large;
            g.raycastTarget = false; // never eat map clicks
            g.color = Color.white;   // vertex colours carry the real colour
            return g;
        }
    }
}
