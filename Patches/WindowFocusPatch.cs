using System;
using HarmonyLib;
using SmartQuickTransfer.Core;
using UnityEngine;

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
    internal static class WindowFocusIntent
    {
        internal static void Touch(PixelWindow window, string origin)
        {
            try
            {
                WindowIntentTracker.Touch(window);
            }
            catch (Exception e)
            {
                ModEntry.LogError(origin + " intent patch failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PixelWindow), "ToFront", typeof(bool))]
    internal static class WindowFocusPatch_ToFront
    {
        private static void Postfix(PixelWindow __instance)
        {
            WindowFocusIntent.Touch(__instance, "ToFront");
        }
    }

    // PixelWindow.Show has multiple overloads. Binding by name alone makes Harmony throw
    // AmbiguousMatchException and aborts every patch in the mod, so bind exact signatures.
    // The no-argument and the fully-specified overloads are the two independent
    // implementations; Show(Vector2, bool) forwards to the latter, so these two cover all opens.
    [HarmonyPatch(typeof(PixelWindow), "Show", new Type[0])]
    internal static class WindowFocusPatch_Show
    {
        private static void Postfix(PixelWindow __instance)
        {
            WindowFocusIntent.Touch(__instance, "Show");
        }
    }

    [HarmonyPatch(typeof(PixelWindow), "Show", typeof(Vector2), typeof(Vector2), typeof(bool))]
    internal static class WindowFocusPatch_ShowWithPosition
    {
        private static void Postfix(PixelWindow __instance)
        {
            WindowFocusIntent.Touch(__instance, "ShowWithPosition");
        }
    }
}
