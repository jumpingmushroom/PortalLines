using System.Collections.Generic;
using System.Text;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.Core
{
    /// <summary>
    /// Everything this client knows about portals, rebuilt from ZDOMan on each scan.
    ///
    /// ZDOMan.GetPortalList() is the whole world on the host and, on a client, every portal ZDO
    /// the server has ever sent this session — clients never discard persistent ZDOs, they only
    /// stop updating them once out of range. Links come from each ZDO's Portal connection, which
    /// only the server writes. For a partner we have not received, ZDOMan.RequestZDO asks the
    /// server to send it regardless of distance; TeleportWorld makes the same request for the
    /// portal you stand next to, so this is traffic the game already generates.
    /// </summary>
    public static class PortalRegistry
    {
        /// <summary>Seconds before the same ZDO is requested again.</summary>
        private const float RequestCooldown = 8f;

        private static readonly Dictionary<ZDOID, float> _requested = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<string, List<PortalEntry>> _byTag = new Dictionary<string, List<PortalEntry>>();
        private static readonly StringBuilder _sig = new StringBuilder(1024);

        private static int _version;

        public static PortalSnapshot Snapshot { get; private set; } = new PortalSnapshot();

        public static int RequestsSent { get; private set; }

        public static void Clear()
        {
            _requested.Clear();
            _byTag.Clear();
            Snapshot = new PortalSnapshot { Version = ++_version };
            RequestsSent = 0;
        }

        /// <summary>Rebuild the snapshot from ZDOMan. Cheap: a few hundred dictionary lookups at most.</summary>
        public static void Scan()
        {
            ZDOMan man = ZDOMan.instance;
            ZNet net = ZNet.instance;
            Game game = Game.instance;
            if (man == null || net == null || game == null)
            {
                if (Snapshot.Portals.Count > 0)
                    Clear();
                return;
            }

            var next = new PortalSnapshot { Authoritative = net.IsServer() };
            var byId = new Dictionary<ZDOID, PortalEntry>();
            Vector3 refPos = net.GetReferencePosition();
            ZNetScene scene = ZNetScene.instance;

            List<ZDO> zdos = man.GetPortalList();
            for (int i = 0; i < zdos.Count; i++)
            {
                ZDO zdo = zdos[i];
                if (zdo == null || !zdo.IsValid() || byId.ContainsKey(zdo.m_uid))
                    continue;

                var e = new PortalEntry
                {
                    Id = zdo.m_uid,
                    Pos = zdo.GetPosition(),
                    Tag = zdo.GetString(ZDOVars.s_tag) ?? "",
                    PrefabHash = zdo.GetPrefab(),
                    PartnerId = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal),
                };
                e.InActiveArea = ZNetScene.InActiveArea(e.Pos, refPos);

                if (scene != null)
                {
                    GameObject prefab = scene.GetPrefab(e.PrefabHash);
                    e.PrefabName = prefab != null ? prefab.name : e.PrefabHash.ToString();
                }

                byId[e.Id] = e;
                next.Portals.Add(e);
            }

            // Deterministic order so the signature is stable and the console listing reads well.
            next.Portals.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Tag, b.Tag);
                if (c != 0) return c;
                c = a.Pos.x.CompareTo(b.Pos.x);
                return c != 0 ? c : a.Pos.z.CompareTo(b.Pos.z);
            });

            _byTag.Clear();
            for (int i = 0; i < next.Portals.Count; i++)
            {
                PortalEntry e = next.Portals[i];
                List<PortalEntry> list;
                if (!_byTag.TryGetValue(e.Tag, out list))
                    _byTag[e.Tag] = list = new List<PortalEntry>(2);
                list.Add(e);
            }
            foreach (KeyValuePair<string, List<PortalEntry>> kv in _byTag)
                for (int i = 0; i < kv.Value.Count; i++)
                    kv.Value[i].TagCount = kv.Value.Count;

            // Pass 1: links the game has written. Mutual means confirmed. One-sided means one copy
            // is stale (a retag or a re-pair we have not been sent yet); draw it as presumed and let
            // the refresh sort it out, unless the other end already has a confirmed partner.
            for (int i = 0; i < next.Portals.Count; i++)
            {
                PortalEntry e = next.Portals[i];
                if (e.Linked || e.PartnerId.IsNone())
                    continue;

                PortalEntry p;
                if (!byId.TryGetValue(e.PartnerId, out p))
                {
                    e.PartnerLoaded = false;
                    next.PendingPartners++;
                    Request(e.PartnerId);
                    continue;
                }

                e.PartnerLoaded = true;
                bool mutual = p.PartnerId == e.Id;
                if (!mutual && p.Linked)
                    continue;
                if (!mutual && !e.InActiveArea)
                    Request(e.Id); // our copy is the stale one; ask for it

                AddLink(next, e, p, mutual ? LinkKind.Confirmed : LinkKind.Presumed);
            }

            // Pass 2: exactly two known portals with a tag and no link between them yet. The
            // server pairs same-tag portals within five seconds, so this is what it will do.
            foreach (KeyValuePair<string, List<PortalEntry>> kv in _byTag)
            {
                List<PortalEntry> list = kv.Value;
                if (list.Count != 2 || list[0].Linked || list[1].Linked)
                    continue;
                AddLink(next, list[0], list[1], LinkKind.Presumed);
            }

            next.Signature = BuildSignature(next);
            if (next.Signature != Snapshot.Signature)
            {
                next.Version = ++_version;
                Snapshot = next;

                if (PluginConfig.Verbose.Value)
                {
                    PortalLinesPlugin.Log.LogDebug(string.Format(
                        "scan: {0} portals, {1} links, {2} pending partner(s), authoritative={3}",
                        next.Portals.Count, next.Links.Count, next.PendingPartners, next.Authoritative));
                }
            }
            else
            {
                // Same shape, but positions/flags may be fresher; keep the newer objects under the
                // old version so consumers do not rebuild for nothing.
                next.Version = Snapshot.Version;
                Snapshot = next;
            }
        }

        /// <summary>
        /// Ask the server to re-send every known portal that is out of range, plus any partner
        /// still missing. Nothing is sent for ZDOs whose data revision has not changed.
        /// </summary>
        public static void RequestRefresh()
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer() || ZDOMan.instance == null)
                return;

            List<PortalEntry> portals = Snapshot.Portals;
            for (int i = 0; i < portals.Count; i++)
            {
                PortalEntry e = portals[i];
                if (!e.InActiveArea)
                    Request(e.Id);
                if (!e.PartnerId.IsNone() && !e.PartnerLoaded)
                    Request(e.PartnerId);
            }
        }

        private static void AddLink(PortalSnapshot snap, PortalEntry a, PortalEntry b, LinkKind kind)
        {
            var link = new PortalLink
            {
                A = a,
                B = b,
                Kind = kind,
                Tag = a.Tag,
                Distance = Utils.DistanceXZ(a.Pos, b.Pos),
            };
            a.Link = link;
            b.Link = link;
            snap.Links.Add(link);
        }

        private static void Request(ZDOID id)
        {
            if (!PluginConfig.FetchPartners.Value || id.IsNone())
                return;

            ZNet net = ZNet.instance;
            if (net == null || net.IsServer())
                return;

            float last;
            if (_requested.TryGetValue(id, out last) && Time.time - last < RequestCooldown)
                return;

            _requested[id] = Time.time;
            RequestsSent++;
            ZDOMan.instance.RequestZDO(id);

            if (PluginConfig.Verbose.Value)
                PortalLinesPlugin.Log.LogDebug("requested ZDO " + id);
        }

        private static string BuildSignature(PortalSnapshot snap)
        {
            _sig.Length = 0;
            for (int i = 0; i < snap.Portals.Count; i++)
            {
                PortalEntry e = snap.Portals[i];
                _sig.Append(e.Id.ID).Append(':').Append(e.Tag).Append(':')
                    .Append(e.Linked ? (e.Link.Kind == LinkKind.Confirmed ? 'C' : 'P') : 'U')
                    .Append(e.Conflict ? '!' : '.')
                    .Append(e.Linked ? e.Link.Other(e).Id.ID : 0u)
                    .Append(';');
            }
            return _sig.ToString();
        }
    }
}
