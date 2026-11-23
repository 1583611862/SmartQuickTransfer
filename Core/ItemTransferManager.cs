using System;
using System.Collections.Generic;
using System.Diagnostics;
using SmartQuickTransfer.Transfer;
using SmartQuickTransfer.Visuals;

namespace SmartQuickTransfer.Core
{
    internal static class ItemTransferManager
    {
        internal static TransferOutcome Transfer(GameItem item, PixelWindow sourceWindow)
        {
            return Transfer(item, sourceWindow, null);
        }

        internal static TransferOutcome Transfer(GameItem item, PixelWindow sourceWindow, string traceId)
        {
            if (item == null)
            {
                return TransferOutcome.NotHandled;
            }

            // Business-event-driven periodic save check; never per-frame, never a background thread.
            PreferenceLearningService.TickSafeSave();

            FlightCapture capture = TransferAnimation.Capture(item);

            try
            {
                EmporiumEntry entry = EmporiumEntry.Instance;
                bool fromGround = entry != null
                    && ReferenceEquals(sourceWindow, entry.afterhourWindow)
                    && ReferenceEquals(item.parentInventory, entry.afterhourInventory);
                if (fromGround)
                {
                    Branch(traceId, "night_ground");
                    TransferOutcome outcome = TransferToAfterhourPocket(item, sourceWindow, capture);
                    ReportSpecial(traceId, "night_ground", item, DescribeFinalArea(item), outcome);
                    return outcome;
                }

                TransferSettings settings = ModEntry.Settings;
                if (settings != null && settings.CustomerDemandDeskPriority
                    && CustomerDemandResolver.WantsItem(item))
                {
                    GameInventory desk = DesktopTransfer.ResolveDesk(item);
                    if (desk != null)
                    {
                        Branch(traceId, "customer_demand");
                        TransferOutcome outcome = TransferToCustomerDesk(item, sourceWindow, desk, capture);
                        ReportSpecial(traceId, "customer_demand", item, DesktopTransfer.DescribeArea(desk), outcome);
                        return outcome;
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
                    Branch(traceId, "normal");
                    TransferOutcome scored = TryScoredTargets(item, sourceWindow, capture, traceId);
                    if (scored != TransferOutcome.NotHandled)
                    {
                        return scored;
                    }
                }
                else
                {
                    Branch(traceId, "machine_return");
                }

                TransferOutcome fallbackOutcome = TransferToFallback(item, sourceWindow, capture, fromMachine);
                ReportSpecial(traceId, fromMachine ? "machine_return" : "fallback", item,
                    DescribeFinalArea(item), fallbackOutcome);
                return fallbackOutcome;
            }
            catch (Exception e)
            {
                ModEntry.LogError("transfer aborted: " + e);
                TransferDiagnostics.ErrorThrottled("transfer", "transfer aborted: " + e.Message);
                return TransferOutcome.Failed;
            }
        }

        /// <summary>
        /// Bulk path for a band selection. This mirrors the base game's group placement: one
        /// destination is chosen once (using the double-clicked item as representative), then the
        /// whole selection is committed into that single inventory with the game's own placement
        /// primitive. It is one decision and one batch commit - not N independent smart transfers
        /// that each pick their own target and play their own trace.
        /// </summary>
        internal static int TransferBatch(List<GameItem> items, PixelWindow sourceWindow, string traceId)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }
            PreferenceLearningService.TickSafeSave();
            Branch(traceId, "batch");

            GameItem representative = items[0];
            EmporiumEntry entry = EmporiumEntry.Instance;
            TransferSettings settings = ModEntry.Settings;

            // 1) Choose one shared destination, using the same decision as a single double-click
            //    but evaluated exactly once from the representative item.
            GameInventory destination = null;
            PixelWindow destinationWindow = null;
            string predictedKey = null;

            bool fromGround = entry != null
                && ReferenceEquals(sourceWindow, entry.afterhourWindow)
                && ReferenceEquals(representative.parentInventory, entry.afterhourInventory);

            if (fromGround)
            {
                destination = DesktopTransfer.ResolveAfterhourPocket(representative);
                destinationWindow = entry.afterhourWindow;
            }
            else
            {
                if (settings != null && settings.CustomerDemandDeskPriority
                    && CustomerDemandResolver.WantsItem(representative))
                {
                    GameInventory desk = DesktopTransfer.ResolveDesk(representative);
                    if (desk != null)
                    {
                        destination = desk;
                        destinationWindow = entry != null ? entry.backInvWindow : null;
                    }
                }

                bool fromMachine = MachineSlotResolver.IsMachineryWindow(sourceWindow)
                    && !WindowActivityManager.IsStorePanel(sourceWindow);

                if (destination == null && !fromMachine)
                {
                    List<TargetCandidate> candidates =
                        WindowActivityManager.GetEligibleTargets(representative, sourceWindow, traceId);
                    if (candidates != null && candidates.Count > 0)
                    {
                        List<TargetCandidate> ranked =
                            TargetScoringService.ScoreAndRank(representative, candidates, traceId);
                        if (ranked.Count > 0 && ranked[0].Inventory != null)
                        {
                            destination = ranked[0].Inventory;
                            destinationWindow = ranked[0].Window;
                            predictedKey = ranked[0].TargetKey;
                        }
                    }
                }

                if (destination == null)
                {
                    destination = DesktopTransfer.Resolve(representative, sourceWindow, fromMachine);
                    if (destination != null)
                    {
                        destinationWindow = GraphUtils.FindParentType<PixelWindow>(destination);
                    }
                }
            }

            if (destination == null)
            {
                ModEntry.LogInfo("band double-click: no destination accepts the selection; items kept in place.");
                TransferDiagnostics.TransferResult(traceId, "n/a_batch", "n/a_batch", 0, 0,
                    "RejectedByTarget", false, false, "unknown", "unavailable");
                return 0;
            }
            if (string.IsNullOrEmpty(predictedKey))
            {
                predictedKey = StableIdentityResolver.ResolveTarget(destinationWindow, destination);
            }

            // 2) Commit the whole selection into that one destination in a single guarded batch,
            //    using the same placement primitive the base game uses for group drag-drop.
            int totalRequested = 0;
            int totalMoved = 0;
            AutomationTransferGuard.Begin();
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    GameItem item = items[i];
                    if (item == null || item.parentInventory == null)
                    {
                        continue;
                    }
                    int requested = item.unitCount;
                    if (requested <= 0)
                    {
                        continue;
                    }
                    totalRequested += requested;

                    GameInventory sourceInventory = item.parentInventory;
                    string itemKey = StableIdentityResolver.ResolveItem(item);
                    string sourceKey = StableIdentityResolver.ResolveInventoryOnly(sourceInventory);
                    FlightCapture capture = TransferAnimation.Capture(item);

                    int leftover = GraphUtils.TryAcceptAll(destination, item);
                    int moved = requested - leftover;
                    if (moved <= 0)
                    {
                        continue;
                    }
                    totalMoved += moved;

                    try
                    {
                        item.Validate();
                    }
                    catch (Exception e)
                    {
                        ModEntry.LogError("validating a batch-moved item failed: " + e.Message);
                    }

                    TransferAnimation.Play(capture, item, null, false);
                    PredictionLedger.Record(traceId, itemKey, item, sourceInventory, destination,
                        sourceKey, predictedKey, requested, moved);
                }
            }
            finally
            {
                AutomationTransferGuard.End();
            }

            ContainerTransfer.Refresh(destinationWindow, sourceWindow);
            ModEntry.LogInfo("band double-click moved " + totalMoved + " unit(s) into '"
                + DesktopTransfer.DescribeArea(destination) + "' as one batch.");
            TransferDiagnostics.TransferResult(traceId, predictedKey, predictedKey,
                totalRequested, totalMoved, totalMoved > 0 ? "success" : "failed",
                totalMoved > 0 && totalMoved < totalRequested, totalMoved <= 0,
                totalMoved > 0 ? DesktopTransfer.DescribeArea(destination) : "unknown", "unavailable");
            return totalMoved;
        }

        /// <summary>
        /// Collects all eligible candidates, ranks them, then tries them in order. Only a complete
        /// failure (nothing moved) advances to the next candidate; a partial move stops immediately.
        /// Behaviour is unchanged from the pre-diagnostic version; logging was added around it.
        /// </summary>
        private static TransferOutcome TryScoredTargets(GameItem item, PixelWindow sourceWindow,
            FlightCapture capture, string traceId)
        {
            List<TargetCandidate> candidates = WindowActivityManager.GetEligibleTargets(item, sourceWindow, traceId);
            if (candidates == null || candidates.Count == 0)
            {
                TransferDiagnostics.NoCandidate(traceId);
                return TransferOutcome.NotHandled;
            }

            List<TargetCandidate> ranked = TargetScoringService.ScoreAndRank(item, candidates, traceId);

            for (int i = 0; i < ranked.Count; i++)
            {
                TargetCandidate candidate = ranked[i];
                TransferAttemptResult attempt = TryMoveInto(item, sourceWindow, candidate, capture);

                TransferDiagnostics.TransferResult(
                    traceId,
                    candidate.TargetKey,
                    candidate.TargetKey,
                    attempt.RequestedCount,
                    attempt.MovedCount,
                    outcomeName(attempt.Outcome),
                    attempt.IsPartial,
                    attempt.Outcome == AttemptOutcome.NothingMoved,
                    attempt.MovedCount > 0 ? DescribeFinalArea(item) : "unknown",
                    attempt.DurationMs);

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
                    GameInventory originalSource = capture != null ? capture.SourceInventory : null;
                    string itemKey = StableIdentityResolver.ResolveItem(item);
                    PredictionLedger.Record(
                        traceId,
                        itemKey,
                        item,
                        originalSource,
                        destination,
                        StableIdentityResolver.ResolveInventoryOnly(originalSource),
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

            ModEntry.LogInfo("all scored candidates rejected " + DescribeItem(item) + "; using fallback.");
            return TransferOutcome.NotHandled;
        }

        /// <summary>Native move into one candidate, guarded, with timing. No behaviour change.</summary>
        private static TransferAttemptResult TryMoveInto(GameItem item, PixelWindow sourceWindow,
            TargetCandidate candidate, FlightCapture capture)
        {
            TransferAttemptResult result = new TransferAttemptResult();
            result.Candidate = candidate;
            result.RequestedCount = item != null ? item.unitCount : 0;

            if (candidate == null || candidate.Slot == null || candidate.Window == null)
            {
                result.Outcome = AttemptOutcome.NothingMoved;
                return result;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int moved = 0;
            AutomationTransferGuard.Begin();
            try
            {
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
            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.Transfer, sw.Elapsed.TotalMilliseconds);

            result.MovedCount = moved;
            if (moved <= 0)
            {
                result.Outcome = AttemptOutcome.NothingMoved;
                return result;
            }

            TransferAnimation.Play(capture, item, candidate.Slot, ShouldHideSource(capture, item, moved, result.RequestedCount));
            result.Outcome = moved < result.RequestedCount
                ? AttemptOutcome.PartialSuccess
                : AttemptOutcome.FullSuccess;
            return result;
        }

        private static void Branch(string traceId, string branch)
        {
            TransferDiagnostics.Branch(traceId, branch);
        }

        private static void ReportSpecial(string traceId, string branch, GameItem item,
            string finalArea, TransferOutcome outcome)
        {
            TransferDiagnostics.TransferResult(
                traceId,
                "n/a_special_branch",
                "n/a_special_branch",
                item == null ? 0 : item.unitCount,
                0,
                outcome.ToString(),
                false,
                false,
                finalArea,
                "unavailable");
        }

        private static string outcomeName(AttemptOutcome outcome)
        {
            switch (outcome)
            {
                case AttemptOutcome.FullSuccess: return "success";
                case AttemptOutcome.PartialSuccess: return "partial";
                default: return "failed";
            }
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
