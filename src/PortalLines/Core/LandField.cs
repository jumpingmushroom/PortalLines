using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.Core
{
    /// <summary>What a field was computed for; a field is current while all of it still holds.</summary>
    public struct FieldKey
    {
        public Vector3 Dest;
        public int Snapshot;
        public int Water;
        public float HopCost;
        public float Penalty;

        public bool SameDest(FieldKey o)
        {
            return Dest == o.Dest && Water == o.Water;
        }

        public bool Same(FieldKey o)
        {
            return SameDest(o) && Snapshot == o.Snapshot && HopCost == o.HopCost && Penalty == o.Penalty;
        }
    }

    /// <summary>One usable direction of a portal link: step in at Entrance, come out at Exit.</summary>
    public sealed class FieldLink
    {
        public Vector3 Entrance;
        public Vector3 Exit;
        public PortalEntry Portal; // main thread only
        public PortalLink Link;    // main thread only
    }

    /// <summary>
    /// The cheapest way from every 24 m cell of the world to one destination, walking or through
    /// portals. A search outward from the destination: stepping to a neighbouring cell costs its
    /// length, times the water penalty where the cell is swim water, and a portal link is an edge
    /// from its entrance's cell to its exit's cell costing the hop cost. Each cell keeps the first
    /// step of its best path, so the route from wherever the player stands is read off by following
    /// <see cref="Next"/>, with no search per move.
    /// </summary>
    public sealed class LandField
    {
        public const byte TakePortal = 8;
        public const byte End = 255;

        /// <summary>The direction of step d; opposite is (d + 4) &amp; 7.</summary>
        private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>World radius plus a margin; beyond it there is nothing to walk to.</summary>
        private const float WorldEdge = 10524f;

        public readonly FieldKey Key;
        public readonly List<FieldLink> Links;
        public readonly int Side;
        public readonly float CellSize;
        public readonly float[] Cost;
        public readonly byte[] Next;
        public readonly Dictionary<int, int> PortalAt = new Dictionary<int, int>();
        public double Ms;
        public int Settled;

        private readonly float _half;

        private LandField(FieldKey key, List<FieldLink> links, WaterBits water)
        {
            Key = key;
            Links = links;
            Side = water.Size / 2;
            CellSize = water.Pixel * 2f;
            _half = Side * 0.5f;
            Cost = new float[Side * Side];
            Next = new byte[Side * Side];
        }

        /// <summary>The cell holding p, or -1 off the grid.</summary>
        public int CellOf(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / CellSize + _half);
            int cy = Mathf.FloorToInt(p.z / CellSize + _half);
            if (cx < 0 || cy < 0 || cx >= Side || cy >= Side)
                return -1;
            return cy * Side + cx;
        }

        public Vector3 Center(int cell)
        {
            int cx = cell % Side, cy = cell / Side;
            return new Vector3((cx - _half + 0.5f) * CellSize, 0f, (cy - _half + 0.5f) * CellSize);
        }

        /// <summary>The cell one step in direction d from cell.</summary>
        public int Step(int cell, int d)
        {
            return cell + Dy[d] * Side + Dx[d];
        }

        /// <summary>Runs on a worker thread. Returns null when cancelled.</summary>
        internal static LandField Compute(FieldKey key, List<FieldLink> links, WaterBits water, Func<bool> cancelled)
        {
            var watch = Stopwatch.StartNew();
            var f = new LandField(key, links, water);
            int side = f.Side, n = side * side;

            // Coarse terrain: 1 land, penalty water, 0 a wall beyond the world's edge.
            var factor = new float[n];
            float edge2 = WorldEdge * WorldEdge;
            for (int cy = 0; cy < side; cy++)
            {
                for (int cx = 0; cx < side; cx++)
                {
                    int c = cy * side + cx;
                    Vector3 p = f.Center(c);
                    if (p.x * p.x + p.z * p.z > edge2)
                        continue;
                    int wet = 0;
                    if (water.IsWater(cx * 2, cy * 2)) wet++;
                    if (water.IsWater(cx * 2 + 1, cy * 2)) wet++;
                    if (water.IsWater(cx * 2, cy * 2 + 1)) wet++;
                    if (water.IsWater(cx * 2 + 1, cy * 2 + 1)) wet++;
                    factor[c] = wet >= 2 ? key.Penalty : 1f;
                }
            }

            // Reversed links: settling an exit's cell offers its entrance's cell.
            var byExit = new Dictionary<int, List<int>>();
            var entranceCell = new int[links.Count];
            for (int i = 0; i < links.Count; i++)
            {
                entranceCell[i] = f.CellOf(links[i].Entrance);
                int exit = f.CellOf(links[i].Exit);
                if (entranceCell[i] < 0 || exit < 0)
                    continue;
                List<int> list;
                if (!byExit.TryGetValue(exit, out list))
                    byExit[exit] = list = new List<int>();
                list.Add(i);
            }

            for (int i = 0; i < n; i++)
            {
                f.Cost[i] = float.PositiveInfinity;
                f.Next[i] = End;
            }

            int start = f.CellOf(key.Dest);
            if (start < 0)
                return f;
            f.Cost[start] = 0f;
            var heap = new Heap(1 << 16);
            heap.Push(0f, start);

            float straight = f.CellSize, diagonal = f.CellSize * 1.41421356f;
            int pops = 0;
            while (heap.Count > 0)
            {
                float cu;
                int u = heap.Pop(out cu);
                if (cu > f.Cost[u])
                    continue;
                f.Settled++;
                if ((++pops & 4095) == 0 && cancelled())
                    return null;

                int ux = u % side, uy = u / side;
                float fu = factor[u] > 0f ? factor[u] : 1f; // the destination may lie off the edge
                for (int d = 0; d < 8; d++)
                {
                    int vx = ux + Dx[d], vy = uy + Dy[d];
                    if (vx < 0 || vy < 0 || vx >= side || vy >= side)
                        continue;
                    int v = vy * side + vx;
                    float fv = factor[v];
                    if (fv <= 0f)
                        continue;
                    float c = cu + ((d & 1) == 0 ? straight : diagonal) * 0.5f * (fu + fv);
                    if (c < f.Cost[v])
                    {
                        f.Cost[v] = c;
                        f.Next[v] = (byte)((d + 4) & 7); // from v, step back towards u
                        heap.Push(c, v);
                    }
                }

                List<int> entering;
                if (byExit.TryGetValue(u, out entering))
                {
                    for (int k = 0; k < entering.Count; k++)
                    {
                        int li = entering[k];
                        int v = entranceCell[li];
                        float c = cu + key.HopCost;
                        if (c < f.Cost[v])
                        {
                            f.Cost[v] = c;
                            f.Next[v] = TakePortal;
                            f.PortalAt[v] = li;
                            heap.Push(c, v);
                        }
                    }
                }
            }

            f.Ms = watch.Elapsed.TotalMilliseconds;
            return f;
        }

        /// <summary>Binary min-heap of (cost, cell) with lazy deletion.</summary>
        private sealed class Heap
        {
            private float[] _keys;
            private int[] _vals;
            public int Count;

            public Heap(int capacity)
            {
                _keys = new float[capacity];
                _vals = new int[capacity];
            }

            public void Push(float key, int val)
            {
                if (Count == _keys.Length)
                {
                    Array.Resize(ref _keys, Count * 2);
                    Array.Resize(ref _vals, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key)
                        break;
                    _keys[i] = _keys[p];
                    _vals[i] = _vals[p];
                    i = p;
                }
                _keys[i] = key;
                _vals[i] = val;
            }

            public int Pop(out float key)
            {
                key = _keys[0];
                int top = _vals[0];
                Count--;
                float k = _keys[Count];
                int v = _vals[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= Count)
                        break;
                    if (c + 1 < Count && _keys[c + 1] < _keys[c])
                        c++;
                    if (_keys[c] >= k)
                        break;
                    _keys[i] = _keys[c];
                    _vals[i] = _vals[c];
                    i = c;
                }
                _keys[i] = k;
                _vals[i] = v;
                return top;
            }
        }
    }

    /// <summary>
    /// Runs <see cref="LandField.Compute"/> on a thread-pool thread, one job at a time in effect:
    /// starting a job cancels the one before, and only the newest result is kept.
    /// </summary>
    public static class LandFieldJob
    {
        private static readonly object _lock = new object();
        private static int _generation;
        private static LandField _result;
        private static FieldKey _runningKey;
        private static bool _running;
        private static bool _failed;
        private static Exception _error;

        /// <summary>A job for this key is running, or failed and should not be retried.</summary>
        public static bool Pending(FieldKey key)
        {
            lock (_lock)
                return (_running || _failed) && _runningKey.Same(key);
        }

        /// <summary>The job for this key threw; the planner falls back to straight lines.</summary>
        public static bool Failed(FieldKey key)
        {
            lock (_lock)
                return _failed && _runningKey.Same(key);
        }

        public static void Start(FieldKey key, List<FieldLink> links, WaterBits water)
        {
            int gen;
            lock (_lock)
            {
                gen = ++_generation;
                _running = true;
                _failed = false;
                _runningKey = key;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                LandField f = null;
                Exception error = null;
                try
                {
                    f = LandField.Compute(key, links, water, () => Volatile.Read(ref _generation) != gen);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                lock (_lock)
                {
                    if (gen != _generation)
                        return;
                    _running = false;
                    _failed = error != null;
                    _result = f;
                    _error = error;
                }
            });
        }

        /// <summary>The newest finished field, once; null when there is none new.</summary>
        public static LandField TakeResult()
        {
            Exception error;
            LandField f;
            lock (_lock)
            {
                f = _result;
                error = _error;
                _result = null;
                _error = null;
            }
            if (error != null)
                PortalLinesPlugin.Log.LogWarning("route search failed: " + error);
            return f;
        }

        public static void Cancel()
        {
            lock (_lock)
            {
                _generation++;
                _running = false;
                _failed = false;
                _result = null;
                _error = null;
            }
        }
    }
}
