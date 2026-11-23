using System;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// One scored destination for a double-clicked item. A single window can expose more than one
    /// inventory, so a candidate always carries the concrete SlotMarker that was found by the
    /// native read-only slot lookup. Scoring fields are filled in by TargetScoringService.
    /// </summary>
    internal sealed class TargetCandidate
    {
        internal PixelWindow Window;
        internal SlotMarker Slot;
        internal GameInventory Inventory;

        internal long FocusStamp;
        internal int OriginalOrder;

        /// <summary>Stable identity of the destination (machine:&lt;id&gt;, storage:&lt;area&gt;, ...).</summary>
        internal string TargetKey = "";

        internal float RecencyScore;
        internal float AffinityScore;
        internal float LearnedScore;
        internal float FinalScore;

        internal string Description
        {
            get { return WindowActivityManager.Describe(Window); }
        }
    }
}
