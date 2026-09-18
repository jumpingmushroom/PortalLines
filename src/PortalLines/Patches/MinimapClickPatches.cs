using HarmonyLib;
using PortalLines.UI;

namespace PortalLines.Patches
{
    /// <summary>
    /// Shift-click on the large map belongs to the route planner; keep the map's own click
    /// handlers (cross off pin, add pin, remove pin) out of the way while Shift is held.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    public static class Minimap_OnMapLeftClick_Patch
    {
        private static bool Prefix()
        {
            return !RouteInput.ShiftHeld();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
    public static class Minimap_OnMapDblClick_Patch
    {
        private static bool Prefix()
        {
            return !RouteInput.ShiftHeld();
        }
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    public static class Minimap_RemovePinUnderPointer_Patch
    {
        private static bool Prefix()
        {
            return !RouteInput.ShiftHeld();
        }
    }
}
