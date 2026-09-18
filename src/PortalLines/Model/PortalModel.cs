using System.Collections.Generic;
using UnityEngine;

namespace PortalLines.Model
{
    public enum LinkKind
    {
        /// <summary>Both ends are loaded and each names the other as its connection.</summary>
        Confirmed,

        /// <summary>
        /// The game has not shown us a mutual connection, but the evidence says these two are (or
        /// are about to be) paired: one end names the other while the other end's copy is stale,
        /// or they are the only two portals we know with this tag.
        /// </summary>
        Presumed
    }

    public sealed class PortalEntry
    {
        public ZDOID Id;
        public Vector3 Pos;
        public string Tag = "";
        public int PrefabHash;
        public string PrefabName = "";

        /// <summary>What this portal's ZDO says its partner is. None when unconnected.</summary>
        public ZDOID PartnerId;

        /// <summary>True when the partner's ZDO is present on this client.</summary>
        public bool PartnerLoaded;

        /// <summary>Inside the active area right now, so the ZDO is live rather than a stale copy.</summary>
        public bool InActiveArea;

        /// <summary>How many known portals share this tag, including this one.</summary>
        public int TagCount;

        public PortalLink Link;

        public bool Linked => Link != null;

        /// <summary>Three or more portals share the tag: only one pair can ever connect.</summary>
        public bool Conflict => TagCount > 2;

        public bool HasTag => !string.IsNullOrEmpty(Tag);
    }

    public sealed class PortalLink
    {
        public PortalEntry A;
        public PortalEntry B;
        public LinkKind Kind;
        public string Tag = "";
        public float Distance;

        public PortalEntry Other(PortalEntry e)
        {
            return ReferenceEquals(e, A) ? B : A;
        }
    }

    public sealed class PortalSnapshot
    {
        public readonly List<PortalEntry> Portals = new List<PortalEntry>();
        public readonly List<PortalLink> Links = new List<PortalLink>();

        /// <summary>Bumped whenever the set of portals or links changes. Consumers memoise on it.</summary>
        public int Version;

        /// <summary>Partners named by a known portal whose ZDO has not arrived yet.</summary>
        public int PendingPartners;

        /// <summary>True on the host or in singleplayer, where the list is the whole world.</summary>
        public bool Authoritative;

        public string Signature = "";
    }
}
