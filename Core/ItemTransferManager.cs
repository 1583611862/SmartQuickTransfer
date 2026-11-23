using System;
using System.Collections.Generic;
using SmartQuickTransfer.Transfer;
using SmartQuickTransfer.Visuals;

namespace SmartQuickTransfer.Core
{
    internal static class ItemTransferManager
    {
        internal static TransferOutcome Transfer(GameItem item, PixelWindow sourceWindow)
        {
            if (item == null)
            {
                return TransferOutcome.NotHandled;
            }

            FlightCapture capture = TransferAnimation.Capture(item);

            try
            {
                EmporiumEntry entry = EmporiumEntry.Instance;
                bool fromGround = entry != null
                    && ReferenceEquals(sourceWindow, entry.afterhourWindow)
                    && ReferenceEquals(item.parentInventory, entry.afterhourInventory);
                if (fromGround)
                {
                    return TransferToAfterhourPocket(item, sourceWindow, capture);
                }

                TransferSettings settings = ModEntry.Settings;
                if (settings != null && settings.CustomerDemandDeskPriority
                    && CustomerDemandResolver.WantsItem(item))
                {
                    GameInventory desk = DesktopTransfer.ResolveDesk(item);
                    if (desk != null)
                    {
                        return TransferToCustomerDesk(item, sourceWindow, desk, capture);
                    }
                    ModEntry.LogInfo("customer wants " + DescribeItem(item)
                        + " but the desk has no valid slot; using the normal route.");
                }

                // Anything taken out of a machine or tool goes back to storage, never to the window
                // that happens to have been focused last. Fixed machinery panels (night ground,
                // vending slots) are storage areas rather than machines and keep the normal routing.
                bool fromMachine = MachineSlotResolver.IsMachineryWindow(sourceWindow)
                    && !WindowActivityManager.IsStorePanel(sourceWindow);

                if (!fromMachine)
                {
                    TransferOutcome scored = TryScoredTargets(item, sourceWindow, capture);
                    if (scored != TransferOutcome.NotHandled)
                    {
                        return scored;
                    }
                }

                return TransferToFallback(item, sourceWindow, capture, fromMachine);
            }
            catch (Exception e)
            {
                ModEntry.LogError("transfer aborted: " + e);
                return TransferOutcome.Failed;
            }
        }

        /// <summary>
        /// Collects all eligible candidates, ranks them with the scoring model, then tries them in
        /// order. Only a complete failure (nothing moved) advances to the next candidate; a partial
        /// move stops immediately (never split a stack across windows). Successful scored transfers
        /// create a short-lived prediction for the correction-learning loop.
        /// Returns NotHandled when there were no candidates, so the caller runs the old fallback.
        /// </summary>
        private static TransferOutcome TryScoredTargets(GameItem item, PixelWindow sourceWindow, FlightCapture capture)
        {
            List<TargetCandidate> candidates = WindowActivityManager.GetEligibleTargets(item, sourceWindow);
            if (candidates == null || candidates.Count == 0)
            {
                return TransferOutcome.NotHandled;
            }

            List<TargetCandidate> ranked = TargetScoringService.ScoreAndRank(item, candidates);

            for (int i = 0; i < ranked.Count; i++)
            {
                TargetCandidate candidate = ranked[i];
                TransferAttemptResult attempt = TryMoveInto(item, sourceWindow, candidate, capture);

                if (attempt.Outcome == AttemptOutcome.NothingMoved)
                {
                    ModEntry.LogInfo("candidate '" + candidate.Description
                        + "' accepted no units; trying the next candidate.");
                    continue;
                }

                // Partial or full success: both stop the loop. We never split a stack further.
                GameInventory destination = candidate.Inventory;
                if (destination != null)
                {
                    // The ORIGINAL source is the pre-transfer snapshot, not item.parentInventory
                    // (which now points at the destination after the native move).
                    GameInventory originalSource = capture != null ? capture.SourceInventory : null;
                    string itemKey = StableIdentityResolver.ResolveItem(item);
                    string sourceKey = StableIdentityResolver.ResolveInventoryOnly(originalSource);
                    PredictionLedger.Record(
                        itemKey,
                        item,
                        originalSource,
                        destination,
                        sourceKey,
                        candidate.TargetKey,
                        attempt.RequestedCount,
                        attempt.MovedCount);
                }

                ModEntry.LogInfo((attempt.IsPartial ? "partially moved " : "moved ") + attempt.MovedCount
                    + " x " + DescribeItem(item) + " from '" + WindowActivityManager.Describe(sourceWindow)
                    + "' into '" + candidate.Description + "' (score=" + candidate.FinalScore.ToString("0.0")
                    + "); item now in " + DescribeFinalArea(item) + ".");

                return TransferOutcome.MovedToWindow;
            }

            // Every scored candidate failed at transfer time; fall back to the old storage routing.
            ModEntry.LogInfo("all scored candidates rejected " + DescribeItem(item) + "; using fallback.");
            return TransferOutcome.NotHandled;
        }

        /// <summary>
        /// Attempts the native move into one candidate while raising the automation guard, so a
        /// simultaneous manual-drag observer can never mistake our own transfer for player input.
        /// </summary>
        private static TransferAttemptResult TryMoveInto(GameItem item, PixelWindow sourceWindow, TargetCandidate candidate, FlightCapture capture)
        {
            TransferAttemptResult result = new TransferAttemptResult();
            result.Candidate = candidate;
            result.RequestedCount = item != null ? item.unitCount : 0;

            if (candidate == null || candidate.Slot == null || candidate.Window == null)
            {
                result.Outcome = AttemptOutcome.NothingMoved;
                return result;
            }

            int moved = 0;
            AutomationTransferGuard.Begin();
            try
            {
                // Use the native window transfer path (same as the old code) so UI refresh and
                // multi-slot acceptance behave exactly as before.
                moved = ContainerTransfer.MoveInto(candidate.Window, item, sourceWindow);
            }
            catch (Exception e)
            {
                ModEntry.LogError("scored transfer into '" + candidate.Description + "' failed: " + e.Message);
            }
            finally
            {
                AutomationTransferGuard.End();
            }

            result.MovedCount = moved;
            if (moved <= 0)
            {
                result.Outcome = AttemptOutcome.NothingMoved;
                return result;
            }

            // Play the flight animation exactly once, as the old single-target path did.
            TransferAnimation.Play(capture, item, candidate.Slot, ShouldHideSource(capture, item, moved, result.RequestedCount));
            result.Outcome = moved < result.RequestedCount
                ? AttemptOutcome.PartialSuccess
                : AttemptOutcome.FullSuccess;
            return result;
        }

        private static TransferOutcome TransferToAfterhourPocket(GameItem item, PixelWindow sourceWindow, FlightCapture capture)
        {
            GameInventory pocket = DesktopTransfer.ResolveAfterhourPocket(item);
            if (pocket == null)
            {
                ModEntry.LogInfo("afterhour backpack and pockets cannot accept " + DescribeItem(item) + "; item kept in place.");
                return TransferOutcome.RejectedByTarget;
            }

            SlotMarker slot = FindSlot(pocket, item);
            int requested = item.unitCount;
            int moved = GuardedMove(pocket, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("afterhour pocket transfer of " + DescribeItem(item) + " moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            ModEntry.LogInfo("moved " + moved + " x " + DescribeItem(item) + " from the dumping ground into "
                + DesktopTransfer.DescribeArea(pocket) + "; item now in " + DescribeFinalArea(item) + ".");
            return TransferOutcome.MovedToWindow;
        }

        private static TransferOutcome TransferToCustomerDesk(GameItem item, PixelWindow sourceWindow, GameInventory desk, FlightCapture capture)
        {
            SlotMarker slot = FindSlot(desk, item);
            int requested = item.unitCount;
            int moved = GuardedMove(desk, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("customer-demand transfer of " + DescribeItem(item)
                    + " into the desk moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            ModEntry.LogInfo("customer wants " + DescribeItem(item) + "; moved " + moved + " x "
                + DescribeItem(item) + " from '" + WindowActivityManager.Describe(sourceWindow)
                + "' into " + DesktopTransfer.DescribeArea(desk) + "; item now in "
                + DescribeFinalArea(item) + ".");
            return TransferOutcome.MovedToCustomerDesk;
        }

        private static TransferOutcome TransferToFallback(GameItem item, PixelWindow sourceWindow, FlightCapture capture, bool fromMachine)
        {
            GameInventory destination = DesktopTransfer.Resolve(item, sourceWindow, fromMachine);
            if (destination == null)
            {
                ModEntry.LogInfo("no usable storage accepts " + DescribeItem(item) + "; item kept in place.");
                return TransferOutcome.RejectedByTarget;
            }

            SlotMarker slot = FindSlot(destination, item);
            int requested = item.unitCount;
            int moved = GuardedMove(destination, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("fallback transfer of " + DescribeItem(item) + " moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            ModEntry.LogInfo((fromMachine ? "taken out of a machine" : "no target window open")
                + "; moved " + moved + " x " + DescribeItem(item) + " from '"
                + WindowActivityManager.Describe(sourceWindow) + "' into "
                + DesktopTransfer.DescribeArea(destination) + "; item now in "
                + DescribeFinalArea(item) + ".");
            return TransferOutcome.MovedToFallback;
        }

        /// <summary>Guarded wrapper around the native inventory transfer (DesktopTransfer path).</summary>
        private static int GuardedMove(GameInventory destination, GameItem item, PixelWindow source)
        {
            AutomationTransferGuard.Begin();
            try
            {
                return DesktopTransfer.MoveInto(destination, item, source);
            }
            finally
            {
                AutomationTransferGuard.End();
            }
        }

        private static bool ShouldHideSource(FlightCapture capture, GameItem item, int moved, int requested)
        {
            if (capture == null || moved < requested || item == null)
            {
                return false;
            }

            // Only hide the source icon when the whole stack actually left its old container;
            // a partial transfer or a stack merge would otherwise hide the remainder.
            return item.parentInventory != null
                && !ReferenceEquals(item.parentInventory, capture.SourceInventory);
        }

        private static SlotMarker FindSlot(GameInventory inventory, GameItem item)
        {
            try
            {
                return inventory.TryFindOneValidInventorySlot(item);
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not resolve the destination slot: " + e.Message);
                return null;
            }
        }

        private static string DescribeItem(GameItem item)
        {
            if (item == null)
            {
                return "<null>";
            }

            string identifier = string.IsNullOrEmpty(item.identifier) ? "<unknown>" : item.identifier;
            return "'" + identifier + "'";
        }

        /// <summary>
        /// Reads back where the item really ended up after the native accept, so a double hop
        /// (for example 桌面 first and 仓库 afterwards) shows up directly in the log.
        /// </summary>
        private static string DescribeFinalArea(GameItem item)
        {
            try
            {
                if (item == null || item.parentInventory == null)
                {
                    return "a merged stack or nowhere";
                }
                return DesktopTransfer.DescribeArea(item.parentInventory);
            }
            catch (Exception)
            {
                return "<unknown>";
            }
        }
    }
}
