using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using SmartQuickTransfer.Core;
using SmartQuickTransfer.Transfer;

namespace SmartQuickTransfer.Patches
{
    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
    internal static class ItemDoubleClickPatch
    {
        // The band-selection handler keeps its picked items in a private field with no public
        // accessor. Resolve it once; if a game update renames it, batch transfer degrades
        // gracefully to the original single-item behaviour instead of breaking the mod.
        private static readonly FieldInfo SelectedItemsField = ResolveSelectedItemsField();

        private static FieldInfo ResolveSelectedItemsField()
        {
            try
            {
                return typeof(ItemMultiSelectHandler).GetField(
                    "selectedItems", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not resolve the band-selection field: " + e.Message);
                return null;
            }
        }

        private static bool Prefix(GameItem __0)
        {
            GameItem newItem = __0;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (newItem == null)
                {
                    return true;
                }

                TransferSettings settings = ModEntry.Settings;
                if (settings != null && !settings.Enabled)
                {
                    Original("mod_disabled", newItem);
                    return true;
                }
                if (ItemPinHelper.BlocksClicks)
                {
                    Original("pinned", newItem);
                    return true;
                }

                // Special items keep the base-game double-click interaction, even when part of a
                // band selection. Decide on the clicked item first so those branches stay intact.
                string reason;
                PixelWindow primaryWindow;
                if (!CanSmartTransfer(newItem, settings, out reason, out primaryWindow))
                {
                    Original(reason, newItem);
                    return true;
                }

                // A band selection is one batch: a single shared destination and one guarded
                // commit of every picked item, mirroring the base game's group placement. It is
                // NOT N independent smart transfers. Items that cannot be smart-transferred are
                // left untouched (and reported) so their base-game interaction is preserved.
                List<GameItem> batch = CollectSelection();
                if (batch != null && batch.Count > 1)
                {
                    List<GameItem> eligible = new List<GameItem>();
                    for (int i = 0; i < batch.Count; i++)
                    {
                        GameItem item = batch[i];
                        string itemReason;
                        PixelWindow itemWindow;
                        if (!CanSmartTransfer(item, settings, out itemReason, out itemWindow))
                        {
                            Original("batch_skip:" + itemReason, item);
                            continue;
                        }
                        eligible.Add(item);
                    }

                    if (eligible.Count > 0)
                    {
                        string batchTrace = TransferDiagnostics.BeginTrace(
                            newItem.identifier,
                            newItem.unitCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            WindowActivityManager.Describe(primaryWindow),
                            StableIdentityResolver.ResolveInventoryOnly(newItem.parentInventory));
                        int moved = 0;
                        try
                        {
                            moved = ItemTransferManager.TransferBatch(eligible, primaryWindow, batchTrace);
                            sw.Stop();
                            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.DoubleClick,
                                sw.Elapsed.TotalMilliseconds);
                            TransferDiagnostics.EndTrace(
                                sw.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        catch (Exception e)
                        {
                            TransferDiagnostics.ErrorThrottled("batch_double_click", "batch failed: " + e.Message);
                            throw;
                        }
                        if (moved > 0)
                        {
                            ClearSelection();
                            return false;
                        }
                    }
                    // Nothing could be moved as a batch; fall through to the single-item path.
                }

                TransferOne(newItem, primaryWindow);
                sw.Stop();
                TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.DoubleClick,
                    sw.Elapsed.TotalMilliseconds);
                return false;
            }
            catch (Exception e)
            {
                ModEntry.LogError("double-click handler failed, falling back to the base game action: " + e);
                return true;
            }
        }

        /// <summary>Runs the shared smart-transfer path for exactly one item.</summary>
        private static void TransferOne(GameItem item, PixelWindow sourceWindow)
        {
            string traceId = TransferDiagnostics.BeginTrace(
                item.identifier,
                item.unitCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                WindowActivityManager.Describe(sourceWindow),
                StableIdentityResolver.ResolveInventoryOnly(item.parentInventory));
            try
            {
                ItemTransferManager.Transfer(item, sourceWindow, traceId);
                TransferDiagnostics.EndTrace();
            }
            catch (Exception e)
            {
                TransferDiagnostics.ErrorThrottled("double_click", "transfer failed: " + e.Message);
                throw;
            }
        }

        /// <summary>
        /// All eligibility checks that decide whether a double-click is handled by the mod.
        /// Kept identical to the original single-item path so special interactions are unchanged.
        /// </summary>
        private static bool CanSmartTransfer(GameItem item, TransferSettings settings,
            out string reason, out PixelWindow sourceWindow)
        {
            reason = "unknown";
            sourceWindow = null;
            if (item == null)
            {
                reason = "null_item";
                return false;
            }
            if (item.IsTag(LotteryHelper.LOTTERY_TICKET_TAG)
                || item.IsTag(ScratcherHelper.SCRATCHER_TAG)
                || item.IsTag(LotteryHelper.LOTTERY_PAPER_TAG)
                || item.IsGameItemType("LOTTERY")
                || item.identifier == "scratcher_stack")
            {
                reason = "lottery_scratcher";
                return false;
            }
            if (item.identifier == "intel_sec"
                || item.IsTag("DOCUMENT")
                || item.IsGameItemType("DOCUMENT"))
            {
                reason = "document";
                return false;
            }
            if (item.identifier == BlackLampHelper.BLACK_LAMP_ID)
            {
                reason = "black_lamp";
                return false;
            }
            // Fish feed double-click splits the stack in the base game; keep that behaviour.
            if (IndustrialFishFeedHelper.IsFeed(item) || GutterflowPelletHelper.IsPellet(item))
            {
                reason = "fish_feed_split";
                return false;
            }
            EmporiumEntry emporium = EmporiumEntry.Instance;
            if (emporium != null && ReferenceEquals(item.parentInventory, emporium.trashcanInvElement))
            {
                reason = "trashcan";
                return false;
            }
            if (GeneralHelper.IsDisplayOnly(item))
            {
                reason = "display_only";
                return false;
            }
            if (item.IsTag(AppraisalHelper.APPRAISAL_ITEM)
                || item.IsTag(InspectableHelper.INSPECTABLE_TAG))
            {
                reason = "appraisal_inspect";
                return false;
            }
            if (item.examineWindow != null)
            {
                reason = "examine_window";
                return false;
            }

            PixelWindow contentWindow = item.contentWindow;
            bool machinery = MachineSlotResolver.IsMachineItem(item);
            if (machinery && settings != null && settings.PreserveMachineOpen)
            {
                reason = "machine_open";
                return false;
            }
            if (!machinery && contentWindow != null && settings != null && settings.PreserveContainerOpen)
            {
                reason = "container_open";
                return false;
            }

            if (!machinery && contentWindow == null
                && item.MayToggleSlotItem() && item.CanToggleSlotItem())
            {
                reason = "toggle_item";
                return false;
            }

            if (item.parentInventory == null)
            {
                reason = "no_inventory";
                return false;
            }
            if (!item.MayRemove())
            {
                reason = "not_removable";
                return false;
            }
            if (item.MaxNumRemove() < 1)
            {
                reason = "max_remove_zero";
                return false;
            }

            sourceWindow = WindowActivityManager.FindSourceWindow(item);
            if (sourceWindow == null)
            {
                reason = "no_source_window";
                return false;
            }

            reason = "ok";
            return true;
        }

        /// <summary>
        /// Snapshot of the current band selection. A snapshot is taken because the first transfer
        /// can remove/merge an item and the game prunes its own list while we iterate.
        /// </summary>
        private static List<GameItem> CollectSelection()
        {
            try
            {
                ItemMultiSelectHandler handler = ItemMultiSelectHandler.current;
                if (handler == null || !handler.HasSelection || SelectedItemsField == null)
                {
                    return null;
                }
                object raw = SelectedItemsField.GetValue(handler);
                IEnumerable entries = raw as IEnumerable;
                if (entries == null)
                {
                    return null;
                }
                List<GameItem> result = new List<GameItem>();
                foreach (object entry in entries)
                {
                    GameItem item = entry as GameItem;
                    if (item == null)
                    {
                        continue;
                    }
                    bool duplicate = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (ReferenceEquals(result[i], item))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        result.Add(item);
                    }
                }
                return result.Count > 0 ? result : null;
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not read the band selection: " + e.Message);
                return null;
            }
        }

        /// <summary>Clear highlights and selection state once the batch has been transferred.</summary>
        private static void ClearSelection()
        {
            try
            {
                ItemMultiSelectHandler handler = ItemMultiSelectHandler.current;
                if (handler != null)
                {
                    handler.Cancel();
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not clear the band selection: " + e.Message);
            }
        }

        /// <summary>An original-interaction branch: report it without touching item state.</summary>
        private static void Original(string reason, GameItem item)
        {
            if (!TransferDiagnostics.DetailedEnabled)
            {
                return;
            }
            try
            {
                string traceId = TransferDiagnostics.BeginTrace(
                    item == null ? "" : item.identifier,
                    item == null ? "0" : item.unitCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "unknown", "unknown");
                TransferDiagnostics.Branch(traceId, "original_interaction:" + reason);
                TransferDiagnostics.EndTrace();
            }
            catch (Exception)
            {
                // logging must never change the original-interaction path
            }
        }
    }
}
