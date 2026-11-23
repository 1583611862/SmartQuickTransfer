using System;
using System.Collections.Generic;
using SmartQuickTransfer.Transfer;

namespace SmartQuickTransfer.Core
{
    internal static class WindowActivityManager
    {
        internal static PixelWindow FindSourceWindow(GameItem item)
        {
            if (item == null)
            {
                return null;
            }

            // The UI tree is the most accurate answer: a backpack lying inside a machine opens its own
            // window, and the item is displayed in that window, not in the machine behind it.
            PixelElement element = item as PixelElement;
            if (element != null)
            {
                try
                {
                    if (element.handler != null)
                    {
                        PixelWindow fromUi = element.handler.FindParentWindow();
                        if (fromUi != null)
                        {
                            return fromUi;
                        }
                    }
                }
                catch (Exception e)
                {
                    ModEntry.LogError("source window lookup through the UI tree failed: " + e.Message);
                }
            }

            GameInventory inventory = item.parentInventory;
            if (inventory != null)
            {
                try
                {
                    PixelWindow fromGraph = GraphUtils.FindParentType<PixelWindow>(inventory);
                    if (fromGraph != null)
                    {
                        return fromGraph;
                    }
                }
                catch (Exception e)
                {
                    ModEntry.LogError("source window lookup through the item graph failed: " + e.Message);
                }
            }

            return null;
        }

        internal static PixelWindow FindActiveTarget(PixelWindow sourceWindow)
        {
            WindowsHandler handler = WindowsHandler.current;
            if (handler == null)
            {
                return null;
            }

            List<PixelWindow> windows = handler.visibleWindows;
            if (windows == null || windows.Count == 0)
            {
                return null;
            }

            bool includeStorePanels = ModEntry.Settings != null && ModEntry.Settings.IncludeStorePanels;
            PixelWindow best = null;
            long bestStamp = long.MinValue;

            for (int i = 0; i < windows.Count; i++)
            {
                PixelWindow candidate = windows[i];
                try
                {
                    if (candidate == null || ReferenceEquals(candidate, sourceWindow))
                    {
                        continue;
                    }
                    if (!IsEligibleTarget(candidate, includeStorePanels))
                    {
                        continue;
                    }

                    long stamp = candidate.focusStamp;
                    if (best == null || stamp >= bestStamp)
                    {
                        best = candidate;
                        bestStamp = stamp;
                    }
                }
                catch (Exception e)
                {
                    ModEntry.LogError("window scan skipped a window: " + e.Message);
                }
            }

            return best;
        }

        /// <summary>
        /// Collects every eligible, slot-compatible destination for the item. This is the scoring
        /// input that replaces the old single "most recent window" choice. Slot compatibility is
        /// checked with the game's read-only TryFindOneValidInventorySlot - never by attempting a
        /// real transfer, so collecting candidates has no side effects on any inventory.
        /// </summary>
        internal static List<TargetCandidate> GetEligibleTargets(GameItem item, PixelWindow sourceWindow)
        {
            List<TargetCandidate> result = new List<TargetCandidate>();
            if (item == null)
            {
                return result;
            }

            WindowsHandler handler = WindowsHandler.current;
            if (handler == null)
            {
                return result;
            }
            List<PixelWindow> windows = handler.visibleWindows;
            if (windows == null || windows.Count == 0)
            {
                return result;
            }

            bool includeStorePanels = ModEntry.Settings != null && ModEntry.Settings.IncludeStorePanels;
            int order = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                PixelWindow candidate = windows[i];
                try
                {
                    if (candidate == null || ReferenceEquals(candidate, sourceWindow))
                    {
                        continue;
                    }
                    if (!IsEligibleTarget(candidate, includeStorePanels))
                    {
                        continue;
                    }

                    // Read-only native slot lookup; ResolveSlot only reads/validates, never moves.
                    SlotMarker slot = MachineSlotResolver.FindValidSlot(candidate, item);
                    if (slot == null)
                    {
                        continue;
                    }

                    GameInventory inventory = slot.inventory;
                    TargetCandidate target = new TargetCandidate();
                    target.Window = candidate;
                    target.Slot = slot;
                    target.Inventory = inventory;
                    target.FocusStamp = candidate.focusStamp;
                    target.OriginalOrder = order++;
                    // Prefer the concrete inventory the slot belongs to: a window can expose
                    // several inventories with different purposes.
                    target.TargetKey = StableIdentityResolver.ResolveTarget(candidate, inventory);
                    result.Add(target);
                }
                catch (Exception e)
                {
                    ModEntry.LogError("candidate scan skipped a window: " + e.Message);
                }
            }

            return result;
        }

        /// <summary>
        /// Public wrapper used by StableIdentityResolver / affinity rules. Returns a stable area
        /// key (仓库/桌面/...) for the fixed shop panels, or null for other windows.
        /// </summary>
        internal static string DescribeShopArea(PixelWindow window)
        {
            return DescribeArea(window);
        }

        internal static bool IsEligibleTarget(PixelWindow window, bool includeStorePanels)
        {
            if (window == null)
            {
                return false;
            }
            if (!window.IsVisible())
            {
                return false;
            }

            if (!window.canMove)
            {
                if (!includeStorePanels)
                {
                    return false;
                }
                if (!IsStorePanel(window))
                {
                    return false;
                }
            }

            return HasItemStorage(window);
        }

        internal static bool IsStorePanel(PixelWindow window)
        {
            if (window == null)
            {
                return false;
            }

            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
            {
                return false;
            }

            return window == entry.invWindow
                || window == entry.backInvWindow
                || window == entry.frontInvWindow
                || window == entry.showcaseWindow
                || window == entry.afterhourWindow;
        }

        internal static bool HasItemStorage(PixelWindow window)
        {
            if (window == null)
            {
                return false;
            }

            try
            {
                List<GameInventory> inventories = GraphUtils.FindChildrenType<GameInventory>(window);
                return inventories != null && inventories.Count > 0;
            }
            catch (Exception e)
            {
                ModEntry.LogError("storage scan failed for a window: " + e.Message);
                return false;
            }
        }

        internal static string Describe(PixelWindow window)
        {
            if (window == null)
            {
                return "<none>";
            }

            try
            {
                string area = DescribeArea(window);
                if (area != null)
                {
                    return area;
                }

                if (window.parentItems != null && window.parentItems.Count > 0 && window.parentItems[0] != null)
                {
                    GameItem owner = window.parentItems[0];
                    string title = string.IsNullOrEmpty(window.titleString) ? owner.identifier : window.titleString;
                    return title + " [" + owner.identifier + "]";
                }
                if (!string.IsNullOrEmpty(window.titleString))
                {
                    return window.titleString;
                }
            }
            catch (Exception)
            {
                // The window may be in the middle of being destroyed; the type name is enough.
            }

            return window.canMove ? "movable window" : "store panel";
        }

        /// <summary>Names the fixed shop panels the way the game UI shows them, or null.</summary>
        private static string DescribeArea(PixelWindow window)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
            {
                return null;
            }
            if (ReferenceEquals(window, entry.invWindow))
            {
                return TransferSettings.TargetStorage;
            }
            if (ReferenceEquals(window, entry.backInvWindow))
            {
                return TransferSettings.TargetDesk;
            }
            if (ReferenceEquals(window, entry.showcaseWindow))
            {
                return TransferSettings.TargetShowcase;
            }
            if (ReferenceEquals(window, entry.frontInvWindow))
            {
                return TransferSettings.TargetFrontCounter;
            }
            if (ReferenceEquals(window, entry.afterhourWindow))
            {
                return TransferSettings.TargetNightGround;
            }
            return null;
        }
    }
}
