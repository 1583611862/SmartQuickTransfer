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
                // Decide by item location, not by whichever window happens to be focused.
                // Ground loot must never fall through to normal scoring, which can pick the
                // warehouse: on the dumping ground it may only go into the backpack or hands.
                bool fromGround = DesktopTransfer.IsOnNightGround(item);
                if (fromGround)
                {
                    Branch(traceId, "night_ground");
                    // Openable items (first-aid boxes, medicine containers, ...) would be opened by
                    // the vanilla double-click. On the dumping ground they are put away like any
                    // other loot - into the visible backpack grid, never into the hands.
                    bool openable = item.contentWindow != null && !MachineSlotResolver.IsMachineItem(item);
                    TransferOutcome outcome = TransferToAfterhourPocket(item, sourceWindow, capture, !openable);
                    ReportSpecial(traceId, "night_ground", item, DescribeFinalArea(item), outcome,
                        outcome == TransferOutcome.RejectedByTarget || outcome == TransferOutcome.Failed ? 0 : item.unitCount);
                    return outcome;
                }

                // Dossier/archive-box display signs belong in the display case, not in storage.
                if (DesktopTransfer.IsShowcaseSpecial(item)
                    && DesktopTransfer.IsFromDossier(item, sourceWindow))
                {
                    GameInventory showcase = DesktopTransfer.ResolveShowcase(item);
                    if (showcase != null)
                    {
                        Branch(traceId, "showcase_special");
                        TransferOutcome outcome = TransferToShowcase(item, sourceWindow, showcase, capture);
                        ReportSpecial(traceId, "showcase_special", item, DescribeFinalArea(item), outcome,
                            outcome == TransferOutcome.Failed ? 0 : item.unitCount);
                        return outcome;
                    }
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
                        ReportSpecial(traceId, "customer_demand", item, DesktopTransfer.DescribeArea(desk), outcome,
                            outcome == TransferOutcome.Failed ? 0 : item.unitCount);
                        return outcome;
                    }
                    ModEntry.LogInfo("customer wants " + DescribeItem(item)
                        + " but the desk has no valid slot; using the normal route.");
                }

                // A machinery-style window is either a real processing machine or a storage
                // container (smuggler bays, storage bays, toolboxes ... all use Machinery style).
                // Fixed machinery panels (night ground, vending slots) are storage areas, not
                // machines, and keep the normal routing.
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
                    Branch(traceId, "normal_fallback");
                    TransferOutcome fallbackOutcome = TransferToFallback(item, sourceWindow, capture, false, traceId);
                    ReportSpecial(traceId, "fallback", item, DescribeFinalArea(item), fallbackOutcome,
                        fallbackOutcome == TransferOutcome.MovedToFallback ? item.unitCount : 0);
                    return fallbackOutcome;
                }

                // Machine source: only a player-intended, non-fixed-panel, slot-compatible target
                // may receive the item. Such a target is a container by construction: processing
                // machines are excluded by IsProcessingMachineWindow. With no such target we keep
                // the original machine-return behaviour (MachineOutputArea).
                List<TargetCandidate> intended = CollectMachineIntendedTargets(item, sourceWindow, traceId);
                int intendedCount = intended == null ? 0 : intended.Count;
                bool usedScored = false;
                if (intendedCount > 0)
                {
                    Branch(traceId, "machine_scored");
                    TransferOutcome scored = TryMachineTargets(item, sourceWindow, intended, capture, traceId);
                    if (scored != TransferOutcome.NotHandled)
                    {
                        usedScored = true;
                        LogMachineDecision(traceId, item, sourceWindow, intendedCount, true);
                        return scored;
                    }
                }
                LogMachineDecision(traceId, item, sourceWindow, intendedCount, usedScored);

                Branch(traceId, "machine_return");
                TransferOutcome machineFallback = TransferToFallback(item, sourceWindow, capture, true, traceId);
                ReportSpecial(traceId, "machine_return", item, DescribeFinalArea(item), machineFallback,
                    machineFallback == TransferOutcome.MovedToFallback ? item.unitCount : 0);
                return machineFallback;
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

            bool fromGround = DesktopTransfer.IsOnNightGround(representative);

            if (fromGround)
            {
                // One shared destination for the whole batch: the visible backpack grid first,
                // then the hands (except for openable items). Never the warehouse - ground loot
                // must stay on the ground when neither the backpack nor the hands can take it.
                bool openable = representative.contentWindow != null
                    && !MachineSlotResolver.IsMachineItem(representative);
                destination = DesktopTransfer.ResolveAfterhourBackpack(representative)
                    ?? (openable ? (GameInventory)null : DesktopTransfer.ResolveAfterhourPocket(representative));
                destinationWindow = entry != null ? entry.afterhourWindow : null;
            }
            else if (DesktopTransfer.IsShowcaseSpecial(representative)
                && DesktopTransfer.IsFromDossier(representative, sourceWindow))
            {
                destination = DesktopTransfer.ResolveShowcase(representative);
                destinationWindow = entry != null ? entry.showcaseWindow : null;
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

                if (destination == null)
                {
                    // Same decision as the single-item path: normal sources use every eligible
                    // candidate; machine sources only use player-intended container targets.
                    List<TargetCandidate> candidates = fromMachine
                        ? CollectMachineIntendedTargets(representative, sourceWindow, traceId)
                        : WindowActivityManager.GetEligibleTargets(representative, sourceWindow, traceId);
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
                        predictedKey = StableIdentityResolver.ResolveTarget(destinationWindow, destination);
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
        /// Machine-source target filter. A machine/tool item may be smart-transferred to another
        /// container only when that container was explicitly touched by the player this session
        /// (intent stamp from the frozen snapshot), is visible and slot-compatible, is neither a
        /// fixed shop panel nor another window instance of the source inventory, and is not a
        /// processing machine. With no such target the caller keeps the original machine-return
        /// behaviour. Slot checks are the native read-only lookup and have no side effects.
        /// </summary>
        private static List<TargetCandidate> CollectMachineIntendedTargets(GameItem item,
            PixelWindow sourceWindow, string traceId)
        {
            List<TargetCandidate> all = WindowActivityManager.GetEligibleTargets(item, sourceWindow, traceId);
            List<TargetCandidate> result = new List<TargetCandidate>();
            if (all == null || all.Count == 0 || item == null)
            {
                return result;
            }

            GameInventory sourceInventory = item.parentInventory;
            for (int i = 0; i < all.Count; i++)
            {
                TargetCandidate c = all[i];
                if (c == null || c.Window == null || c.Inventory == null)
                {
                    continue;
                }
                if (WindowActivityManager.IsStorePanel(c.Window))
                {
                    TransferDiagnostics.CandidateRejected(traceId, c.TargetKey, "machine_fixed_panel");
                    continue;
                }
                if (sourceInventory != null && ReferenceEquals(c.Inventory, sourceInventory))
                {
                    TransferDiagnostics.CandidateRejected(traceId, c.TargetKey, "machine_source_inventory");
                    continue;
                }
                if (MachineSlotResolver.IsProcessingMachineWindow(c.Window))
                {
                    TransferDiagnostics.CandidateRejected(traceId, c.TargetKey, "machine_processing_target");
                    continue;
                }
                if (c.IntentStamp <= 0L)
                {
                    // Only a window the player actively opened/focused/dropped into qualifies.
                    // The stamp comes from the snapshot frozen at double-click start, so the press
                    // on the source window cannot turn itself into a target intent.
                    TransferDiagnostics.CandidateRejected(traceId, c.TargetKey, "machine_no_intent");
                    continue;
                }
                result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Ranks player-intended machine targets with the shared scorer and moves into them in
        /// order, reusing the exact same attempt/prediction logic as the normal scored path.
        /// </summary>
        private static TransferOutcome TryMachineTargets(GameItem item, PixelWindow sourceWindow,
            List<TargetCandidate> intended, FlightCapture capture, string traceId)
        {
            List<TargetCandidate> ranked = TargetScoringService.ScoreAndRank(item, intended, traceId);
            for (int i = 0; i < ranked.Count; i++)
            {
                TargetCandidate candidate = ranked[i];
                TransferAttemptResult attempt = TryMoveInto(item, sourceWindow, candidate, capture);
                TransferDiagnostics.TransferResult(
                    traceId, candidate.TargetKey, candidate.TargetKey,
                    attempt.RequestedCount, attempt.MovedCount, outcomeName(attempt.Outcome),
                    attempt.IsPartial, attempt.Outcome == AttemptOutcome.NothingMoved,
                    attempt.MovedCount > 0 ? DescribeFinalArea(item) : "unknown", attempt.DurationMs);

                if (attempt.Outcome == AttemptOutcome.NothingMoved)
                {
                    ModEntry.LogInfo("machine target '" + candidate.Description
                        + "' accepted no units; trying the next candidate.");
                    continue;
                }

                GameInventory destination = candidate.Inventory;
                if (destination != null)
                {
                    GameInventory originalSource = capture != null ? capture.SourceInventory : null;
                    PredictionLedger.Record(
                        traceId,
                        StableIdentityResolver.ResolveItem(item),
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
                    + "' into player-selected container '" + candidate.Description + "' (score="
                    + candidate.FinalScore.ToString("0.0") + "); item now in " + DescribeFinalArea(item) + ".");
                return TransferOutcome.MovedToWindow;
            }
            return TransferOutcome.NotHandled;
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

        private static void LogMachineDecision(string traceId, GameItem item, PixelWindow sourceWindow,
            int intendedCount, bool usedScoredTarget)
        {
            string sourceKey = StableIdentityResolver.ResolveInventoryOnly(
                item == null ? null : item.parentInventory);
            long stamp = sourceWindow == null ? 0L : WindowIntentTracker.CurrentStampOf(sourceWindow);
            TransferDiagnostics.MachineDecision(traceId, WindowActivityManager.Describe(sourceWindow),
                sourceKey, stamp, intendedCount, usedScoredTarget);
        }

        /// <summary>
        /// Reports a special branch with the REAL moved count instead of the old hard-coded zero.
        /// A special route can move the item into warehouse/showcase/desk/ground just like a scored
        /// route; reporting zero produced misleading "moved=0 but item now in 仓库" traces.
        /// </summary>
        private static void ReportSpecial(string traceId, string branch, GameItem item,
            string finalArea, TransferOutcome outcome, int moved)
        {
            bool noMove = outcome == TransferOutcome.Failed
                || outcome == TransferOutcome.RejectedByTarget
                || outcome == TransferOutcome.NotHandled;
            int requested = item == null ? 0 : item.unitCount;
            TransferDiagnostics.TransferResult(
                traceId,
                branch,
                branch,
                requested,
                noMove ? 0 : moved,
                outcome.ToString(),
                !noMove && moved < requested,
                noMove,
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

        private static TransferOutcome TransferToAfterhourPocket(GameItem item, PixelWindow sourceWindow,
            FlightCapture capture, bool allowHands = true)
        {
            // The backpack slot in the night-ground window only holds the backpack item. Put loot
            // into that item's internal grid, which is the "背包" grid the player can actually see.
            GameInventory pocket = DesktopTransfer.ResolveAfterhourBackpack(item);
            if (pocket == null && allowHands)
            {
                pocket = DesktopTransfer.ResolveAfterhourPocket(item);
            }
            if (pocket == null)
            {
                // No backpack that can take it and no free hands: leave it on the ground.
                // The warehouse is never a destination for night-ground loot.
                ModEntry.LogInfo("afterhour backpack and pockets cannot accept " + DescribeItem(item)
                    + "; item kept on the ground (never returned to the warehouse).");
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

        private static TransferOutcome TransferToShowcase(GameItem item, PixelWindow sourceWindow,
            GameInventory showcase, FlightCapture capture)
        {
            SlotMarker slot = FindSlot(showcase, item);
            int requested = item.unitCount;
            int moved = GuardedMove(showcase, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("showcase transfer of " + DescribeItem(item)
                    + " moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            ModEntry.LogInfo("dossier display item " + DescribeItem(item) + "; moved " + moved + " x "
                + DescribeItem(item) + " into " + DesktopTransfer.DescribeArea(showcase)
                + "; item now in " + DescribeFinalArea(item) + ".");
            return TransferOutcome.MovedToWindow;
        }

        /// <summary>
        /// Final fallback into the fixed storage panels. Every successful automatic move registers
        /// a real prediction so a later manual drag can be matched as a correction - including the
        /// machine-return route, which previously produced no prediction at all.
        /// </summary>
        private static TransferOutcome TransferToFallback(GameItem item, PixelWindow sourceWindow,
            FlightCapture capture, bool fromMachine, string traceId)
        {
            GameInventory destination = DesktopTransfer.Resolve(item, sourceWindow, fromMachine);
            if (destination == null)
            {
                ModEntry.LogInfo("no usable storage accepts " + DescribeItem(item) + "; item kept in place.");
                return TransferOutcome.RejectedByTarget;
            }

            SlotMarker slot = FindSlot(destination, item);
            int requested = item.unitCount;
            GameInventory originalSource = capture != null ? capture.SourceInventory : item.parentInventory;
            int moved = GuardedMove(destination, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("fallback transfer of " + DescribeItem(item) + " moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            string actualKey = StableIdentityResolver.ResolveInventoryOnly(destination);
            if (!string.IsNullOrEmpty(traceId))
            {
                PredictionLedger.Record(
                    traceId,
                    StableIdentityResolver.ResolveItem(item),
                    item,
                    originalSource,
                    destination,
                    StableIdentityResolver.ResolveInventoryOnly(originalSource),
                    actualKey,
                    requested,
                    moved);
            }

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
