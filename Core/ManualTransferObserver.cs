using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SmartQuickTransfer.Core
{
    /// <summary>Describes a completed, genuine player drag from one inventory to another.</summary>
    internal sealed class ManualTransferEvent
    {
        internal string ItemKey = "";
        internal GameItem ItemReference;
        internal GameInventory SourceInventory;
        internal GameInventory DestinationInventory;
        internal string SourceKey = "";
        internal string DestinationKey = "";
        internal int MovedCount;
        internal int RequestedCount;
    }

    /// <summary>
    /// Observes the native player drag-completion method ItemMouseDragHandler.EndDrag. The mod's own
    /// transfers never call EndDrag (they use GraphUtils.TryAcceptAll directly), so genuine player
    /// drags are isolated by construction. We only READ game state - nothing here mutates an
    /// inventory. Moved count is derived from the change in the destination inventory's units of
    /// the same stable item key, which also handles stack merges correctly.
    /// </summary>
    internal static class ManualTransferObserver
    {
        private sealed class DragContext
        {
            internal GameItem Item;
            internal string ItemKey;
            internal GameInventory SourceInventory;
            internal GameInventory DestinationInventory;
            internal int RequestedCount;
            internal int DestUnitsBefore;
            internal int SourceUnitsBefore;
        }

        // EndDrag is a synchronous, single-threaded Unity callback; one active context is enough.
        private static DragContext Active;

        private static FieldInfo FieldLastInventory;
        private static FieldInfo FieldLastItem;
        private static FieldInfo FieldCurrentRemove;

        private static void EnsureFields()
        {
            if (FieldLastInventory != null)
            {
                return;
            }
            Type t = typeof(ItemMouseDragHandler);
            FieldLastInventory = AccessTools.Field(t, "lastInventory");
            FieldLastItem = AccessTools.Field(t, "lastItem");
            FieldCurrentRemove = AccessTools.Field(t, "currentRemove");
        }

        private static T Read<T>(FieldInfo field, ItemMouseDragHandler instance) where T : class
        {
            if (field == null || instance == null)
            {
                return null;
            }
            try
            {
                return field.GetValue(instance) as T;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Called from the Harmony Prefix of EndDrag, before any transfer happens.</summary>
        internal static void OnDragCompleting(ItemMouseDragHandler handler, bool cancelled)
        {
            Active = null;
            if (cancelled || handler == null || AutomationTransferGuard.IsAutomating)
            {
                return;
            }

            GameItem item = null;
            try
            {
                item = handler.GetCurrentItem();
            }
            catch (Exception)
            {
                return;
            }
            if (item == null || item.parentInventory == null)
            {
                return;
            }

            EnsureFields();
            GameInventory lastInv = Read<GameInventory>(FieldLastInventory, handler);
            GameItem lastItem = Read<GameItem>(FieldLastItem, handler);
            GameInventory dest = ResolveDestination(lastInv, lastItem);
            if (dest == null)
            {
                // MachineryTabs drop or a use-on-target action: no clear destination inventory.
                // Per the design principle we skip learning rather than guess.
                return;
            }

            string itemKey = StableIdentityResolver.ResolveItem(item);
            int requested = 0;
            if (FieldCurrentRemove != null && handler != null)
            {
                try { requested = (int)FieldCurrentRemove.GetValue(handler); }
                catch (Exception) { requested = item.unitCount; }
            }
            if (requested <= 0)
            {
                requested = item.unitCount;
            }

            DragContext ctx = new DragContext();
            ctx.Item = item;
            ctx.ItemKey = itemKey;
            ctx.SourceInventory = item.parentInventory;
            ctx.DestinationInventory = dest;
            ctx.RequestedCount = requested;
            ctx.DestUnitsBefore = CountUnits(dest, itemKey);
            ctx.SourceUnitsBefore = CountUnits(ctx.SourceInventory, itemKey);
            Active = ctx;
        }

        /// <summary>Called from the Harmony Postfix of EndDrag, after the native transfer ran.</summary>
        internal static ManualTransferEvent OnDragCompleted(ItemMouseDragHandler handler, bool cancelled)
        {
            DragContext ctx = Active;
            Active = null;
            if (ctx == null || cancelled || ctx.SourceInventory == null || ctx.DestinationInventory == null)
            {
                return null;
            }
            if (ReferenceEquals(ctx.SourceInventory, ctx.DestinationInventory))
            {
                // Repositioning inside the same inventory is not a transfer.
                return null;
            }

            int destUnitsAfter = CountUnits(ctx.DestinationInventory, ctx.ItemKey);
            int sourceUnitsAfter = CountUnits(ctx.SourceInventory, ctx.ItemKey);
            int moved = destUnitsAfter - ctx.DestUnitsBefore;
            int leftSource = ctx.SourceUnitsBefore - sourceUnitsAfter;
            if (moved <= 0 || leftSource <= 0)
            {
                return null;
            }
            // If the destination gained fewer units than the source lost, something else happened
            // (e.g. a split into a third inventory). Do not learn from an ambiguous drag.
            if (moved != leftSource)
            {
                return null;
            }

            ManualTransferEvent ev = new ManualTransferEvent();
            ev.ItemKey = ctx.ItemKey;
            ev.ItemReference = ctx.Item;
            ev.SourceInventory = ctx.SourceInventory;
            ev.DestinationInventory = ctx.DestinationInventory;
            ev.SourceKey = StableIdentityResolver.ResolveInventoryOnly(ctx.SourceInventory);
            ev.DestinationKey = StableIdentityResolver.ResolveInventoryOnly(ctx.DestinationInventory);
            ev.MovedCount = moved;
            ev.RequestedCount = ctx.RequestedCount;
            return ev;
        }

        private static GameInventory ResolveDestination(GameInventory lastInventory, GameItem lastItem)
        {
            if (lastInventory != null)
            {
                return lastInventory;
            }
            if (lastItem != null)
            {
                try { return lastItem.parentInventory; }
                catch (Exception) { return null; }
            }
            return null;
        }

        /// <summary>Read-only aggregate of how many units of an item key an inventory currently holds.</summary>
        internal static int CountUnits(GameInventory inventory, string itemKey)
        {
            if (inventory == null || string.IsNullOrEmpty(itemKey))
            {
                return 0;
            }
            try
            {
                List<GameItem> items = inventory.childItems;
                if (items == null)
                {
                    return 0;
                }
                int total = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    GameItem it = items[i];
                    if (it != null && StableIdentityResolver.ResolveItem(it) == itemKey)
                    {
                        total += it.unitCount;
                    }
                }
                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
