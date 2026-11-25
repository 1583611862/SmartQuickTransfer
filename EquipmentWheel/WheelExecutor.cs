using System;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Executes an equipment change through the game's own entry point and verifies the result
    /// against real state. The vanilla path is ItemContextHandler's Equip button, which calls:
    ///
    ///     ItemSelectHandler.current.TryEquipItem(gameItem);
    ///
    /// TryEquipItem accepts an arbitrary GameItem - it does NOT require a Toolbox slot - and
    /// itself checks effective ownership, MaySelectSlotItem and CanSelectSlotItem. We therefore
    /// never move, swap or overwrite anything in the player's 1-9 Toolbox slots.
    ///
    /// A wheel sector is a ToolGroup (one tool type, possibly several instances). At confirm time
    /// every instance is re-validated and the first still-usable one is equipped, so a stale or
    /// moved copy does not block the other copies of the same tool.
    /// </summary>
    internal static class WheelExecutor
    {
        internal static bool TryEquip(ToolGroup group)
        {
            if (group == null)
            {
                return false;
            }
            try
            {
                ItemSelectHandler selector = ItemSelectHandler.current;
                if (selector == null)
                {
                    return false;
                }

                System.Collections.Generic.List<GameItem> items = group.Instances;
                for (int i = 0; i < items.Count; i++)
                {
                    GameItem item = items[i];
                    if (item == null)
                    {
                        continue;
                    }
                    // Re-run the admission gate at confirm time: the instance may have moved.
                    if (!WheelEligibility.IsToolboxAdmitted(item) || !WheelEligibility.CanEquipNow(item))
                    {
                        continue;
                    }

                    selector.TryEquipItem(item);
                    // A missing exception is not success: verify the game's actual equipped ref.
                    if (ReferenceEquals(selector.GetEquippedItem(), item))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ModEntry.LogError("equipment wheel: native equip failed: " + e.Message);
                return false;
            }
        }
    }
}
