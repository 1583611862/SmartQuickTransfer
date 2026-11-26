using System;
using System.Collections.Generic;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Tracks the player's explicit intent over windows with an independent monotonic clock.
    ///
    /// The game's native PixelWindow.focusStamp is refreshed by ToFront() on many unrelated
    /// actions (clicking an inventory background, starting/finishing a drag, opening a content
    /// window), so it reflects "last brought to front", not "the container the player actually
    /// wants as a target". This tracker records intent at the points that express a real choice:
    /// opening a window, bringing it to the front through player input, or dropping an item into it.
    ///
    /// The mod's own transfers also call ToFront() through Refresh; those are filtered out by
    /// AutomationTransferGuard so automation never feeds back into the intent ranking.
    ///
    /// A double-click first presses on the source window, which would otherwise mark the source as
    /// the most recent. Callers take an immutable snapshot at the very start of the double-click
    /// (before that press is processed) and score against the snapshot.
    ///
    /// All state is in-memory and bounded; no persistence, no per-frame work.
    /// </summary>
    internal static class WindowIntentTracker
    {
        private static readonly Dictionary<PixelWindow, long> Stamps = new Dictionary<PixelWindow, long>();
        private static long Counter;
        private static long SnapshotCounter;

        /// <summary>
        /// The frozen snapshot for the decision currently in progress. Set once at the very start
        /// of a double-click so later focus changes cannot move the goalposts mid-decision.
        /// </summary>
        private static Snapshot Current;

        /// <summary>Clear all intent state. Called on session boundaries / mod unload.</summary>
        internal static void Reset()
        {
            Stamps.Clear();
            Counter = 0;
            SnapshotCounter = 0;
            Current = null;
        }

        /// <summary>Record that the player expressed intent toward a window (open / focus / drop).</summary>
        internal static void Touch(PixelWindow window)
        {
            if (window == null)
            {
                return;
            }
            if (AutomationTransferGuard.IsAutomating)
            {
                return;
            }
            try
            {
                Counter++;
                Stamps[window] = Counter;
            }
            catch (Exception e)
            {
                ModEntry.LogError("window intent touch failed: " + e.Message);
            }
        }

        /// <summary>
        /// Freeze the current intent state for one decision. The returned snapshot is immutable and
        /// unaffected by later Touch() calls, so a press on the source during the double-click
        /// cannot change the ranking already in progress.
        /// </summary>
        internal static Snapshot TakeSnapshot()
        {
            SnapshotCounter++;
            Dictionary<PixelWindow, long> copy = new Dictionary<PixelWindow, long>(Stamps);
            Current = new Snapshot(SnapshotCounter, copy);
            return Current;
        }

        /// <summary>Intent stamp from the active decision snapshot; 0 when none or never touched.</summary>
        internal static long CurrentStampOf(PixelWindow window)
        {
            return StampOf(Current, window);
        }

        /// <summary>Intent stamp of a window at snapshot time; 0 when the window was never touched.</summary>
        internal static long StampOf(Snapshot snapshot, PixelWindow window)
        {
            if (snapshot == null || window == null)
            {
                return 0L;
            }
            long value;
            return snapshot.Stamps.TryGetValue(window, out value) ? value : 0L;
        }

        internal sealed class Snapshot
        {
            internal readonly long Id;
            internal readonly Dictionary<PixelWindow, long> Stamps;

            internal Snapshot(long id, Dictionary<PixelWindow, long> stamps)
            {
                Id = id;
                Stamps = stamps;
            }
        }
    }
}
