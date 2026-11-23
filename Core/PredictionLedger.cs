using System;
using System.Collections.Generic;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Holds the most recent automatic-transfer predictions for a short time window. Bounded,
    /// in-memory only. Expired records are pruned lazily on read. A record is consumed exactly
    /// once when it is matched against a user correction.
    /// </summary>
    internal static class PredictionLedger
    {
        internal const float PredictionLifetimeSeconds = 20f;
        internal const int MaxPending = 64;

        private static readonly List<PendingPrediction> Entries = new List<PendingPrediction>();

        internal static void Record(
            string itemKey,
            GameItem itemReference,
            GameInventory sourceInventory,
            GameInventory predictedInventory,
            string sourceKey,
            string predictedTargetKey,
            int requestedCount,
            int movedCount)
        {
            if (movedCount <= 0 || predictedInventory == null || string.IsNullOrEmpty(itemKey))
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            Prune(now);

            PendingPrediction entry = new PendingPrediction();
            entry.ItemKey = itemKey;
            entry.ItemReference = itemReference;
            entry.SourceInventory = sourceInventory;
            entry.PredictedInventory = predictedInventory;
            entry.SourceKey = sourceKey;
            entry.PredictedTargetKey = predictedTargetKey;
            entry.RequestedCount = requestedCount;
            entry.MovedCount = movedCount;
            entry.CreatedAt = now;
            entry.IsPending = true;

            Entries.Add(entry);
            while (Entries.Count > MaxPending)
            {
                Entries.RemoveAt(0);
            }

            ModEntry.LogInfo("prediction: item=" + itemKey + ", target=" + predictedTargetKey);
        }

        /// <summary>
        /// Find the most recent still-pending prediction whose predicted destination inventory is
        /// the given source inventory. Corrections always drag the item back OUT of the predicted
        /// target, so this is the anchor of the match. Returns null when nothing matches.
        /// </summary>
        internal static PendingPrediction FindByPredictedInventory(GameInventory predictedSource)
        {
            if (predictedSource == null)
            {
                return null;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            Prune(now);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                PendingPrediction p = Entries[i];
                if (p.IsPending
                    && !p.IsExpired(now, PredictionLifetimeSeconds)
                    && ReferenceEquals(p.PredictedInventory, predictedSource))
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>
        /// True when exactly one pending prediction matches the given item key and predicted
        /// inventory. Used to avoid learning when several identical items make the match ambiguous.
        /// </summary>
        internal static bool IsUniquePendingFor(string itemKey, GameInventory predictedInventory)
        {
            int count = 0;
            float now = UnityEngine.Time.realtimeSinceStartup;
            for (int i = 0; i < Entries.Count; i++)
            {
                PendingPrediction p = Entries[i];
                if (p.IsPending
                    && !p.IsExpired(now, PredictionLifetimeSeconds)
                    && p.ItemKey == itemKey
                    && ReferenceEquals(p.PredictedInventory, predictedInventory))
                {
                    count++;
                    if (count > 1)
                    {
                        return false;
                    }
                }
            }
            return count == 1;
        }

        internal static void Consume(PendingPrediction prediction)
        {
            if (prediction == null)
            {
                return;
            }
            prediction.IsPending = false;
            Entries.Remove(prediction);
        }

        /// <summary>Drop expired entries and entries that are no longer pending.</summary>
        private static void Prune(float now)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].IsPending || Entries[i].IsExpired(now, PredictionLifetimeSeconds))
                {
                    Entries.RemoveAt(i);
                }
            }
        }

        internal static void Clear()
        {
            Entries.Clear();
        }
    }
}
