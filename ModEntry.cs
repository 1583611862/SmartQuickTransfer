using System;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer
{
    public class ModEntry : IMod
    {
        internal static ModLog Log;
        internal static string ModId = "";
        internal static TransferSettings Settings = TransferSettings.CreateDefaults();

        public void Init(ModManifest manifest)
        {
            ModId = manifest.ID;
            Log = new ModLog(manifest);
            Settings = TransferSettings.Create(manifest.ID);
        }

        public void OnEnable()
        {
            LogInfo("enabled.");
            try
            {
                PreferenceLearningService.Initialize();
            }
            catch (Exception e)
            {
                LogError("failed to initialise the learning model: " + e.Message);
            }
        }

        public void OnDisable()
        {
            try
            {
                // Final flush of aggregated preferences, then drop in-memory predictions.
                PreferenceLearningService.Shutdown();
                PredictionLedger.Clear();
            }
            catch (Exception e)
            {
                LogError("failed during shutdown: " + e.Message);
            }
            LogInfo("disabled.");
        }

        internal static void LogInfo(string message)
        {
            if (Log != null)
            {
                Log.Log("[SmartQuickTransfer] " + message);
            }
        }

        internal static void LogError(string message)
        {
            if (Log != null)
            {
                Log.Error("[SmartQuickTransfer] " + message);
            }
        }
    }
}
