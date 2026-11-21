using System;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Transfer
{
    internal static class CustomerDemandResolver
    {
        internal static bool WantsItem(GameItem item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                PlayerStore store = PlayerStore.Instance;
                if (store == null || store.currentClientInstance == null)
                {
                    return false;
                }

                StoreClient client = store.currentClientInstance.GetClientBlueprint();
                if (client == null)
                {
                    return false;
                }

                if (!GeneralHelper.IsItemOwned(item))
                {
                    return false;
                }

                return client.IsClientBuyingThisItem(item);
            }
            catch (Exception e)
            {
                ModEntry.LogError("customer demand check failed: " + e.Message);
                return false;
            }
        }
    }
}
