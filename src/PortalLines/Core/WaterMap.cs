using System.Diagnostics;
using UnityEngine;

namespace PortalLines.Core
{
    /// <summary>The finished water bitset; immutable, so the route search thread can read it.</summary>
    public sealed class WaterBits
    {
        public readonly int Size;
        public readonly float Pixel;
        private readonly ulong[] _bits;

        public WaterBits(ulong[] bits, int size, float pixel)
        {
            _bits = bits;
            Size = size;
            Pixel = pixel;
        }

        /// <summary>Texel (px, py) is swim water; off the map is the world's edge ocean.</summary>
        public bool IsWater(int px, int py)
        {
            if (px < 0 || py < 0 || px >= Size || py >= Size)
                return true;
            int i = py * Size + px;
            return (_bits[i >> 6] & (1UL << (i & 63))) != 0;
        }
    }

    /// <summary>
    /// Which parts of the world you would have to swim through, for the route planner.
    ///
    /// The minimap already holds the generated terrain height of the whole world in
    /// <c>m_heightTexture</c> (one texel per <c>m_pixelSize</c> metres; the game reads it back with
    /// GetPixel itself). This copies it, a few rows per frame, into one bit per texel: set where the
    /// ground is deeper than swim depth below sea level. Shallow fords stay land. Terraforming and
    /// bridges are not in the texture, so they are not seen.
    /// </summary>
    public static class WaterMap
    {
        /// <summary>Character.m_swimDepth (2) less the 0.4 slack IsSwimming allows.</summary>
        private const float SwimDepth = 1.6f;
        private const int RowsPerFrame = 64;

        /// <summary>Bumped when the map becomes ready or is dropped, so cached costs are redone.</summary>
        public static int Version { get; private set; }
        public static bool Ready => Bits != null;

        /// <summary>The finished bitset, or null while building.</summary>
        public static WaterBits Bits { get; private set; }
        public static string Status { get; private set; } = "not built";

        private static ulong[] _bits;
        private static int _size;
        private static float _pixel;
        private static float _threshold;
        private static int _rowsDone;
        private static int _waterCount;
        private static int _frames;
        private static Minimap _source;
        private static readonly Stopwatch _watch = new Stopwatch();

        public static void Clear()
        {
            if (_bits == null && _source == null)
                return;
            _bits = null;
            Bits = null;
            _source = null;
            _rowsDone = 0;
            _size = 0;
            Status = "not built";
            Version++;
        }

        /// <summary>Call every frame; does a slice of the copy until it is done.</summary>
        public static void Update(Minimap map)
        {
            if (map == null || !map.m_hasGenerated || map.m_heightTexture == null)
                return;

            if (!ReferenceEquals(map, _source))
            {
                Clear();
                _source = map;
                _size = map.m_textureSize;
                _pixel = map.m_pixelSize;
                float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                _threshold = sea - SwimDepth;
                _bits = new ulong[((long)_size * _size + 63) / 64];
                _waterCount = 0;
                _frames = 0;
                _watch.Reset();
                Status = "building";
            }

            if (Ready || _bits == null)
                return;

            _watch.Start();
            try
            {
                int rows = Mathf.Min(RowsPerFrame, _size - _rowsDone);
                Color[] px = map.m_heightTexture.GetPixels(0, _rowsDone, _size, rows);
                for (int r = 0; r < rows; r++)
                {
                    int rowBase = (_rowsDone + r) * _size;
                    for (int x = 0; x < _size; x++)
                    {
                        if (px[r * _size + x].r >= _threshold)
                            continue;
                        int i = rowBase + x;
                        _bits[i >> 6] |= 1UL << (i & 63);
                        _waterCount++;
                    }
                }
                _rowsDone += rows;
                _frames++;
            }
            catch (System.Exception ex)
            {
                // Unreadable texture or the like: route as before rather than retry every frame.
                PortalLinesPlugin.Log.LogWarning("water map: could not read the minimap heights: " + ex.Message);
                _bits = null;
                Status = "unavailable";
                return;
            }
            finally
            {
                _watch.Stop();
            }

            if (_rowsDone >= _size)
            {
                Bits = new WaterBits(_bits, _size, _pixel);
                Version++;
                Status = string.Format("{0}x{0} at {1:0} m, {2:0}% swim water, built in {3} ms over {4} frames",
                    _size, _pixel, 100.0 * _waterCount / ((double)_size * _size), _watch.ElapsedMilliseconds, _frames);
                PortalLinesPlugin.Log.LogInfo("water map: " + Status);
            }
        }

        /// <summary>Metres of swim water on the straight line from a to b; 0 until the map is ready.</summary>
        public static float WaterAlong(Vector3 a, Vector3 b)
        {
            WaterBits bits = Bits;
            if (bits == null)
                return 0f;
            float d = Utils.DistanceXZ(a, b);
            if (d < 1f)
                return 0f;

            float pixel = bits.Pixel;
            int steps = Mathf.CeilToInt(d / pixel);
            float half = bits.Size * 0.5f;
            float dx = (b.x - a.x) / steps, dz = (b.z - a.z) / steps;
            float x = a.x + dx * 0.5f, z = a.z + dz * 0.5f;
            int wet = 0;
            for (int s = 0; s < steps; s++, x += dx, z += dz)
            {
                if (bits.IsWater(Mathf.FloorToInt(x / pixel + half), Mathf.FloorToInt(z / pixel + half)))
                    wet++;
            }
            return d * wet / steps;
        }
    }
}
