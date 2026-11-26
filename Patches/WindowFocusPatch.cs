using System;
using HarmonyLib;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Patches
{
    /// <summary>
    /// Records player window intent by observing the native focus/opening methods.
    ///
    /// PixelWindow.ToFront is the single place the game brings a window forward, and it is called
    /// when the player clicks an inventory background, drops an item, or opens a content window.
    /// PixelWindow.Show is called when a window is first opened. Both therefore express intent.
    ///
    /// The mod's own transfers also bring windows forward through Refresh; those run under
    /// AutomationTransferGuard and WindowIntentTracker.Touch ignores them, so automation cannot
    /// pollute the intent ranking.
    ///
    /// These are read-only postfixes: they never alter game state or the original call flow.
    /// </summary>
    [HarmonyPatch(typeof(PixelWindow), "ToFront")]
    internal static class WindowFocusPatch_ToFront
    {
        private static void Postfix(PixelWindow __instance)
        {
            try
            {
                WindowIntentTracker.Touch(__instance);
            }
            catch (Exception e)
            {
                ModEntry.LogError("ToFront intent patch failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PixelWindow), "Show")]
    internal static class WindowFocusPatch_Show
    {
        private static void Postfix(PixelWindow __instance)
        {
            try
            {
                WindowIntentTracker.Touch(__instance);
            }
            catch (Exception e)
            {
                ModEntry.LogError("Show intent patch failed: " + e.Message);
            }
        }
    }
}
