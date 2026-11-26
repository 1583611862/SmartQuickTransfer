using System;
using SmartQuickTransfer.Core;
using SmartQuickTransfer.EquipmentWheel;
using System.Collections.Generic;

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

            string gameVersions = "";
            try
            {
                if (manifest.GameVersions != null)
                {
                    gameVersions = string.Join(",", manifest.GameVersions.ToArray());
                }
            }
            catch (Exception)
            {
                gameVersions = "";
            }
            TransferDiagnostics.Configure(manifest.ModVersion, gameVersions);
        }

        public void OnEnable()
        {
            LogInfo("enabled.");
            WindowIntentTracker.Reset();
            TransferDiagnostics.BeginSession();
            LogConfigurationSafe();
            try
            {
                PreferenceLearningService.Initialize();
            }
            catch (Exception e)
            {
                LogError("failed to initialise the learning model: " + e.Message);
            }
            try
            {
                EquipmentWheelController.Initialize();
            }
            catch (Exception e)
            {
                LogError("failed to initialise the equipment wheel: " + e.Message);
            }
        }

        public void OnDisable()
        {
            try
            {
                // Close the wheel first so no input stays intercepted after the mod unloads.
                EquipmentWheelController.Shutdown();
                // CLASS A: clear temporary predictions. CLASS B: save dirty preferences first.
                PreferenceLearningService.Shutdown("mod_disable");
                PredictionLedger.ClearPendingPredictions("mod_disable");
                WindowIntentTracker.Reset();
                TransferDiagnostics.ModUnloading(true, true, false);
                TransferDiagnostics.Flush(
                    PredictionLedger.Count,
                    PreferenceLearningService.ItemCount,
                    PreferenceLearningService.TargetCount,
                    PreferenceLearningService.IsDirty,
                    PreferenceLearningService.LastSaveUtcText);
            }
            catch (Exception e)
            {
                LogError("failed during shutdown: " + e.Message);
            }
            LogInfo("disabled.");
        }

        private static void LogConfigurationSafe()
        {
            TransferSettings s = Settings;
            TransferDiagnostics.LogConfiguration(
                s != null && s.Enabled,
                s == null || s.PreserveMachineOpen,
                s == null || s.PreserveContainerOpen,
                s != null && s.IncludeStorePanels,
                s == null || s.CustomerDemandDeskPriority,
                s == null ? "auto" : s.FallbackArea,
                s == null ? "仓库" : s.MachineOutputArea,
                s == null || s.Animate);
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
