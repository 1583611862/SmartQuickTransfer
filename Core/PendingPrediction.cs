using System;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// A short-lived, in-memory record of one successful automatic transfer. It exists only so a
    /// following manual drag can be recognised as a correction. It is never persisted to disk.
    /// Uses the monotonic Time.realtimeSinceStartup clock for expiry.
    /// </summary>
    internal sealed class PendingPrediction
    {
        internal string ItemKey = "";
        internal GameItem ItemReference;
        internal GameInventory SourceInventory;
        internal GameInventory PredictedInventory;
        internal string SourceKey = "";
        internal string PredictedTargetKey = "";
        internal int RequestedCount;
        internal int MovedCount;
        internal float CreatedAt;
        internal bool IsPending = true;

        internal bool IsExpired(float now, float lifetimeSeconds)
        {
            return now - CreatedAt > lifetimeSeconds;
        }
    }
}
