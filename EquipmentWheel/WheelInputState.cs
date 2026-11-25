using UnityEngine;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Shared suppression flag read by the Harmony patches. The wheel controller is the only
    /// writer. Closing is fail-safe: suppression ends after a couple of frames even if the
    /// controller is destroyed before its close animation finishes, so input can never remain
    /// blocked permanently.
    /// </summary>
    internal static class WheelInputState
    {
        private const int ReleaseFrames = 2;

        private static bool open;
        private static int suppressUntilFrame;

        internal static bool IsOpen { get { return open; } }

        internal static bool Suppress
        {
            get { return open || Time.frameCount <= suppressUntilFrame; }
        }

        internal static void SetOpen(bool value)
        {
            open = value;
            suppressUntilFrame = value ? 0 : Time.frameCount + ReleaseFrames;
        }

        internal static void ForceClear()
        {
            open = false;
            suppressUntilFrame = 0;
        }
    }
}
