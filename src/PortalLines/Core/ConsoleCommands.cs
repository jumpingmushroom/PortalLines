using System.Collections.Generic;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.Core
{
    /// <summary>
    /// "portallines" console command. What the mod knows is otherwise only visible as lines, and
    /// "why is there no line here" needs the tag, the partner id and whether the partner's ZDO
    /// has arrived — all of which this prints.
    /// </summary>
    public static class ConsoleCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            new Terminal.ConsoleCommand("portallines",
                "PortalLines diagnostics: list | links | refresh | requests",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
                    switch (sub)
                    {
                        case "list": List(args.Context); break;
                        case "links": Links(args.Context); break;
                        case "refresh": Refresh(args.Context); break;
                        case "requests":
                            Say(args.Context, "PortalLines: " + PortalRegistry.RequestsSent + " ZDO request(s) sent this world.");
                            break;
                        default:
                            Say(args.Context, "portallines list     - every known portal: tag, position, partner, state");
                            Say(args.Context, "portallines links    - every drawn line and whether it is confirmed");
                            Say(args.Context, "portallines refresh  - rescan now and ask the server for stale copies");
                            Say(args.Context, "portallines requests - how many ZDO requests have been sent");
                            break;
                    }
                });
        }

        /// <summary>Console output also goes to the BepInEx log, so it can be read back from a file.</summary>
        private static void Say(Terminal ctx, string line)
        {
            ctx.AddString(line);
            PortalLinesPlugin.Log.LogInfo(line);
        }

        private static void Refresh(Terminal ctx)
        {
            PortalRegistry.Scan();
            PortalRegistry.RequestRefresh();
            PortalSnapshot s = PortalRegistry.Snapshot;
            Say(ctx, string.Format("PortalLines: {0} portal(s), {1} link(s), {2} partner(s) pending{3}",
                s.Portals.Count, s.Links.Count, s.PendingPartners,
                s.Authoritative ? " (host: this is the whole world)" : ""));
        }

        private static void List(Terminal ctx)
        {
            PortalRegistry.Scan();
            PortalSnapshot s = PortalRegistry.Snapshot;
            Say(ctx, string.Format("PortalLines: {0} known portal(s){1}", s.Portals.Count,
                s.Authoritative ? " (host: whole world)" : " (client: seen this session)"));

            for (int i = 0; i < s.Portals.Count; i++)
            {
                PortalEntry e = s.Portals[i];
                string state;
                if (e.Linked)
                    state = e.Link.Kind == LinkKind.Confirmed ? "linked" : "presumed";
                else if (e.PartnerId.IsNone())
                    state = e.Conflict ? "unlinked (tag conflict)" : "unlinked";
                else
                    state = e.PartnerLoaded ? "stale link" : "partner not loaded";

                Say(ctx, string.Format("  \"{0}\"  ({1:0},{2:0})  {3}  {4}{5}{6}",
                    e.Tag, e.Pos.x, e.Pos.z, e.PrefabName, state,
                    e.InActiveArea ? "  [near]" : "",
                    e.Conflict ? "  x" + e.TagCount : ""));
            }
        }

        private static void Links(Terminal ctx)
        {
            PortalSnapshot s = PortalRegistry.Snapshot;
            Say(ctx, "PortalLines: " + s.Links.Count + " link(s)");
            for (int i = 0; i < s.Links.Count; i++)
            {
                PortalLink l = s.Links[i];
                Say(ctx, string.Format("  \"{0}\"  ({1:0},{2:0}) <-> ({3:0},{4:0})  {5:0} m  {6}",
                    l.Tag, l.A.Pos.x, l.A.Pos.z, l.B.Pos.x, l.B.Pos.z, l.Distance,
                    l.Kind == LinkKind.Confirmed ? "confirmed" : "presumed"));
            }
        }
    }
}
