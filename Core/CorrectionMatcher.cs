using System;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Decides whether a genuine manual transfer is a correction of a recent automatic prediction.
    /// All conditions from the task book must hold; when anything is ambiguous we deliberately do
    /// nothing ("宁愿少学习，也不能错误学习").
    /// </summary>
    internal static class CorrectionMatcher
    {
        internal static void Evaluate(ManualTransferEvent ev)
        {
            if (ev == null || ev.SourceInventory == null || ev.DestinationInventory == null)
            {
                return;
            }

            // The player must drag the item back OUT of the inventory we predicted as the target.
            PendingPrediction prediction = PredictionLedger.FindByPredictedInventory(ev.SourceInventory);
            if (prediction == null)
            {
                return;
            }

            // Same item: prefer object reference (handles the normal no-merge case). If the
            // original reference was merged/destroyed, require the stable key to match AND that
            // the manual drag item is the only unit of that key leaving the source - otherwise
            // several identical items make the association ambiguous, so skip.
            bool sameItem;
            if (prediction.ItemReference != null && ev.ItemReference != null)
            {
                sameItem = ReferenceEquals(prediction.ItemReference, ev.ItemReference);
            }
            else
            {
                sameItem = prediction.ItemKey == ev.ItemKey;
            }
            if (!sameItem)
            {
                return;
            }
            if (prediction.ItemKey != ev.ItemKey)
            {
                return;
            }

            // Destination must differ from the predicted target.
            string actualKey = ev.DestinationKey;
            if (string.IsNullOrEmpty(actualKey) || actualKey == prediction.PredictedTargetKey)
            {
                return;
            }

            // Ambiguity guard: if another pending prediction for the same item key also targets
            // this inventory, we cannot tell which one the player is correcting. Skip.
            if (!PredictionLedger.IsUniquePendingFor(prediction.ItemKey, ev.SourceInventory))
            {
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
                return;
            }

            ModEntry.LogInfo("correction: item=" + prediction.ItemKey
                + ", predicted=" + prediction.PredictedTargetKey
                + ", actual=" + actualKey);

            // Consume before learning so a second drag in the same frame cannot double-learn.
            PredictionLedger.Consume(prediction);
            PreferenceLearningService.ApplyCorrection(
                prediction.ItemKey,
                prediction.PredictedTargetKey,
                actualKey,
                weight);
        }
    }
}
