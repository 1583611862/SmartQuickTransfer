using System;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// A short-lived, in-memory record of one successful automatic transfer (class A data). It
    /// exists only so a following manual drag can be recognised as a correction. It is NEVER
    /// persisted to disk and is NEVER part of the PreferenceStore. Uses the monotonic
    /// Time.realtimeSinceStartup clock for expiry, plus a UTC timestamp for diagnostics only.
    /// </summary>
    internal sealed class PendingPrediction
    {
        internal string PredictionId = "000000";
        internal string TraceId = "000000";
        internal string ItemKey = "";
        internal GameItem ItemReference;
        internal GameInventory SourceInventory;
        internal GameInventory PredictedInventory;
        internal string SourceKey = "";
        internal string PredictedTargetKey = "";
        internal int RequestedCount;
        internal int MovedCount;
        internal float CreatedAt;
        internal DateTime CreatedUtc;
        internal DateTime ExpiresUtc;
        internal bool IsPending = true;

        internal bool IsExpired(float now, float lifetimeSeconds)
        {
            return now - CreatedAt > lifetimeSeconds;
        }

        /// <summary>Releases temporary Unity object references when a record is retired.</summary>
        internal void ReleaseReferences()
        {
            ItemReference = null;
            SourceInventory = null;
            PredictedInventory = null;
        }
    }
}
