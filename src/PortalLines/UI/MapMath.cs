using UnityEngine;

namespace PortalLines.UI
{
    /// <summary>
    /// The same arithmetic Minimap uses to place pins, so lines land on the map pixel-for-pixel.
    ///
    /// Minimap.WorldToMapPoint: world XZ → [0,1] texture space.
    /// Minimap.MapPointToLocalGuiPos: texture space → pixels within the map RectTransform, given
    /// the RawImage's current uvRect (which is how zoom and pan are expressed).
    /// </summary>
    internal static class MapMath
    {
        public static Vector2 WorldToMapPoint(Minimap map, Vector3 p)
        {
            float half = map.m_textureSize / 2;
            float mx = (p.x / map.m_pixelSize + half) / map.m_textureSize;
            float my = (p.z / map.m_pixelSize + half) / map.m_textureSize;
            return new Vector2(mx, my);
        }

        public static Vector2 MapPointToLocal(Vector2 mp, Rect uv, Rect rect)
        {
            return new Vector2(
                (mp.x - uv.xMin) / uv.width * rect.width,
                (mp.y - uv.yMin) / uv.height * rect.height);
        }

        public static Vector2 WorldToLocal(Minimap map, Vector3 p, Rect uv, Rect rect)
        {
            return MapPointToLocal(WorldToMapPoint(map, p), uv, rect);
        }

        /// <summary>Liang–Barsky. Returns false when the segment lies entirely outside the rect.</summary>
        public static bool ClipToRect(ref Vector2 a, ref Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x, dy = b.y - a.y;

            if (!Clip(-dx, a.x - r.xMin, ref t0, ref t1)) return false;
            if (!Clip(dx, r.xMax - a.x, ref t0, ref t1)) return false;
            if (!Clip(-dy, a.y - r.yMin, ref t0, ref t1)) return false;
            if (!Clip(dy, r.yMax - a.y, ref t0, ref t1)) return false;

            Vector2 na = new Vector2(a.x + t0 * dx, a.y + t0 * dy);
            Vector2 nb = new Vector2(a.x + t1 * dx, a.y + t1 * dy);
            a = na;
            b = nb;
            return true;
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f)
                return q >= 0f;

            float t = q / p;
            if (p < 0f)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }

        /// <summary>A stable, well-separated colour for a tag. Untagged portals get a neutral grey.</summary>
        public static Color TagColor(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return new Color(0.82f, 0.82f, 0.82f);

            // Golden-ratio hue stepping over the hash gives neighbours in hash space distinct hues.
            uint h = (uint)tag.GetStableHashCode();
            float hue = (h * 0.6180339887f) % 1f;
            return Color.HSVToRGB(hue, 0.72f, 1f);
        }
    }
}
