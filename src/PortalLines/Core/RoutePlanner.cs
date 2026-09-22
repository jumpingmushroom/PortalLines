using System.Collections.Generic;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.Core
{
    public enum LegKind { Walk, Hop }

    public sealed class RouteLeg
    {
        public LegKind Kind;
        public Vector3 From;
        public Vector3 To;
        public float Distance;
        public PortalLink Link; // for hops
        public PortalEntry Portal; // the portal entered, for hops
    }

    public sealed class Route
    {
        public readonly List<RouteLeg> Legs = new List<RouteLeg>();
        public float Walking;
        public float Direct;
        public int Hops;
        public bool AnyPresumed;
        public bool UsesPortals => Hops > 0;
    }

    /// <summary>
    /// Shortest route from the player to a chosen destination over the portal network.
    ///
    /// Dijkstra over a small complete graph: the start, the destination and every known portal,
    /// with straight-line walking between any two and a fixed cost for stepping through a link.
    /// Straight-line walking ignores water and mountains, the same simplification the map makes.
    /// A few hundred portals is a few hundred thousand relaxations, well under a millisecond.
    /// </summary>
    public static class RouteState
    {
        public static bool Active { get; private set; }
        public static Vector3 Destination { get; private set; }
        public static Route Current { get; private set; }
        public static int Version { get; private set; }

        private static Vector3 _lastStart;
        private static int _lastSnapshot = -1;
        private static bool _wasAway;

        public static void Set(Vector3 destination)
        {
            Active = true;
            Destination = destination;
            _lastSnapshot = -1;
            _wasAway = false;
            Version++;
        }

        /// <summary>
        /// True, and the route cleared, when the player has come within <paramref name="radius"/>
        /// of the destination after having been farther away, so a destination set next to you
        /// does not vanish on the spot.
        /// </summary>
        public static bool CheckArrival(Vector3 pos, float radius)
        {
            if (!Active || radius <= 0f)
                return false;
            float d = Utils.DistanceXZ(pos, Destination);
            if (d > radius * 1.5f)
                _wasAway = true;
            if (!_wasAway || d > radius)
                return false;
            Clear();
            return true;
        }

        /// <summary>The point to head for next: the end of the first walking leg, else the destination.</summary>
        public static Vector3 NextWaypoint()
        {
            Route r = Current;
            if (r != null && r.Legs.Count > 0 && r.Legs[0].Kind == LegKind.Walk)
                return r.Legs[0].To;
            return Destination;
        }

        public static void Clear()
        {
            if (!Active)
                return;
            Active = false;
            Current = null;
            _lastSnapshot = -1;
            Version++;
        }

        /// <summary>Recompute when the start moved or the network changed.</summary>
        public static void Update(PortalSnapshot snap, Vector3 start)
        {
            if (!Active)
                return;
            if (_lastSnapshot == snap.Version && Utils.DistanceXZ(start, _lastStart) < 5f && Current != null)
                return;

            _lastSnapshot = snap.Version;
            _lastStart = start;
            Current = Compute(snap, start, Destination);
            Version++;
        }

        private static Route Compute(PortalSnapshot snap, Vector3 start, Vector3 dest)
        {
            float hopCost = PluginConfig.HopCost.Value;
            bool allowPresumed = PluginConfig.RoutePresumed.Value;

            // Node 0 = start, 1 = dest, 2.. = portals (only ones with a usable link matter).
            var portals = new List<PortalEntry>();
            for (int i = 0; i < snap.Portals.Count; i++)
            {
                PortalEntry e = snap.Portals[i];
                if (e.Linked && (allowPresumed || e.Link.Kind == LinkKind.Confirmed))
                    portals.Add(e);
            }

            int n = 2 + portals.Count;
            var pos = new Vector3[n];
            pos[0] = start;
            pos[1] = dest;
            var index = new Dictionary<string, int>();
            for (int i = 0; i < portals.Count; i++)
            {
                pos[2 + i] = portals[i].Pos;
                index[portals[i].Key] = 2 + i;
            }

            var dist = new float[n];
            var prev = new int[n];
            var prevHop = new bool[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.PositiveInfinity;
                prev[i] = -1;
            }
            dist[0] = 0f;

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                if (u < 0 || u == 1)
                    break;
                done[u] = true;

                // Walk to anything.
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || v == u)
                        continue;
                    float d = dist[u] + Utils.DistanceXZ(pos[u], pos[v]);
                    if (d < dist[v])
                    {
                        dist[v] = d;
                        prev[v] = u;
                        prevHop[v] = false;
                    }
                }

                // Hop through a portal's link.
                if (u >= 2)
                {
                    PortalEntry e = portals[u - 2];
                    int v;
                    if (index.TryGetValue(e.Link.Other(e).Key, out v) && !done[v])
                    {
                        float d = dist[u] + hopCost;
                        if (d < dist[v])
                        {
                            dist[v] = d;
                            prev[v] = u;
                            prevHop[v] = true;
                        }
                    }
                }
            }

            var route = new Route { Direct = Utils.DistanceXZ(start, dest) };
            if (float.IsInfinity(dist[1]))
                return route;

            var chain = new List<int>();
            for (int v = 1; v != -1; v = prev[v])
                chain.Add(v);
            chain.Reverse();

            for (int i = 1; i < chain.Count; i++)
            {
                int a = chain[i - 1], b = chain[i];
                var leg = new RouteLeg { From = pos[a], To = pos[b] };
                if (prevHop[b])
                {
                    PortalEntry e = portals[a - 2];
                    leg.Kind = LegKind.Hop;
                    leg.Link = e.Link;
                    leg.Portal = e;
                    leg.Distance = 0f;
                    route.Hops++;
                    if (e.Link.Kind != LinkKind.Confirmed)
                        route.AnyPresumed = true;
                }
                else
                {
                    leg.Kind = LegKind.Walk;
                    leg.Distance = Utils.DistanceXZ(pos[a], pos[b]);
                    route.Walking += leg.Distance;
                }
                route.Legs.Add(leg);
            }
            return route;
        }
    }
}
