using System;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// The single admission gate for the equipment wheel.
    ///
    /// This does NOT maintain a whitelist. It delegates to the exact predicate the vanilla game
    /// uses when it wires a Toolbox slot in ToolboxHelper.InitToolbox:
    ///
    ///     gameSlotInventory.mayInventoryAddItemFunc += (item, _) =&gt;
    ///         slotIndex &lt; GetUnlockedSlots(box)
    ///         &amp;&amp; GeneralHelper.IsItemOwned(item)
    ///         &amp;&amp; ToolboxHelper.IsToolboxCompatible(item);
    ///
    /// The slot-index clause is deliberately NOT copied: the wheel is an extension of the Toolbox
    /// that is not bounded by the nine vanilla slots. The two item clauses are the real admission
    /// rule and are preserved verbatim. See ToolboxHelper.IsToolboxCompatible (Assembly-CSharp).
    /// </summary>
    internal static class WheelEligibility
    {
        internal static bool IsToolboxAdmitted(GameItem item)
        {
            if (item == null)
            {
                return false;
            }
            try
            {
                return GeneralHelper.IsItemOwned(item) && ToolboxHelper.IsToolboxCompatible(item);
            }
            catch (Exception)
            {
                // A malformed item must not abort the whole scan.
                return false;
            }
        }

        /// <summary>
        /// The additional guard ItemSelectHandler.TryEquipItem applies before equipping. Used at
        /// confirmation time (and for dimming entries), never as a scan-time whitelist.
        /// </summary>
        internal static bool CanEquipNow(GameItem item)
        {
            if (item == null)
            {
                return false;
            }
            try
            {
                return GeneralHelper.IsItemEffectivelyOwned(item)
                    && item.MaySelectSlotItem()
                    && item.CanSelectSlotItem();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
