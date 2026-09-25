using System;
using BepInEx.Configuration;
using UnityEngine;

namespace PortalLines
{
    public enum LineColorMode
    {
        /// <summary>Each end coloured by the biome it sits in, blended along the line.</summary>
        Biome,

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
        public static ConfigEntry<bool> RememberPortals;
        public static ConfigEntry<int> ForgetAfterDays;
        public static ConfigEntry<float> RememberedAlpha;

        public static ConfigEntry<bool> LinesEnabled;
        public static ConfigEntry<float> LineWidth;
        public static ConfigEntry<float> LineAlpha;
        public static ConfigEntry<LineColorMode> ColorMode;
        public static ConfigEntry<Color> SingleColor;
        public static ConfigEntry<bool> ShowPresumed;
        public static ConfigEntry<Color> MeadowsColor;
        public static ConfigEntry<Color> BlackForestColor;
        public static ConfigEntry<Color> SwampColor;
        public static ConfigEntry<Color> MountainColor;
        public static ConfigEntry<Color> PlainsColor;
        public static ConfigEntry<Color> MistlandsColor;
        public static ConfigEntry<Color> AshlandsColor;
        public static ConfigEntry<Color> DeepNorthColor;
        public static ConfigEntry<Color> OceanColor;
        public static ConfigEntry<bool> Outline;
        public static ConfigEntry<bool> ShowOnMinimap;
        public static ConfigEntry<bool> MapToggle;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<bool> HoverEnabled;
        public static ConfigEntry<float> HoverRadius;
        public static ConfigEntry<float> FocusDim;
        public static ConfigEntry<bool> RouteEnabled;
        public static ConfigEntry<float> HopCost;
        public static ConfigEntry<bool> RoutePresumed;
        public static ConfigEntry<Color> RouteColor;
        public static ConfigEntry<bool> RouteOnMinimap;
        public static ConfigEntry<float> ArriveDistance;
        public static ConfigEntry<KeyboardShortcut> ClearRouteKey;
        public static ConfigEntry<bool> ShowHudArrow;
        public static ConfigEntry<float> HudArrowOffsetY;
        public static ConfigEntry<float> HudArrowScale;

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

            ColorMode = cfg.Bind("Lines", "ColorMode", LineColorMode.Biome,
                new ConfigDescription(
                    "Biome colours each end of a line by the biome it sits in and blends between " +
                    "them, so a line tells you where it goes. PerTag gives each tag its own stable " +
                    "colour so crossing lines can be told apart. Single uses one colour for everything.",
                    null, Attr(80)));

            SingleColor = cfg.Bind("Lines", "SingleColor", new Color(0.55f, 0.85f, 1f),
                new ConfigDescription("Line colour when ColorMode is Single.", null, Attr(75)));

            // Biome palette. Hues are spread around the wheel on purpose: the map paints Meadows
            // and Black Forest both green, which blended along a line over green terrain says
            // nothing, so Black Forest is a teal-pine here. Mountain and Deep North are both snow;
            // they differ by saturation rather than hue.
            MeadowsColor = BiomeEntry(cfg, "Meadows", new Color(0.62f, 1f, 0.3f), 79);
            BlackForestColor = BiomeEntry(cfg, "BlackForest", new Color(0.1f, 0.68f, 0.55f), 78);
            SwampColor = BiomeEntry(cfg, "Swamp", new Color(0.72f, 0.5f, 0.38f), 77);
            MountainColor = BiomeEntry(cfg, "Mountain", new Color(0.5f, 0.78f, 1f), 76);
            PlainsColor = BiomeEntry(cfg, "Plains", new Color(1f, 0.9f, 0.2f), 75);
            MistlandsColor = BiomeEntry(cfg, "Mistlands", new Color(0.75f, 0.5f, 1f), 74);
            AshlandsColor = BiomeEntry(cfg, "Ashlands", new Color(1f, 0.28f, 0.18f), 73);
            DeepNorthColor = BiomeEntry(cfg, "DeepNorth", new Color(0.92f, 0.96f, 1f), 72);
            OceanColor = BiomeEntry(cfg, "Ocean", new Color(0.25f, 0.5f, 1f), 71);

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

            MapToggle = cfg.Bind("Lines", "MapToggle", true,
                new ConfigDescription("Show a \"Portal lines\" checkbox on the large map, above the map's own toggles.",
                    null, Attr(58)));

            ToggleKey = cfg.Bind("Lines", "ToggleKey", KeyboardShortcut.Empty,
                new ConfigDescription("Hotkey that shows and hides the lines. Unbound by default.", null, Attr(57)));

            HoverEnabled = cfg.Bind("Hover", "Enabled", true,
                new ConfigDescription(
                    "Hovering a portal on the large map highlights its line, dims the others, and " +
                    "shows its tag, destination biome, distance and link state. With lines switched " +
                    "off, hovering still shows that one portal's line.",
                    null, Attr(56)));

            HoverRadius = cfg.Bind("Hover", "Radius", 28f,
                new ConfigDescription("How close the cursor must be to a portal, in screen pixels.",
                    new AcceptableValueRange<float>(8f, 80f), Attr(55)));

            FocusDim = cfg.Bind("Hover", "DimOthers", 0.12f,
                new ConfigDescription("Opacity multiplier for every other line while a portal is hovered.",
                    new AcceptableValueRange<float>(0f, 1f), Attr(54)));

            RouteEnabled = cfg.Bind("Route", "Enabled", true,
                new ConfigDescription(
                    "Shift-click a spot on the large map to plan the fastest way there through the " +
                    "portal network: walk to a portal, hop, walk on. Shift-right-click clears it.",
                    null, Attr(53)));

            HopCost = cfg.Bind("Route", "HopCost", 60f,
                new ConfigDescription(
                    "How many metres of walking one portal hop is worth, so the planner does not " +
                    "chain hops that save almost nothing.",
                    new AcceptableValueRange<float>(0f, 500f), Attr(52)));

            RoutePresumed = cfg.Bind("Route", "UsePresumedLinks", true,
                new ConfigDescription("Let the planner use links that are presumed but not yet confirmed by the server.",
                    null, Attr(51)));

            RouteColor = cfg.Bind("Route", "Color", new Color(1f, 1f, 1f),
                new ConfigDescription("Colour of the walking legs and markers.", null, Attr(50)));

            RouteOnMinimap = cfg.Bind("Route", "ShowOnMinimap", true,
                new ConfigDescription(
                    "Follow the route on the small minimap: the walking leg from you to the next " +
                    "portal or the destination, with an arrow at the edge when it is out of view.",
                    null, Attr(49)));

            ArriveDistance = cfg.Bind("Route", "ArriveDistance", 20f,
                new ConfigDescription(
                    "Clear the route by itself once you are this close to the destination (metres). " +
                    "0 keeps it until you clear it.",
                    new AcceptableValueRange<float>(0f, 100f), Attr(48)));

            ClearRouteKey = cfg.Bind("Route", "ClearKey", KeyboardShortcut.Empty,
                new ConfigDescription("Hotkey that clears the route without opening the map. Unbound by default.",
                    null, Attr(47)));

            ShowHudArrow = cfg.Bind("Route", "ShowHudArrow", true,
                new ConfigDescription(
                    "An arrow at the top of the screen pointing the way to the next portal or the " +
                    "destination, relative to where you are looking, with the distance underneath. " +
                    "Beside the portal to take it says which one to enter.",
                    null, Attr(46)));

            HudArrowOffsetY = cfg.Bind("Route", "HudArrowOffsetY", 0f,
                new ConfigDescription(
                    "Move the HUD arrow down (positive) or up (negative) from its place at the top " +
                    "centre, e.g. to clear a compass mod. It already drops below raid and boss bars.",
                    new AcceptableValueRange<float>(-100f, 600f), Attr(45)));

            HudArrowScale = cfg.Bind("Route", "HudArrowScale", 1f,
                new ConfigDescription("Size of the HUD arrow and its label.",
                    new AcceptableValueRange<float>(0.5f, 2.5f), Attr(44)));

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

            RememberPortals = cfg.Bind("Data", "RememberPortals", true,
                new ConfigDescription(
                    "Remember portals on disk per world, so the map is populated from the moment you " +
                    "log in. The game only tells a client about portals near it, so without this the " +
                    "map starts empty every session. Stored in BepInEx/config/PortalLines/.",
                    null, Attr(28)));

            ForgetAfterDays = cfg.Bind("Data", "ForgetAfterDays", 0,
                new ConfigDescription(
                    "Drop remembered portals not seen for this many days. 0 keeps them until you " +
                    "visit the spot and find them gone.",
                    new AcceptableValueRange<int>(0, 365), Attr(26)));

            RememberedAlpha = cfg.Bind("Data", "RememberedAlpha", 0.55f,
                new ConfigDescription(
                    "Opacity of pins and lines for portals known only from memory, not yet confirmed " +
                    "this session.",
                    new AcceptableValueRange<float>(0.1f, 1f), Attr(24)));

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
            RouteOnMinimap.SettingChanged += (s, e) => Raise(StyleChanged);

            RememberedAlpha.SettingChanged += (s, e) => Raise(StyleChanged);
            FocusDim.SettingChanged += (s, e) => Raise(StyleChanged);
            MapToggle.SettingChanged += (s, e) => Raise(StyleChanged);

            PinsEnabled.SettingChanged += (s, e) => Raise(PinsChanged);
            ShowTags.SettingChanged += (s, e) => Raise(PinsChanged);
            TintUnlinked.SettingChanged += (s, e) => Raise(PinsChanged);
            UnlinkedColor.SettingChanged += (s, e) => Raise(PinsChanged);
            ConflictColor.SettingChanged += (s, e) => Raise(PinsChanged);
        }

        private static ConfigEntry<Color> BiomeEntry(ConfigFile cfg, string biome, Color def, int order)
        {
            ConfigEntry<Color> e = cfg.Bind("Biome colours", biome, def,
                new ConfigDescription("Line colour for a portal standing in " + biome + " (ColorMode Biome).", null, Attr(order)));
            e.SettingChanged += (s, ev) => Raise(StyleChanged);
            return e;
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
