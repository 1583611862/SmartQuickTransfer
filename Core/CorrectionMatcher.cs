using System;
using System.Diagnostics;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Decides whether a genuine manual transfer is a correction of a recent automatic prediction.
    /// All conditions from the task book must hold; when anything is ambiguous we deliberately do
    /// nothing. Decision logic is UNCHANGED; structured reject/match logging was added around it.
    /// </summary>
    internal static class CorrectionMatcher
    {
        internal static void Evaluate(ManualTransferEvent ev)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (ev == null || ev.SourceInventory == null || ev.DestinationInventory == null)
            {
                Reject(ev, "transfer_failed");
                return;
            }

            // The player must drag the item back OUT of the inventory we predicted as the target.
            PendingPrediction prediction = PredictionLedger.FindByPredictedInventory(ev.SourceInventory);
            if (prediction == null)
            {
                Reject(ev, "no_pending_prediction");
                return;
            }

            // Same item: prefer object reference; if merged, require stable key + uniqueness.
            bool sameItem;
            string association;
            if (prediction.ItemReference != null && ev.ItemReference != null)
            {
                sameItem = ReferenceEquals(prediction.ItemReference, ev.ItemReference);
                association = "object_reference";
            }
            else
            {
                sameItem = prediction.ItemKey == ev.ItemKey;
                association = "stable_key";
            }
            if (!sameItem)
            {
                Reject(ev, "item_mismatch");
                return;
            }
            if (prediction.ItemKey != ev.ItemKey)
            {
                Reject(ev, "item_mismatch");
                return;
            }

            // Destination must differ from the predicted target.
            string actualKey = ev.DestinationKey;
            if (string.IsNullOrEmpty(actualKey))
            {
                Reject(ev, "transfer_failed");
                return;
            }
            if (actualKey == prediction.PredictedTargetKey)
            {
                Reject(ev, "same_target");
                return;
            }

            // Ambiguity guard: several identical items cannot be uniquely attributed.
            if (!PredictionLedger.IsUniquePendingFor(prediction.ItemKey, ev.SourceInventory))
            {
                Reject(ev, "item_identity_ambiguous");
                return;
            }

            float weight = 1f;
            if (prediction.MovedCount > 0 && ev.MovedCount > 0 && ev.MovedCount < prediction.MovedCount)
            {
                weight = (float)ev.MovedCount / prediction.MovedCount;
            }
            if (weight > 1f)
            {
                weight = 1f;
            }
            if (weight <= 0f)
            {
                Reject(ev, "transfer_failed");
                return;
            }

            ev.WasMatched = true;
            float interval = (float)(DateTime.UtcNow - prediction.CreatedUtc).TotalSeconds;
            TransferDiagnostics.CorrectionMatched(
                prediction.PredictionId, prediction.ItemKey, prediction.PredictedTargetKey, actualKey,
                association, interval, prediction.MovedCount, ev.MovedCount, weight);

            // Consume before learning so a second drag in the same frame cannot double-learn.
            PredictionLedger.Consume(prediction);

            PreferenceLearningService.LearningUpdateInfo info =
                PreferenceLearningService.ApplyCorrection(
                    prediction.ItemKey, prediction.PredictedTargetKey, actualKey, weight);
            TransferDiagnostics.LearningUpdate(
                prediction.PredictionId, info.ItemKey, info.OldTarget, info.NewTarget,
                info.OldPositiveBefore, info.OldPositiveAfter,
                info.OldNegativeBefore, info.OldNegativeAfter,
                info.NewPositiveBefore, info.NewPositiveAfter,
                info.NewNegativeBefore, info.NewNegativeAfter,
                info.CorrectionEvents, info.Confidence, info.LearningWeight, info.FeedbackWeight,
                info.Success);

            sw.Stop();
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.CorrectionMatch, sw.Elapsed.TotalMilliseconds);
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.LearningUpdate, sw.Elapsed.TotalMilliseconds);
        }

        private static void Reject(ManualTransferEvent ev, string reason)
        {
            if (ev != null)
            {
                ev.WasMatched = false;
                ev.LogRejectReason = reason;
            }
            TransferDiagnostics.CorrectionRejected(reason);
        }
    }
}
