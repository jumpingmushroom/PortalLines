using System.Collections.Generic;
using PortalLines.Core;
using PortalLines.Model;
using UnityEngine;

namespace PortalLines.UI
{
    /// <summary>
    /// One vanilla pin per known portal, carrying the game's own portal icon and the tag as name.
    ///
    /// Pins are added with save:false, so they are never written to the map file, never shared via
    /// the cartography table, and — because Minimap.GetClosestPin only considers saved pins —
    /// cannot be clicked, checked or right-click-deleted. Type is PinType.None so AddPin does not
    /// flip the player's icon filter; the vanilla portal-icon filter is honoured by hand instead.
    /// </summary>
    internal sealed class PortalPins
    {
        private static readonly Dictionary<Minimap.PinData, PortalEntry> s_owned = new Dictionary<Minimap.PinData, PortalEntry>();

        private readonly Dictionary<string, Minimap.PinData> _pins = new Dictionary<string, Minimap.PinData>();
        private readonly List<string> _gone = new List<string>();
        private Minimap _map;
        private Sprite _icon;
        private int _version = -1;
        private bool _shown;
        private bool _forceRebuild;

        public void Invalidate()
        {
            _forceRebuild = true;
        }

        public void Clear()
        {
            _pins.Clear();
            s_owned.Clear();
            _map = null;
            _icon = null;
            _version = -1;
            _shown = false;
        }

        public void Sync(PortalSnapshot snap)
        {
            Minimap map = Minimap.instance;
            if (map == null)
            {
                Clear();
                return;
            }

            if (!ReferenceEquals(map, _map))
            {
                Clear();
                _map = map;
                _icon = PortalIcon(map);
            }

            bool filterVisible = map.m_visibleIconTypes != null
                                 && (int)Minimap.PinType.Icon4 < map.m_visibleIconTypes.Length
                                 && map.m_visibleIconTypes[(int)Minimap.PinType.Icon4];
            bool show = PluginConfig.PinsEnabled.Value && filterVisible;

            if (!show)
            {
                if (_shown)
                    RemoveAll(map);
                _shown = false;
                return;
            }

            bool changed = !_shown || _forceRebuild || snap.Version != _version;
            _shown = true;
            if (!changed)
                return;

            if (_forceRebuild)
            {
                RemoveAll(map);
                _forceRebuild = false;
            }

            _version = snap.Version;
            bool showTags = PluginConfig.ShowTags.Value;

            var seen = new HashSet<string>();
            for (int i = 0; i < snap.Portals.Count; i++)
            {
                PortalEntry e = snap.Portals[i];
                seen.Add(e.Key);
                string name = showTags ? e.Tag : "";

                Minimap.PinData pin;
                if (_pins.TryGetValue(e.Key, out pin))
                {
                    if (pin.m_name != name)
                    {
                        // A name change needs a new PinNameData; simplest is a fresh pin.
                        map.RemovePin(pin);
                        s_owned.Remove(pin);
                        pin = null;
                    }
                    else
                    {
                        pin.m_pos = e.Pos;
                        s_owned[pin] = e;
                    }
                }

                if (pin == null)
                {
                    pin = CreatePin(map, e.Pos, name);
                    _pins[e.Key] = pin;
                    s_owned[pin] = e;
                }
            }

            _gone.Clear();
            foreach (KeyValuePair<string, Minimap.PinData> kv in _pins)
                if (!seen.Contains(kv.Key))
                    _gone.Add(kv.Key);

            for (int i = 0; i < _gone.Count; i++)
            {
                Minimap.PinData pin = _pins[_gone[i]];
                map.RemovePin(pin);
                s_owned.Remove(pin);
                _pins.Remove(_gone[i]);
            }

            map.m_pinUpdateRequired = true;
        }

        /// <summary>
        /// What Minimap.AddPin does, minus the icon-filter flip and the optional PlatformUserID
        /// author parameter (whose type lives in an assembly this project does not reference).
        /// </summary>
        private Minimap.PinData CreatePin(Minimap map, Vector3 pos, string name)
        {
            var pin = new Minimap.PinData
            {
                m_type = Minimap.PinType.None,
                m_name = name ?? "",
                m_pos = pos,
                m_icon = _icon,
                m_save = false,
                m_checked = false,
                m_ownerID = 0L,
            };
            if (pin.m_name.Length > 0)
                pin.m_NamePinData = new Minimap.PinNameData(pin);
            map.m_pins.Add(pin);
            map.m_pinUpdateRequired = true;
            return pin;
        }

        private void RemoveAll(Minimap map)
        {
            foreach (KeyValuePair<string, Minimap.PinData> kv in _pins)
            {
                map.RemovePin(kv.Value);
                s_owned.Remove(kv.Value);
            }
            _pins.Clear();
            _version = -1;
        }

        /// <summary>
        /// Called after Minimap.UpdatePins, which sets every icon's colour to white or the
        /// shared-map grey each pass. Re-apply our tints on top.
        /// </summary>
        public static void ApplyTints()
        {
            if (s_owned.Count == 0)
                return;

            bool tint = PluginConfig.TintUnlinked.Value;
            Color unlinked = PluginConfig.UnlinkedColor.Value;
            Color conflict = PluginConfig.ConflictColor.Value;
            float rememberedAlpha = PluginConfig.RememberedAlpha.Value;

            foreach (KeyValuePair<Minimap.PinData, PortalEntry> kv in s_owned)
            {
                Minimap.PinData pin = kv.Key;
                PortalEntry e = kv.Value;
                if (pin.m_iconElement == null)
                    continue;

                Color c = pin.m_iconElement.color;
                if (tint && e.Conflict && !e.Linked)
                    c = conflict;
                else if (tint && !e.Linked)
                    c = unlinked;

                // Remembered from an earlier session, not confirmed to still exist: fade it.
                if (e.Remembered)
                {
                    c.a *= rememberedAlpha;
                    if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                    {
                        Color t = pin.m_NamePinData.PinNameText.color;
                        t.a = c.a;
                        pin.m_NamePinData.PinNameText.color = t;
                    }
                }
                pin.m_iconElement.color = c;
            }
        }

        /// <summary>The vanilla portal pin icon (the fifth selectable icon).</summary>
        private static Sprite PortalIcon(Minimap map)
        {
            if (map.m_icons == null)
                return null;

            for (int i = 0; i < map.m_icons.Count; i++)
                if (map.m_icons[i].m_name == Minimap.PinType.Icon4)
                    return map.m_icons[i].m_icon;

            return null;
        }
    }
}
