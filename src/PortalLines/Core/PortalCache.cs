using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.Core
{
    /// <summary>
    /// Per-world memory of portals on disk, so the map is populated from the moment you log in
    /// rather than only after you have walked near each portal again.
    ///
    /// Keyed by position, never by ZDOID: ids are session-scoped and the save file itself stores
    /// portal links as a hash that is re-resolved at load. One tab-separated line per portal, no
    /// serializer dependency. Lives in BepInEx/config/PortalLines/, so it follows the profile.
    /// </summary>
    public static class PortalCache
    {
        public sealed class Entry
        {
            public string Key = "";
            public Vector3 Pos;
            public string Tag = "";
            public string Prefab = "";
            public long LastSeen;
            public bool HasPartnerPos;
            public Vector3 PartnerPos;

            /// <summary>Session-only: when a loaded area first failed to contain this portal.</summary>
            public float MissingSince;
        }

        private const string Header = "# PortalLines cache v1: x\ty\tz\ttag\tprefab\tlastSeen\tpartnerX\tpartnerZ";

        private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private static string _path;
        private static long _loadedWorld;
        private static bool _dirty;

        public static bool Loaded => _path != null;
        public static int Count => _entries.Count;
        public static IEnumerable<Entry> Entries => _entries.Values;

        /// <summary>Load the cache for the current world once ZNet knows which world that is.</summary>
        public static void EnsureLoaded()
        {
            if (!PluginConfig.RememberPortals.Value)
                return;

            World world = ZNet.World;
            if (world == null)
                return;

            long id = world.m_uid != 0 ? world.m_uid : world.m_seed;
            if (_path != null && _loadedWorld == id)
                return;

            Unload();
            _loadedWorld = id;
            _path = PathFor(world, id);
            Load();
        }

        public static void Unload()
        {
            if (_dirty)
                Save();
            _entries.Clear();
            _path = null;
            _loadedWorld = 0;
            _dirty = false;
        }

        public static void Forget()
        {
            _entries.Clear();
            _dirty = true;
            Save();
        }

        public static Entry Get(string key)
        {
            Entry e;
            return _entries.TryGetValue(key, out e) ? e : null;
        }

        /// <summary>Record what a live ZDO says. Returns true if anything changed.</summary>
        public static void Observe(PortalEntry live, long now)
        {
            if (_path == null)
                return;

            Entry e;
            if (!_entries.TryGetValue(live.Key, out e))
            {
                e = new Entry { Key = live.Key };
                _entries[live.Key] = e;
                _dirty = true;
            }

            if (e.Pos != live.Pos || e.Tag != live.Tag || e.Prefab != live.PrefabName)
                _dirty = true;
            e.Pos = live.Pos;
            e.Tag = live.Tag;
            e.Prefab = live.PrefabName;
            e.MissingSince = 0f;

            // Only write the timestamp when it moves by a minute, so an idle session does not
            // mark the file dirty every scan.
            if (now - e.LastSeen >= 60)
            {
                e.LastSeen = now;
                _dirty = true;
            }

            if (live.Linked && live.Link.Kind == LinkKind.Confirmed)
            {
                Vector3 p = live.Link.Other(live).Pos;
                if (!e.HasPartnerPos || e.PartnerPos != p)
                    _dirty = true;
                e.HasPartnerPos = true;
                e.PartnerPos = p;
            }
            else if (live.PartnerId.IsNone() && live.InActiveArea && e.HasPartnerPos)
            {
                // A live, in-range portal that the server says is unconnected.
                e.HasPartnerPos = false;
                _dirty = true;
            }
        }

        /// <summary>
        /// A remembered portal whose area is loaded and in range but has no live ZDO has been
        /// demolished. Give the ZDO stream a grace period first: the zone spawns locally before
        /// every ZDO for it has arrived from the server.
        /// </summary>
        public static bool NoteMissing(Entry e, float now, float grace)
        {
            if (e.MissingSince == 0f)
            {
                e.MissingSince = now;
                return false;
            }
            if (now - e.MissingSince < grace)
                return false;

            _entries.Remove(e.Key);
            _dirty = true;
            if (PluginConfig.Verbose.Value)
                PortalLinesPlugin.Log.LogDebug("forgot demolished portal \"" + e.Tag + "\" at " + e.Key);
            return true;
        }

        public static void NotePresent(Entry e)
        {
            e.MissingSince = 0f;
        }

        public static void SaveIfDirty()
        {
            if (_dirty)
                Save();
        }

        private static string PathFor(World world, long id)
        {
            var sb = new StringBuilder();
            foreach (char c in world.m_name ?? "world")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            if (sb.Length == 0)
                sb.Append("world");
            string dir = Path.Combine(BepInEx.Paths.ConfigPath, "PortalLines");
            return Path.Combine(dir, sb + "-" + id.ToString(CultureInfo.InvariantCulture) + ".tsv");
        }

        private static void Load()
        {
            _entries.Clear();
            _dirty = false;
            if (!File.Exists(_path))
            {
                PortalLinesPlugin.Log.LogInfo("no portal cache yet at " + _path);
                return;
            }

            long cutoff = 0;
            int days = PluginConfig.ForgetAfterDays.Value;
            if (days > 0)
                cutoff = Now() - days * 86400L;

            int dropped = 0;
            try
            {
                foreach (string raw in File.ReadAllLines(_path))
                {
                    if (raw.Length == 0 || raw[0] == '#')
                        continue;
                    string[] f = raw.Split('\t');
                    if (f.Length < 6)
                        continue;

                    var e = new Entry
                    {
                        Pos = new Vector3(F(f[0]), F(f[1]), F(f[2])),
                        Tag = Unescape(f[3]),
                        Prefab = f[4],
                        LastSeen = long.Parse(f[5], CultureInfo.InvariantCulture),
                    };
                    if (f.Length >= 8 && f[6].Length > 0)
                    {
                        e.HasPartnerPos = true;
                        e.PartnerPos = new Vector3(F(f[6]), 0f, F(f[7]));
                    }
                    e.Key = PortalEntry.MakeKey(e.Pos);

                    if (cutoff > 0 && e.LastSeen < cutoff)
                    {
                        dropped++;
                        _dirty = true;
                        continue;
                    }
                    _entries[e.Key] = e;
                }
            }
            catch (Exception ex)
            {
                PortalLinesPlugin.Log.LogWarning("could not read portal cache " + _path + ": " + ex.Message);
                _entries.Clear();
                return;
            }

            PortalLinesPlugin.Log.LogInfo(string.Format("loaded {0} remembered portal(s) from {1}{2}",
                _entries.Count, Path.GetFileName(_path), dropped > 0 ? ", forgot " + dropped + " not seen for " + days + " days" : ""));
        }

        private static void Save()
        {
            if (_path == null)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                foreach (Entry e in _entries.Values)
                {
                    sb.Append(S(e.Pos.x)).Append('\t').Append(S(e.Pos.y)).Append('\t').Append(S(e.Pos.z)).Append('\t')
                      .Append(Escape(e.Tag)).Append('\t').Append(e.Prefab).Append('\t')
                      .Append(e.LastSeen.ToString(CultureInfo.InvariantCulture)).Append('\t');
                    if (e.HasPartnerPos)
                        sb.Append(S(e.PartnerPos.x)).Append('\t').Append(S(e.PartnerPos.z));
                    else
                        sb.Append('\t');
                    sb.Append('\n');
                }

                // Write beside, then rename: a crash mid-write must not lose the whole cache.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(tmp, _path);
                _dirty = false;

                if (PluginConfig.Verbose.Value)
                    PortalLinesPlugin.Log.LogDebug("saved " + _entries.Count + " portal(s) to " + Path.GetFileName(_path));
            }
            catch (Exception ex)
            {
                PortalLinesPlugin.Log.LogWarning("could not write portal cache " + _path + ": " + ex.Message);
            }
        }

        public static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string S(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static float F(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char n = s[++i];
                switch (n)
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
