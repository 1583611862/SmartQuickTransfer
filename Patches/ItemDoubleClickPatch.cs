using System;
using System.Diagnostics;
using HarmonyLib;
using SmartQuickTransfer.Core;
using SmartQuickTransfer.Transfer;

namespace SmartQuickTransfer.Patches
{
    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
    internal static class ItemDoubleClickPatch
    {
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
                if (newItem.IsTag(LotteryHelper.LOTTERY_TICKET_TAG)
                    || newItem.IsTag(ScratcherHelper.SCRATCHER_TAG)
                    || newItem.IsTag(LotteryHelper.LOTTERY_PAPER_TAG)
                    || newItem.IsGameItemType("LOTTERY")
                    || newItem.identifier == "scratcher_stack")
                {
                    Original("lottery_scratcher", newItem);
                    return true;
                }
                if (newItem.identifier == "intel_sec"
                    || newItem.IsTag("DOCUMENT")
                    || newItem.IsGameItemType("DOCUMENT"))
                {
                    Original("document", newItem);
                    return true;
                }
                if (newItem.identifier == BlackLampHelper.BLACK_LAMP_ID)
                {
                    Original("black_lamp", newItem);
                    return true;
                }
                // 鱼饲料（工业鱼饲料 / 沟流颗粒）双击原版行为是拆分，交回原版逻辑，不执行快捷转移。
                if (IndustrialFishFeedHelper.IsFeed(newItem) || GutterflowPelletHelper.IsPellet(newItem))
                {
                    Original("fish_feed_split", newItem);
                    return true;
                }
                EmporiumEntry emporium = EmporiumEntry.Instance;
                if (emporium != null && ReferenceEquals(newItem.parentInventory, emporium.trashcanInvElement))
                {
                    Original("trashcan", newItem);
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
                if (GeneralHelper.IsDisplayOnly(newItem))
                {
                    Original("display_only", newItem);
                    return true;
                }
                if (newItem.IsTag(AppraisalHelper.APPRAISAL_ITEM)
                    || newItem.IsTag(InspectableHelper.INSPECTABLE_TAG))
                {
                    Original("appraisal_inspect", newItem);
                    return true;
                }
                if (newItem.examineWindow != null)
                {
                    Original("examine_window", newItem);
                    return true;
                }

                PixelWindow contentWindow = newItem.contentWindow;
                bool machinery = MachineSlotResolver.IsMachineItem(newItem);
                if (machinery && settings != null && settings.PreserveMachineOpen)
                {
                    Original("machine_open", newItem);
                    return true;
                }
                if (!machinery && contentWindow != null && settings != null && settings.PreserveContainerOpen)
                {
                    Original("container_open", newItem);
                    return true;
                }

                if (!machinery && contentWindow == null
                    && newItem.MayToggleSlotItem() && newItem.CanToggleSlotItem())
                {
                    Original("toggle_item", newItem);
                    return true;
                }

                if (newItem.parentInventory == null)
                {
                    Original("no_inventory", newItem);
                    return true;
                }
                if (!newItem.MayRemove())
                {
                    Original("not_removable", newItem);
                    return true;
                }
                if (newItem.MaxNumRemove() < 1)
                {
                    Original("max_remove_zero", newItem);
                    return true;
                }

                PixelWindow sourceWindow = WindowActivityManager.FindSourceWindow(newItem);
                if (sourceWindow == null)
                {
                    Original("no_source_window", newItem);
                    return true;
                }

                // Reaching here means a normal smart transfer will be attempted.
                string traceId = TransferDiagnostics.BeginTrace(
                    newItem.identifier,
                    newItem.unitCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    WindowActivityManager.Describe(sourceWindow),
                    StableIdentityResolver.ResolveInventoryOnly(newItem.parentInventory));
                try
                {
                    ItemTransferManager.Transfer(newItem, sourceWindow, traceId);
                    sw.Stop();
                    TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.DoubleClick, sw.Elapsed.TotalMilliseconds);
                    TransferDiagnostics.EndTrace(sw.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    TransferDiagnostics.ErrorThrottled("double_click", "transfer failed: " + e.Message);
                    throw;
                }
                return false;
            }
            catch (Exception e)
            {
                ModEntry.LogError("double-click handler failed, falling back to the base game action: " + e);
                return true;
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
