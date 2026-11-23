using System;
using System.Collections.Generic;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// CLASS A DATA - temporary predictions. Bounded, in-memory only. These records exist solely to
    /// link one automatic transfer to a possible subsequent player correction. They are NEVER
    /// written to the PreferenceStore and clearing them NEVER affects persisted learning data.
    /// Expired records are pruned lazily with the monotonic clock. A record is consumed exactly once.
    /// </summary>
    internal static class PredictionLedger
    {
        internal const float PredictionLifetimeSeconds = 20f;
        internal const int MaxPending = 64;

        private static readonly List<PendingPrediction> Entries = new List<PendingPrediction>();

        internal static void Record(
            string traceId,
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
            PruneExpired(now);

            PendingPrediction entry = new PendingPrediction();
            entry.PredictionId = TransferDiagnostics.NextPredictionId();
            entry.TraceId = string.IsNullOrEmpty(traceId) ? "000000" : traceId;
            entry.ItemKey = itemKey;
            entry.ItemReference = itemReference;
            entry.SourceInventory = sourceInventory;
            entry.PredictedInventory = predictedInventory;
            entry.SourceKey = sourceKey;
            entry.PredictedTargetKey = predictedTargetKey;
            entry.RequestedCount = requestedCount;
            entry.MovedCount = movedCount;
            entry.CreatedAt = now;
            entry.CreatedUtc = DateTime.UtcNow;
            entry.ExpiresUtc = entry.CreatedUtc.AddSeconds(PredictionLifetimeSeconds);
            entry.IsPending = true;

            Entries.Add(entry);

            // Capacity eviction: oldest first, with an explicit reason + count log.
            int evicted = 0;
            while (Entries.Count > MaxPending)
            {
                Retire(Entries[0], "capacity", false);
                Entries.RemoveAt(0);
                evicted++;
            }
            if (evicted > 0)
            {
                TransferDiagnostics.PredictionLifecycle("evicted", "", "capacity", Entries.Count);
            }

            TransferDiagnostics.PredictionRegistered(
                entry.TraceId, entry.PredictionId, itemKey, sourceKey, predictedTargetKey,
                requestedCount, movedCount, Entries.Count);
        }

        /// <summary>
        /// Find the most recent still-pending prediction whose predicted destination inventory is
        /// the given source inventory. Returns null when nothing matches (the caller logs the reason).
        /// </summary>
        internal static PendingPrediction FindByPredictedInventory(GameInventory predictedSource)
        {
            if (predictedSource == null)
            {
                return null;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            PruneExpired(now);
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

        /// <summary>True when exactly one pending prediction matches the given item key and inventory.</summary>
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
            Retire(prediction, "matched", true);
            Entries.Remove(prediction);
            TransferDiagnostics.PredictionLifecycle("consumed", prediction.PredictionId, "matched", Entries.Count);
        }

        /// <summary>Lazily drop expired records using the monotonic clock; log aggregate count only.</summary>
        private static void PruneExpired(float now)
        {
            int expired = 0;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                PendingPrediction p = Entries[i];
                if (p.IsExpired(now, PredictionLifetimeSeconds))
                {
                    Retire(p, "expired", false);
                    Entries.RemoveAt(i);
                    expired++;
                }
            }
            if (expired > 0)
            {
                TransferDiagnostics.PredictionLifecycle("expired", "", "timeout", Entries.Count);
            }
        }

        private static void Retire(PendingPrediction p, string reason, bool consumed)
        {
            if (p == null)
            {
                return;
            }
            p.IsPending = false;
            // Release temporary Unity object references so retired records do not keep objects alive.
            p.ReleaseReferences();
        }

        /// <summary>
        /// CLASS A only: clears temporary predictions for a lifecycle reason. This method never
        /// touches PreferenceStore or persisted learning data.
        /// </summary>
        internal static void ClearPendingPredictions(string reason)
        {
            int count = Entries.Count;
            for (int i = 0; i < Entries.Count; i++)
            {
                Retire(Entries[i], reason, false);
            }
            Entries.Clear();
            if (count > 0 || reason == "mod_disable" || reason == "game_exit" || reason == "mod_unload")
            {
                TransferDiagnostics.PredictionLifecycle("cleared", "", reason, Entries.Count);
            }
        }

        internal static int Count
        {
            get { return Entries.Count; }
        }

        /// <summary>Compatibility alias used by ModEntry shutdown paths.</summary>
        internal static void Clear()
        {
            ClearPendingPredictions("lifecycle");
        }
    }
}
