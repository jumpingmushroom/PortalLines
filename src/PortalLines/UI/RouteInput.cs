using System.Text;
using PortalLines.Core;
using UnityEngine;

namespace PortalLines.UI
{
    /// <summary>
    /// Shift-click sets the route destination, shift-right-click clears it. Also owns the
    /// summary panel drawn beside the destination. The route itself is kept current whenever
    /// the player exists, large map or not, so the minimap can follow it.
    /// </summary>
    internal sealed class RouteInput
    {
        private readonly MapPanel _panel = new MapPanel();
        private Minimap _map;
        private int _lastVersion = -1;
        private string _lastText = "";

        public static bool ShiftHeld()
        {
            return ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);
        }

        public void Update(Minimap map, bool large)
        {
            if (map == null)
            {
                Destroy();
                return;
            }
            if (!ReferenceEquals(map, _map))
            {
                _panel.Destroy();
                _map = map;
            }

            if (!PluginConfig.RouteEnabled.Value)
            {
                _panel.Hide();
                return;
            }

            Player player = Player.m_localPlayer;
            if (RouteState.Active && player != null)
            {
                if (PluginConfig.ClearRouteKey.Value.IsDown() && !PortalLinesPlugin.InputBlocked())
                    RouteState.Clear();
                else if (RouteState.CheckArrival(player.transform.position, PluginConfig.ArriveDistance.Value))
                    player.Message(MessageHud.MessageType.TopLeft, "Route: arrived");
                else
                    RouteState.Update(PortalRegistry.Snapshot, player.transform.position);
            }

            if (!large)
            {
                _panel.Hide();
                return;
            }

            if (ShiftHeld() && !Minimap.InTextInput())
            {
                if (ZInput.GetMouseButtonDown(0))
                {
                    Vector3 world = map.ScreenToWorldPoint(ZInput.pointerPosition);
                    if (world != Vector3.zero)
                        RouteState.Set(world);
                }
                else if (ZInput.GetMouseButtonDown(1))
                {
                    RouteState.Clear();
                }
            }

            if (!RouteState.Active)
            {
                _panel.Hide();
                return;
            }

            if (!_panel.Created && !_panel.Create(map, "PortalLinesRoute"))
                return;

            if (RouteState.Version != _lastVersion)
            {
                _lastVersion = RouteState.Version;
                _lastText = Describe(RouteState.Current);
            }

            // Beside the destination ring, in the map root's local space.
            Rect uv = map.m_mapImageLarge.uvRect;
            Rect rect = map.m_mapImageLarge.rectTransform.rect;
            Vector2 local = MapMath.WorldToLocal(map, RouteState.Destination, uv, rect) + rect.min;
            _panel.ShowAtRootLocal(map, _lastText, local, 16f);
        }

        public void Destroy()
        {
            _panel.Destroy();
            _map = null;
            _lastVersion = -1;
        }

        private static string Describe(Route route)
        {
            var sb = new StringBuilder(160);
            sb.Append("<b>Route</b>  <alpha=#99>shift-right-click clears\n");
            if (route == null)
                return sb.Append("computing…").ToString();

            if (!route.UsesPortals)
            {
                sb.Append("Walk ").Append(Dist(route.Direct)).Append("\n<alpha=#99>no portal saves any walking");
                return sb.ToString();
            }

            sb.Append("Walk ").Append(Dist(route.Walking)).Append(", ")
              .Append(route.Hops).Append(route.Hops == 1 ? " portal hop" : " portal hops")
              .Append("  <alpha=#99>(").Append(Dist(route.Direct)).Append(" direct)");

            int n = 0;
            for (int i = 0; i < route.Legs.Count; i++)
            {
                RouteLeg leg = route.Legs[i];
                if (leg.Kind != LegKind.Hop)
                    continue;
                n++;
                Color c = MapMath.BiomeColor(leg.Link.Other(leg.Portal).Biome);
                sb.Append('\n').Append(n).Append(". ")
                  .Append(leg.Portal.HasTag ? leg.Portal.Tag : "(no tag)")
                  .Append(" → <color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>')
                  .Append(BiomeName(leg.To)).Append("</color>");
                if (leg.Link.Kind != Model.LinkKind.Confirmed)
                    sb.Append("  <alpha=#99>presumed");
            }
            return sb.ToString();
        }

        private static string BiomeName(Vector3 pos)
        {
            WorldGenerator wg = WorldGenerator.instance;
            if (wg == null) return "?";
            try { return wg.GetBiomeSector(pos).GetName(); }
            catch (System.Exception) { return "?"; }
        }

        private static string Dist(float m)
        {
            return m < 1000f ? Mathf.RoundToInt(m) + " m" : (m / 1000f).ToString("0.0") + " km";
        }
    }
}
