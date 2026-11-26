using System;
using HarmonyLib;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Patches
{
    /// <summary>
    /// Listens to the completion of a genuine player item drag. ItemMouseDragHandler.EndDrag is the
    /// single native method that finalises manual drags; the mod's own transfers never call it.
    /// A Prefix snapshots the source/destination before the transfer, a Postfix reads the result.
    /// No Transpiler is needed and no game state is modified.
    /// </summary>
    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class ManualItemTransferPatch
    {
        private static void Prefix(ItemMouseDragHandler __instance, bool cancelled)
        {
            try
            {
                ManualTransferObserver.OnDragCompleting(__instance, cancelled);
            }
            catch (Exception e)
            {
                ModEntry.LogError("drag prefix failed: " + e.Message);
            }
        }

        private static void Postfix(ItemMouseDragHandler __instance, bool cancelled)
        {
            try
            {
                ManualTransferEvent ev = ManualTransferObserver.OnDragCompleted(__instance, cancelled);
                if (ev != null)
                {
                    CorrectionMatcher.Evaluate(ev);
                    if (!ev.WasMatched)
                    {
                        // A genuine cross-inventory drag that is not a correction is still the
                        // clearest statement of player intent: "this item goes in B". Feed a direct
                        // positive preference for the chosen destination. When it WAS a correction,
                        // ApplyCorrection already recorded the positive evidence, so skip here.
                        PreferenceLearningService.LearningUpdateInfo direct =
                            PreferenceLearningService.RecordDirectPreference(
                                ev.ItemKey, ev.DestinationKey, 1f);
                        TransferDiagnostics.DirectPreference(
                            ev.ItemIdentifier, ev.DestinationKey, direct.Success, direct.LearningWeight);
                    }
                    TransferDiagnostics.ManualDrag(
                        ev.ItemIdentifier,
                        ev.SourceKey,
                        ev.DestinationKey,
                        ev.MovedCount,
                        ev.WasMatched,
                        ev.WasMatched ? "" : (string.IsNullOrEmpty(ev.LogRejectReason) ? "no_pending_prediction" : ev.LogRejectReason));
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("drag postfix failed: " + e.Message);
            }
        }
    }
}
