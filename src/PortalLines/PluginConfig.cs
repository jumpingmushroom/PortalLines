using System;
using BepInEx.Configuration;
using UnityEngine;

namespace PortalLines
{
    public enum LineColorMode
    {
        /// <summary>A stable colour derived from the tag, so crossing lines can be told apart.</summary>
        PerTag,

        /// <summary>Every line in the configured single colour.</summary>
        Single
    }

    public static class PluginConfig
    {
        public static ConfigEntry<float> ScanInterval;
        public static ConfigEntry<float> RefreshInterval;
        public static ConfigEntry<bool> FetchPartners;

        public static ConfigEntry<bool> LinesEnabled;
        public static ConfigEntry<float> LineWidth;
        public static ConfigEntry<float> LineAlpha;
        public static ConfigEntry<LineColorMode> ColorMode;
        public static ConfigEntry<Color> SingleColor;
        public static ConfigEntry<bool> ShowPresumed;
        public static ConfigEntry<bool> Outline;
        public static ConfigEntry<bool> ShowOnMinimap;

        public static ConfigEntry<bool> PinsEnabled;
        public static ConfigEntry<bool> ShowTags;
        public static ConfigEntry<bool> TintUnlinked;
        public static ConfigEntry<Color> UnlinkedColor;
        public static ConfigEntry<Color> ConflictColor;

        public static ConfigEntry<bool> Verbose;

        /// <summary>Raised when something that changes how lines are drawn is edited.</summary>
        public static event Action StyleChanged;

        /// <summary>Raised when something that changes which pins exist is edited.</summary>
        public static event Action PinsChanged;

        // ConfigurationManagerAttributes is supplied by Jotunn (global namespace). Config
        // managers match it by type name via reflection, so no dependency on any particular
        // ConfigurationManager build is implied and nothing breaks if none is installed.
        private static ConfigurationManagerAttributes Attr(int order, bool advanced = false)
        {
            return new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
        }

        public static void Bind(ConfigFile cfg)
        {
            LinesEnabled = cfg.Bind("Lines", "Enabled", true,
                new ConfigDescription("Draw a line between each pair of connected portals.", null, Attr(100)));

            LineWidth = cfg.Bind("Lines", "Width", 3f,
                new ConfigDescription("Line width in pixels on the large map. The minimap uses two thirds of this.",
                    new AcceptableValueRange<float>(1f, 12f), Attr(90)));

            LineAlpha = cfg.Bind("Lines", "Alpha", 0.85f,
                new ConfigDescription("Line opacity.", new AcceptableValueRange<float>(0.1f, 1f), Attr(85)));

            ColorMode = cfg.Bind("Lines", "ColorMode", LineColorMode.PerTag,
                new ConfigDescription(
                    "PerTag gives each tag its own stable colour so crossing lines can be told apart. " +
                    "Single uses one colour for everything.",
                    null, Attr(80)));

            SingleColor = cfg.Bind("Lines", "SingleColor", new Color(0.55f, 0.85f, 1f),
                new ConfigDescription("Line colour when ColorMode is Single.", null, Attr(75)));

            ShowPresumed = cfg.Bind("Lines", "ShowPresumed", true,
                new ConfigDescription(
                    "Also draw dashed lines for pairs the game has not confirmed yet: the only two " +
                    "known portals with a tag, or a pair where one end's copy is still stale.",
                    null, Attr(70)));

            Outline = cfg.Bind("Lines", "Outline", true,
                new ConfigDescription("Dark outline under each line so it stays readable over snow and sand.",
                    null, Attr(65)));

            ShowOnMinimap = cfg.Bind("Lines", "ShowOnMinimap", false,
                new ConfigDescription("Also draw lines on the small minimap in the corner.", null, Attr(60)));

            PinsEnabled = cfg.Bind("Pins", "Enabled", true,
                new ConfigDescription(
                    "Add a portal pin at every known portal. These pins are never saved and never " +
                    "shared through the cartography table. The map's own portal-icon filter hides them.",
                    null, Attr(50)));

            ShowTags = cfg.Bind("Pins", "ShowTags", true,
                new ConfigDescription("Label each pin with the portal's tag (visible when zoomed in, like any pin name).",
                    null, Attr(45)));

            TintUnlinked = cfg.Bind("Pins", "TintUnlinked", true,
                new ConfigDescription("Tint the pin of a portal that has no connection.", null, Attr(40)));

            UnlinkedColor = cfg.Bind("Pins", "UnlinkedColor", new Color(1f, 0.45f, 0.45f),
                new ConfigDescription("Pin tint for unconnected portals.", null, Attr(35)));

            ConflictColor = cfg.Bind("Pins", "ConflictColor", new Color(1f, 0.75f, 0.2f),
                new ConfigDescription(
                    "Pin tint when three or more known portals share a tag. Only one pair of them " +
                    "can ever be connected; the rest sit dark.",
                    null, Attr(30)));

            ScanInterval = cfg.Bind("Data", "ScanInterval", 2f,
                new ConfigDescription("Seconds between re-reading the portal list while the large map is open.",
                    new AcceptableValueRange<float>(0.5f, 10f), Attr(20, advanced: true)));

            RefreshInterval = cfg.Bind("Data", "RefreshInterval", 10f,
                new ConfigDescription(
                    "Seconds between asking the server to re-send the portals you know about but are " +
                    "not near, while the large map is open. This is how retagged or demolished portals " +
                    "far away catch up. The server sends nothing for portals that have not changed.",
                    new AcceptableValueRange<float>(3f, 60f), Attr(15, advanced: true)));

            FetchPartners = cfg.Bind("Data", "FetchPartners", true,
                new ConfigDescription(
                    "Ask the server for the far end of every known portal. This is the same request " +
                    "the game makes for the portal you are standing next to; without it, a line is " +
                    "only drawn once you have been near both ends.",
                    null, Attr(10, advanced: true)));

            Verbose = cfg.Bind("Logging", "Verbose", false,
                new ConfigDescription("Log scans and requests to the BepInEx log. Off by default; nothing is logged per tick.",
                    null, Attr(5, advanced: true)));

            LinesEnabled.SettingChanged += (s, e) => Raise(StyleChanged);
            LineWidth.SettingChanged += (s, e) => Raise(StyleChanged);
            LineAlpha.SettingChanged += (s, e) => Raise(StyleChanged);
            ColorMode.SettingChanged += (s, e) => Raise(StyleChanged);
            SingleColor.SettingChanged += (s, e) => Raise(StyleChanged);
            ShowPresumed.SettingChanged += (s, e) => Raise(StyleChanged);
            Outline.SettingChanged += (s, e) => Raise(StyleChanged);
            ShowOnMinimap.SettingChanged += (s, e) => Raise(StyleChanged);

            PinsEnabled.SettingChanged += (s, e) => Raise(PinsChanged);
            ShowTags.SettingChanged += (s, e) => Raise(PinsChanged);
            TintUnlinked.SettingChanged += (s, e) => Raise(PinsChanged);
            UnlinkedColor.SettingChanged += (s, e) => Raise(PinsChanged);
            ConflictColor.SettingChanged += (s, e) => Raise(PinsChanged);
        }

        private static void Raise(Action a)
        {
            if (a == null)
                return;

            try
            {
                a();
            }
            catch (Exception ex)
            {
                PortalLinesPlugin.Log.LogWarning("config listener threw: " + ex);
            }
        }
    }
}
