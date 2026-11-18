using System;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Transfer
{
    internal static class ContainerTransfer
    {
        internal static int MoveInto(PixelWindow target, GameItem item, PixelWindow source)
        {
            if (target == null || item == null)
            {
                return 0;
            }

            int requested = item.unitCount;
            if (requested <= 0)
            {
                return 0;
            }

            int leftover = GraphUtils.TryAcceptAll(target, item);
            int moved = requested - leftover;

            try
            {
                item.Validate();
            }
            catch (Exception e)
            {
                ModEntry.LogError("validating the moved item failed: " + e.Message);
            }

            Refresh(target, source);
            return moved;
        }

        internal static void Refresh(PixelWindow target, PixelWindow source)
        {
            try
            {
                if (target != null)
                {
                    target.Validate();
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("refreshing the target window failed: " + e.Message);
            }

            try
            {
                if (source != null && !ReferenceEquals(source, target))
                {
                    source.Validate();
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("refreshing the source window failed: " + e.Message);
            }

            try
            {
                if (OverlayHandler.current != null)
                {
                    OverlayHandler.current.UpdateInventoryStateUI();
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("refreshing the inventory overlay failed: " + e.Message);
            }
        }
    }
}
