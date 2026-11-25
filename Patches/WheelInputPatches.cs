using HarmonyLib;
using SmartQuickTransfer.EquipmentWheel;

namespace SmartQuickTransfer.Patches
{
    /// <summary>
    /// Input interception, active only while the wheel is up (plus two recovery frames).
    ///
    /// The game dispatches input through InputActionManager.Update to priority-ordered handlers;
    /// OnEventUpdate() == true short-circuits the rest. Skipping that Update removes left click,
    /// right click, middle click, scroll and toolbox hotkeys from every handler in one place.
    /// The three direct Input.* readers (StoreUIManager, CustomEventHandler, Arm) are patched as
    /// well, so confirmation cannot also fire a double-click, a menu action or a shot.
    ///
    /// Every prefix is a plain boolean guard: when the wheel is closed the game path is untouched,
    /// and because the flag is time-bounded there is no way to leave input blocked permanently.
    /// </summary>
    [HarmonyPatch(typeof(InputActionManager), "Update")]
    internal static class WheelPatch_InputActionManager
    {
        private static bool Prefix()
        {
            return !WheelInputState.Suppress;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "Update")]
    internal static class WheelPatch_StoreUIManager
    {
        private static bool Prefix()
        {
            return !WheelInputState.Suppress;
        }
    }

    [HarmonyPatch(typeof(CustomEventHandler), "Update")]
    internal static class WheelPatch_CustomEventHandler
    {
        private static bool Prefix()
        {
            return !WheelInputState.Suppress;
        }
    }

    [HarmonyPatch(typeof(Arm), "Update")]
    internal static class WheelPatch_Arm
    {
        private static bool Prefix()
        {
            return !WheelInputState.Suppress;
        }
    }

    [HarmonyPatch(typeof(ToolboxHelper), "HandleHotkeys")]
    internal static class WheelPatch_ToolboxHotkeys
    {
        private static bool Prefix()
        {
            return !WheelInputState.Suppress;
        }
    }
}
