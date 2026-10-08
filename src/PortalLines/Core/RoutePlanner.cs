using System.Collections.Generic;
using System.Diagnostics;
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
        public float Water; // metres of swim water on a walking leg
        /// <summary>From … To. A walk that follows land bends; a hop or a straight walk has two points.</summary>
        public List<Vector3> Path;
        public PortalLink Link; // for hops
        public PortalEntry Portal; // the portal entered, for hops
    }

    public sealed class Route
    {
        public readonly List<RouteLeg> Legs = new List<RouteLeg>();
        public float Walking;
        public float Water;
        public float Direct;
        public int Hops;
        public bool AnyPresumed;
        /// <summary>From the straight-line planner: no water map yet, on a ship, or WaterPenalty 1.</summary>
        public bool Straight;
        /// <summary>A land-following route for the current inputs is still being searched for.</summary>
        public bool Planning;
        public bool UsesPortals => Hops > 0;
    }

    /// <summary>
    /// The route from the player to a chosen destination over the portal network.
    ///
    /// Normally the route is read off a <see cref="LandField"/>: a search from the destination
    /// over a 24 m grid in which swim water costs WaterPenalty times its length and portal links
    /// are teleports, run on a worker thread when the destination, the network or a setting
    /// changes. Following it from the player's cell gives walking legs that bend round fjords and
    /// bays, and the portals worth taking given that. It is re-read every 5 m, which costs a walk
    /// along the path, not a search.
    ///
    /// The straight-line planner covers the rest: a small complete graph of the start, the
    /// destination and every known portal, straight walking between any two and a fixed cost per
    /// hop. It answers before the water map exists, if a search fails, and on a ship, where water
    /// is free. While the first field for a destination computes (about half a second) the route
    /// has no legs.
    /// </summary>
    public static class RouteState
    {
        /// <summary>A shortcut may cross this much more water than the grid path it replaces.</summary>
        private const float SmoothSlack = 12f;
        /// <summary>A bend closer than this is passed; the arrow looks to the one after.</summary>
        private const float BendReached = 10f;

        public static bool Active { get; private set; }
        public static Vector3 Destination { get; private set; }
        public static Route Current { get; private set; }
        public static int Version { get; private set; }
        public static double LastComputeMs { get; private set; }
        public static string FieldStatus { get; private set; } = "none";

        private static Vector3 _lastStart;
        private static int _lastSnapshot = -1;
        private static bool _wasAway;
        private static LandField _field;
        private static LandField _usedField;
        private static bool _lastShip;
        private static bool _lastPlanning;
        private static string _fieldStats = "none yet";

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

        /// <summary>
        /// The point to head for next: the next bend of the first walking leg still ahead of the
        /// player, else the destination.
        /// </summary>
        public static Vector3 NextWaypoint()
        {
            Route r = Current;
            if (r == null || r.Legs.Count == 0 || r.Legs[0].Kind != LegKind.Walk)
                return Destination;
            List<Vector3> path = r.Legs[0].Path;
            return path[NextIndex(path, PlayerPos(r))];
        }

        /// <summary>How far the player still has to walk to the next portal or the destination.</summary>
        public static float DistanceToLegEnd(Vector3 pos)
        {
            Route r = Current;
            if (r == null || r.Legs.Count == 0 || r.Legs[0].Kind != LegKind.Walk)
                return Utils.DistanceXZ(pos, NextWaypoint());
            List<Vector3> path = r.Legs[0].Path;
            int i = NextIndex(path, pos);
            float d = Utils.DistanceXZ(pos, path[i]);
            for (; i + 1 < path.Count; i++)
                d += Utils.DistanceXZ(path[i], path[i + 1]);
            return d;
        }

        /// <summary>A planner setting changed: recompute on the next update even if nothing moved.</summary>
        public static void Invalidate()
        {
            _lastSnapshot = -1;
        }

        public static void Clear()
        {
            LandFieldJob.Cancel();
            _field = null;
            _usedField = null;
            if (!Active)
                return;
            Active = false;
            Current = null;
            _lastSnapshot = -1;
            Version++;
        }

        /// <summary>Recompute when the start moved, the network changed or a better field arrived.</summary>
        public static void Update(PortalSnapshot snap, Vector3 start)
        {
            if (!Active)
                return;

            bool onShip = Ship.GetLocalShip() != null;
            WaterBits water = WaterMap.Bits;
            float penalty = PluginConfig.WaterPenalty.Value;
            bool land = water != null && !onShip && penalty > 1f;
            var key = new FieldKey
            {
                Dest = Destination,
                Snapshot = snap.Version,
                Water = WaterMap.Version,
                HopCost = PluginConfig.HopCost.Value,
                Penalty = penalty,
            };

            LandField arrived = LandFieldJob.TakeResult();
            if (arrived != null && arrived.Key.SameDest(key))
            {
                _field = arrived;
                _fieldStats = string.Format("{0:0} m grid, {1} cells searched in {2:0} ms",
                    arrived.CellSize, arrived.Settled, arrived.Ms);
                if (PluginConfig.Verbose.Value)
                    PortalLinesPlugin.Log.LogDebug("route field: " + _fieldStats);
            }
            if (land && (_field == null || !_field.Key.Same(key)) && !LandFieldJob.Pending(key))
                LandFieldJob.Start(key, BuildLinks(snap), water);

            // A field for the same destination stays in use while a fresher one is computed.
            LandField use = land && _field != null && _field.Key.SameDest(key) ? _field : null;
            bool planning = land && (use == null || !use.Key.Same(key));

            if (!land)
                FieldStatus = water == null ? "straight lines: water map not ready"
                    : onShip ? "straight lines: on a ship" : "straight lines: WaterPenalty is 1";
            else
                FieldStatus = (planning ? "computing; last " : "") + _fieldStats;

            if (_lastSnapshot == snap.Version && ReferenceEquals(use, _usedField) && onShip == _lastShip
                && planning == _lastPlanning && Utils.DistanceXZ(start, _lastStart) < 5f && Current != null)
                return;

            _lastSnapshot = snap.Version;
            _usedField = use;
            _lastShip = onShip;
            _lastPlanning = planning;
            _lastStart = start;
            var watch = Stopwatch.StartNew();
            Route route;
            if (use != null)
                route = Extract(use, start, Destination) ?? ComputeStraight(snap, start, Destination);
            else if (land && !LandFieldJob.Failed(key))
                // First search for this destination: a straight route now would often be the very
                // line across the water that the search is about to replace, so show none.
                route = new Route { Direct = Utils.DistanceXZ(start, Destination) };
            else
                route = ComputeStraight(snap, start, Destination);
            route.Planning = planning;
            Current = route;
            LastComputeMs = watch.Elapsed.TotalMilliseconds;
            Version++;
        }

        private static Vector3 PlayerPos(Route r)
        {
            Player p = Player.m_localPlayer;
            return p != null ? p.transform.position : r.Legs[0].From;
        }

        /// <summary>
        /// The index of the path point to head for: the end of the segment the player is nearest,
        /// or the one after when that end is already within reach.
        /// </summary>
        private static int NextIndex(List<Vector3> path, Vector3 pos)
        {
            var p = new Vector2(pos.x, pos.z);
            int best = 1;
            float bestD = float.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                float d = DistToSegment(p, new Vector2(path[i - 1].x, path[i - 1].z), new Vector2(path[i].x, path[i].z));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best + 1 < path.Count && Utils.DistanceXZ(pos, path[best]) < BendReached)
                best++;
            return best;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (a + ab * t - p).magnitude;
        }

        private static List<FieldLink> BuildLinks(PortalSnapshot snap)
        {
            bool allowPresumed = PluginConfig.RoutePresumed.Value;
            var links = new List<FieldLink>();
            for (int i = 0; i < snap.Portals.Count; i++)
            {
                PortalEntry e = snap.Portals[i];
                if (!e.Linked || (!allowPresumed && e.Link.Kind != LinkKind.Confirmed))
                    continue;
                links.Add(new FieldLink { Entrance = e.Pos, Exit = e.Link.Other(e).Pos, Portal = e, Link = e.Link });
            }
            return links;
        }

        /// <summary>Follow the field from the start; null when the start is off the grid or unreached.</summary>
        private static Route Extract(LandField f, Vector3 start, Vector3 dest)
        {
            int cell = f.CellOf(start);
            if (cell < 0 || float.IsInfinity(f.Cost[cell]))
                return null;

            var route = new Route { Direct = Utils.DistanceXZ(start, dest) };
            var cells = new List<int>();
            Vector3 legStart = start;
            int guard = f.Side * 8;
            for (int steps = 0; steps < guard; steps++)
            {
                byte next = f.Next[cell];
                if (next == LandField.End)
                {
                    AddWalk(route, f, legStart, cells, dest);
                    return route;
                }
                if (next == LandField.TakePortal)
                {
                    int li;
                    if (!f.PortalAt.TryGetValue(cell, out li))
                        return null;
                    FieldLink link = f.Links[li];
                    AddWalk(route, f, legStart, cells, link.Entrance);
                    AddHop(route, link);
                    legStart = link.Exit;
                    cells.Clear();
                    cell = f.CellOf(link.Exit);
                    if (cell < 0)
                        return null;
                    continue;
                }
                cell = f.Step(cell, next);
                cells.Add(cell);
            }
            return null;
        }

        /// <summary>
        /// A walking leg through the centres of the cells walked, the last replaced by the real
        /// end, then straightened wherever a direct line is no wetter.
        /// </summary>
        private static void AddWalk(Route route, LandField f, Vector3 from, List<int> cells, Vector3 to)
        {
            var pts = new List<Vector3>(cells.Count + 2) { from };
            for (int i = 0; i < cells.Count - 1; i++)
                pts.Add(f.Center(cells[i]));
            pts.Add(to);
            List<Vector3> path = Smooth(pts);

            float len = 0f, wet = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                len += Utils.DistanceXZ(path[i - 1], path[i]);
                wet += WaterMap.WaterAlong(path[i - 1], path[i]);
            }
            if (len < 1f)
                return;
            route.Legs.Add(new RouteLeg
            {
                Kind = LegKind.Walk, From = path[0], To = path[path.Count - 1],
                Distance = len, Water = wet, Path = path,
            });
            route.Walking += len;
            route.Water += wet;
        }

        private static void AddHop(Route route, FieldLink link)
        {
            route.Legs.Add(new RouteLeg
            {
                Kind = LegKind.Hop, From = link.Entrance, To = link.Exit,
                Link = link.Link, Portal = link.Portal,
                Path = new List<Vector3> { link.Entrance, link.Exit },
            });
            route.Hops++;
            if (link.Link.Kind != LinkKind.Confirmed)
                route.AnyPresumed = true;
        }

        /// <summary>
        /// Greedy string pulling: from each kept point, reach as far along the path as a straight
        /// line can without crossing more water than the path does there.
        /// </summary>
        private static List<Vector3> Smooth(List<Vector3> pts)
        {
            int last = pts.Count - 1;
            var wet = new float[pts.Count];
            for (int i = 1; i <= last; i++)
                wet[i] = wet[i - 1] + WaterMap.WaterAlong(pts[i - 1], pts[i]);

            var outp = new List<Vector3> { pts[0] };
            int a = 0;
            while (a < last)
            {
                int b = a + 1;
                while (b < last && WaterMap.WaterAlong(pts[a], pts[b + 1]) <= wet[b + 1] - wet[a] + SmoothSlack)
                    b++;
                outp.Add(pts[b]);
                a = b;
            }
            return outp;
        }

        /// <summary>Dijkstra over straight lines between the start, the destination and every usable portal.</summary>
        private static Route ComputeStraight(PortalSnapshot snap, Vector3 start, Vector3 dest)
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

            var route = new Route { Direct = Utils.DistanceXZ(start, dest), Straight = true };
            if (float.IsInfinity(dist[1]))
                return route;

            var chain = new List<int>();
            for (int v = 1; v != -1; v = prev[v])
                chain.Add(v);
            chain.Reverse();

            for (int i = 1; i < chain.Count; i++)
            {
                int a = chain[i - 1], b = chain[i];
                var leg = new RouteLeg { From = pos[a], To = pos[b], Path = new List<Vector3> { pos[a], pos[b] } };
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
                    leg.Water = WaterMap.WaterAlong(pos[a], pos[b]);
                    route.Walking += leg.Distance;
                    route.Water += leg.Water;
                }
                route.Legs.Add(leg);
            }
            return route;
        }
    }
}
