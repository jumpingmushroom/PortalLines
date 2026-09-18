using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PortalLines.Core;
using PortalLines.UI;
using UnityEngine;

namespace PortalLines
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public sealed class PortalLinesPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jumpingmushroom.portallines";
        public const string PluginName = "PortalLines";
        public const string PluginVersion = "0.3.0";

        /// <summary>Never toggle while the player is typing.</summary>
        private static bool InputBlocked()
        {
            if (Console.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            if (Minimap.InTextInput()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            return false;
        }

        /// <summary>Scan cadence while the large map is closed: cheap, and keeps pins current.</summary>
        private const float IdleScanInterval = 15f;

        internal static ManualLogSource Log;

        private readonly MapOverlay _overlay = new MapOverlay();
        private readonly PortalPins _pins = new PortalPins();
        private readonly MapToggle _toggle = new MapToggle();
        private readonly PortalHover _hover = new PortalHover();
        private Harmony _harmony;

        private float _nextScan;
        private float _nextRefresh;
        private float _nextSave;
        private bool _wasLarge;
        private Player _lastPlayer;
        private bool _hadPlayer;

        private void Awake()
        {
            Log = Logger;

            PluginConfig.Bind(base.Config);
            ConsoleCommands.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(PortalLinesPlugin).Assembly);

            PluginConfig.StyleChanged += OnStyleChanged;
            PluginConfig.PinsChanged += OnPinsChanged;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            PluginConfig.StyleChanged -= OnStyleChanged;
            PluginConfig.PinsChanged -= OnPinsChanged;
            _overlay.Destroy();
            _toggle.Destroy();
            _hover.Destroy();
            _pins.Clear();
            PortalCache.Unload();
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        private void OnStyleChanged()
        {
            _overlay.MarkStyleDirty();
            _toggle.Sync();
        }

        private void OnPinsChanged()
        {
            _pins.Invalidate();
        }

        /// <summary>Logged out or returned to the menu: forget the world.</summary>
        private void LocalPlayerGone()
        {
            _overlay.Destroy();
            _toggle.Destroy();
            _hover.Destroy();
            _pins.Clear();
            PortalRegistry.Clear();
            PortalCache.Unload(); // saves if dirty
            Diagnostics.Reset();
            _nextScan = 0f;
            _wasLarge = false;
        }

        /// <summary>A new local player: a fresh world, or a respawn into a new instance.</summary>
        private void LocalPlayerArrived()
        {
            PortalRegistry.Clear();
            Diagnostics.Reset();
            _nextScan = 0f;
            _wasLarge = false;
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;

            // Detect logout / world change by polling, rather than from Player.OnDestroy: that
            // method nulls m_localPlayer inside its own body, so a postfix comparing against it
            // never matches. Unity's == treats a destroyed object as null, so track presence as a
            // bool and identity with ReferenceEquals.
            bool hasPlayer = player != null;
            bool sameInstance = ReferenceEquals(player, _lastPlayer);

            if (!hasPlayer && _hadPlayer)
                LocalPlayerGone();
            else if (hasPlayer && (!_hadPlayer || !sameInstance))
                LocalPlayerArrived();

            _hadPlayer = hasPlayer;
            _lastPlayer = player;

            if (player == null)
                return;

            Minimap map = Minimap.instance;
            if (map == null)
                return;

            _overlay.EnsureAttached(map);
            if (!_toggle.Created)
                _toggle.Create(map);
            _toggle.SetVisible(PluginConfig.MapToggle.Value);

            if (PluginConfig.ToggleKey.Value.IsDown() && !InputBlocked())
                PluginConfig.LinesEnabled.Value = !PluginConfig.LinesEnabled.Value;

            bool large = map.m_mode == Minimap.MapMode.Large;
            bool opened = large && !_wasLarge;
            _wasLarge = large;
            if (opened)
                _nextScan = 0f;

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + (large ? PluginConfig.ScanInterval.Value : IdleScanInterval);
                PortalRegistry.Scan();
            }

            if (opened)
            {
                // Opening the map: ask for anything stale, and report the layout once, after the
                // scan so the counts in the report are real.
                _nextRefresh = Time.time + PluginConfig.RefreshInterval.Value;
                PortalRegistry.RequestRefresh();
                Diagnostics.ReportOnce(map);
            }

            if (large && Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + PluginConfig.RefreshInterval.Value;
                PortalRegistry.RequestRefresh();
            }

            _pins.Sync(PortalRegistry.Snapshot);

            if (large)
                _hover.Update(map, PortalRegistry.Snapshot);
            else
                _hover.Clear();

            if (Time.time >= _nextSave)
            {
                _nextSave = Time.time + 10f;
                PortalCache.SaveIfDirty();
            }
        }
    }
}
