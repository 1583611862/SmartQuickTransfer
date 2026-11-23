using System;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Distinguishes transfers caused by this mod from genuine player drags. The native player
    /// drag completes inside ItemMouseDragHandler.EndDrag, which this mod's own transfer path never
    /// calls - so a manual-drag patch is already isolated by construction. This guard is an extra
    /// safety net: while it is raised, any observer callback is ignored outright.
    /// </summary>
    internal static class AutomationTransferGuard
    {
        private static int Depth;

        internal static bool IsAutomating
        {
            get { return Depth > 0; }
        }

        internal static void Begin()
        {
            Depth++;
        }

        internal static void End()
        {
            if (Depth > 0)
            {
                Depth--;
            }
        }
    }
}
