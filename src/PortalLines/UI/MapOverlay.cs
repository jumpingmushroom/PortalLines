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

        public bool Attached => _large != null;

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
                _large = Create(map.m_pinRootLarge, map.m_mapImageLarge, true);
                _route = CreateRoute(map.m_pinRootLarge, map.m_mapImageLarge);
            }

            bool wantSmall = PluginConfig.ShowOnMinimap.Value;
            if (wantSmall && _small == null)
                _small = Create(map.m_pinRootSmall, map.m_mapImageSmall, false);
            else if (!wantSmall && _small != null)
            {
                Object.Destroy(_small.gameObject);
                _small = null;
            }
        }

        public void MarkStyleDirty()
        {
            if (_large != null) _large.MarkStyleDirty();
            if (_small != null) _small.MarkStyleDirty();
        }

        public void Destroy()
        {
            if (_large != null) Object.Destroy(_large.gameObject);
            if (_small != null) Object.Destroy(_small.gameObject);
            if (_route != null) Object.Destroy(_route.gameObject);
            _large = null;
            _small = null;
            _route = null;
            _map = null;
        }

        private static RouteGraphic CreateRoute(RectTransform root, RawImage image)
        {
            if (root == null || image == null)
                return null;
            var go = new GameObject("PortalLines.Route", typeof(RectTransform), typeof(CanvasRenderer), typeof(RouteGraphic));
            go.layer = root.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(1); // above the lines, below every pin
            var g = go.GetComponent<RouteGraphic>();
            g.MapImage = image;
            g.raycastTarget = false;
            g.color = Color.white;
            return g;
        }

        private static PortalLinesGraphic Create(RectTransform root, RawImage image, bool large)
        {
            if (root == null || image == null)
                return null;

            var go = new GameObject(large ? "PortalLines.Large" : "PortalLines.Small",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(PortalLinesGraphic));
            go.layer = root.gameObject.layer;

            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.SetAsFirstSibling(); // beneath every pin

            var g = go.GetComponent<PortalLinesGraphic>();
            g.MapImage = image;
            g.IsLarge = large;
            g.raycastTarget = false; // never eat map clicks
            g.color = Color.white;   // vertex colours carry the real colour
            return g;
        }
    }
}
