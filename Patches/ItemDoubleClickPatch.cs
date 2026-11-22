using System;
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
                    return true;
                }
                if (newItem.identifier == "intel_sec"
                    || newItem.IsTag("DOCUMENT")
                    || newItem.IsGameItemType("DOCUMENT"))
                {
                    return true;
                }
                if (newItem.identifier == BlackLampHelper.BLACK_LAMP_ID)
                {
                    return true;
                }
                EmporiumEntry emporium = EmporiumEntry.Instance;
                if (emporium != null && ReferenceEquals(newItem.parentInventory, emporium.trashcanInvElement))
                {
                    return true;
                }

                TransferSettings settings = ModEntry.Settings;
                if (settings != null && !settings.Enabled)
                {
                    return true;
                }
                if (ItemPinHelper.BlocksClicks)
                {
                    return true;
                }
                if (GeneralHelper.IsDisplayOnly(newItem))
                {
                    return true;
                }
                if (newItem.IsTag(AppraisalHelper.APPRAISAL_ITEM)
                    || newItem.IsTag(InspectableHelper.INSPECTABLE_TAG))
                {
                    return true;
                }
                if (newItem.examineWindow != null)
                {
                    return true;
                }

                PixelWindow contentWindow = newItem.contentWindow;
                bool machinery = MachineSlotResolver.IsMachineItem(newItem);
                if (machinery && settings != null && settings.PreserveMachineOpen)
                {
                    return true;
                }
                if (!machinery && contentWindow != null && settings != null && settings.PreserveContainerOpen)
                {
                    return true;
                }

                if (!machinery && contentWindow == null
                    && newItem.MayToggleSlotItem() && newItem.CanToggleSlotItem())
                {
                    return true;
                }

                if (newItem.parentInventory == null)
                {
                    return true;
                }
                if (!newItem.MayRemove())
                {
                    return true;
                }
                if (newItem.MaxNumRemove() < 1)
                {
                    return true;
                }

                PixelWindow sourceWindow = WindowActivityManager.FindSourceWindow(newItem);
                if (sourceWindow == null)
                {
                    return true;
                }

                ItemTransferManager.Transfer(newItem, sourceWindow);
                return false;
            }
            catch (Exception e)
            {
                ModEntry.LogError("double-click handler failed, falling back to the base game action: " + e);
                return true;
            }
        }
    }
}
