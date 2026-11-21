using System;
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
                    PixelWindow target = WindowActivityManager.FindActiveTarget(sourceWindow);
                    if (target != null)
                    {
                        return TransferToWindow(item, sourceWindow, target, capture);
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

        private static TransferOutcome TransferToWindow(GameItem item, PixelWindow sourceWindow, PixelWindow target, FlightCapture capture)
        {
            SlotMarker slot = MachineSlotResolver.FindValidSlot(target, item);
            if (slot == null)
            {
                ModEntry.LogInfo("target '" + WindowActivityManager.Describe(target)
                    + "' has no slot that accepts " + DescribeItem(item) + "; item kept in place.");
                return TransferOutcome.RejectedByTarget;
            }

            int requested = item.unitCount;
            int moved = ContainerTransfer.MoveInto(target, item, sourceWindow);
            if (moved <= 0)
            {
                ModEntry.LogError("native transfer of " + DescribeItem(item) + " into '"
                    + WindowActivityManager.Describe(target) + "' moved nothing; item kept in place.");
                return TransferOutcome.Failed;
            }

            TransferAnimation.Play(capture, item, slot, ShouldHideSource(capture, item, moved, requested));

            ModEntry.LogInfo("moved " + moved + " x " + DescribeItem(item) + " from '"
                + WindowActivityManager.Describe(sourceWindow) + "' into '"
                + WindowActivityManager.Describe(target) + "' (" + MachineSlotResolver.Describe(target)
                + "); item now in " + DescribeFinalArea(item) + ".");
            return TransferOutcome.MovedToWindow;
        }

        private static TransferOutcome TransferToCustomerDesk(GameItem item, PixelWindow sourceWindow, GameInventory desk, FlightCapture capture)
        {
            SlotMarker slot = FindSlot(desk, item);
            int requested = item.unitCount;
            int moved = DesktopTransfer.MoveInto(desk, item, sourceWindow);
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
            int moved = DesktopTransfer.MoveInto(destination, item, sourceWindow);
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
