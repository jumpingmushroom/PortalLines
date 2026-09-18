using HarmonyLib;
using PortalLines.UI;

namespace PortalLines.Patches
{
    /// <summary>
    /// UpdatePins overwrites every pin icon's colour on each pass (white, or grey for shared
    /// pins), so tinting has to happen after it rather than once at creation.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    public static class Minimap_UpdatePins_Patch
    {
        private static void Postfix()
        {
            PortalPins.ApplyTints();
        }
    }
}
