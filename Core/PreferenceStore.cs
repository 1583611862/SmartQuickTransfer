using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// CLASS B DATA - persisted learning preferences. Local, versioned JSON in the game-provided
    /// ModData folder (&lt;persistentDataPath&gt;/ModData/&lt;modId&gt;), separate from ModConfigs.
    /// Only stable keys and aggregate counters are stored - never Unity objects, window references,
    /// inventories, temporary predictions (class A) or per-action logs.
    ///
    /// File safety:
    ///   - write to a temporary file, validate it, then replace the main file;
    ///   - all access goes through capability-free ModHelper mod-data APIs (no System.IO);
    ///   - keep the previous valid file as a .bak before overwriting;
    ///   - on load failure try the backup first; if both are corrupt, PRESERVE the original files
    ///     (never overwrite the only copy) and fall back to an empty model in memory;
    ///   - version-incompatible files are preserved, not silently deleted.
    /// </summary>
    internal static class PreferenceStore
    {
        internal const int CurrentVersion = 1;
        private const string FileName = "preferences.json";
        private const string BackupFileName = "preferences.json.bak";
        private const string TempFileName = "preferences.json.tmp";

        [Serializable]
        internal sealed class TargetPreferenceDto
        {
            public string targetKey;
            public float positive;
            public float negative;
            public string updatedUtc;
        }

        [Serializable]
        internal sealed class ItemPreferenceDto
        {
            public string itemKey;
            public float correctionEvents;
            public TargetPreferenceDto[] targets;
        }

        [Serializable]
        internal sealed class PreferenceFileDto
        {
            public int version;
            public ItemPreferenceDto[] items;
        }

        internal sealed class LoadResult
        {
            internal List<ItemPreference> Model = new List<ItemPreference>();
            internal bool FileExists;
            internal bool Success;
            internal bool RecoveredFromBackup;
            internal string DataVersion = "unknown";
            internal string FilePath = "unavailable";
            internal double LoadDurationMs;
            internal int TargetCount;
            internal string Reason = "";
        }

        internal sealed class SaveResult
        {
            internal bool Success;
            internal long FileBytes = -1;
            internal string FilePath = "unavailable";
            internal double DurationMs;
            internal string Reason = "";
        }

        /// <summary>Load with backup recovery and full diagnostics metadata.</summary>
        internal static LoadResult Load()
        {
            LoadResult result = new LoadResult();
            Stopwatch sw = Stopwatch.StartNew();
            result.FilePath = SafePath(FileName) ?? "unavailable";

            string mainText = null;
            try
            {
                result.FileExists = ModHelper.ModDataFileExists(ModEntry.ModId, FileName);
                mainText = ModHelper.ReadModDataFileText(ModEntry.ModId, FileName);
            }
            catch (Exception e)
            {
                result.Reason = "read_failed: " + e.Message;
                TransferDiagnostics.ErrorThrottled("preference_read", "reading learning data failed: " + e.Message);
            }

            string usedText = mainText;
            if (!string.IsNullOrEmpty(usedText))
            {
                List<ItemPreference> parsed;
                if (TryParse(usedText, out parsed, result))
                {
                    result.Model = parsed;
                    result.Success = true;
                }
                else
                {
                    // Main file present but corrupt: try the backup before giving up.
                    string backupText = null;
                    try
                    {
                        if (ModHelper.ModDataFileExists(ModEntry.ModId, BackupFileName))
                        {
                            backupText = ModHelper.ReadModDataFileText(ModEntry.ModId, BackupFileName);
                        }
                    }
                    catch (Exception e)
                    {
                        TransferDiagnostics.ErrorThrottled("preference_backup_read", "reading backup failed: " + e.Message);
                    }
                    if (!string.IsNullOrEmpty(backupText))
                    {
                        LoadResult backupProbe = new LoadResult();
                        List<ItemPreference> backupParsed;
                        if (TryParse(backupText, out backupParsed, backupProbe))
                        {
                            result.Model = backupParsed;
                            result.Success = true;
                            result.RecoveredFromBackup = true;
                        }
                    }
                    // If both are corrupt we intentionally leave both files untouched and run with
                    // an empty in-memory model - never overwrite the only copy of user data.
                }
            }

            sw.Stop();
            result.LoadDurationMs = sw.Elapsed.TotalMilliseconds;
            result.TargetCount = CountTargets(result.Model);
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.PreferenceLoad, result.LoadDurationMs);
            TransferDiagnostics.PreferenceLoaded(
                result.FileExists, result.Success, result.Model.Count, result.TargetCount,
                result.RecoveredFromBackup, result.FilePath, result.DataVersion,
                result.LoadDurationMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            return result;
        }

        /// <summary>
        /// Persists a snapshot with a backup + validate-then-replace. Failures are reported, never thrown.
        /// The main and backup files are preserved when any step fails.
        /// </summary>
        internal static SaveResult Save(List<ItemPreference> model)
        {
            SaveResult result = new SaveResult();
            if (model == null)
            {
                result.Reason = "null_model";
                return result;
            }
            Stopwatch sw = Stopwatch.StartNew();
            result.FilePath = SafePath(FileName) ?? "unavailable";
            try
            {
                List<ItemPreferenceDto> items = new List<ItemPreferenceDto>();
                int itemCount = 0;
                for (int i = 0; i < model.Count && itemCount < PreferenceLearningService.MaxPersistedItems; i++)
                {
                    ItemPreference item = model[i];
                    if (item == null || item.Targets.Count == 0)
                    {
                        continue;
                    }
                    List<TargetPreferenceDto> targets = new List<TargetPreferenceDto>();
                    int targetCount = 0;
                    foreach (KeyValuePair<string, TargetPreference> kv in item.Targets)
                    {
                        if (targetCount >= PreferenceLearningService.MaxPersistedTargetsPerItem)
                        {
                            break;
                        }
                        TargetPreference t = kv.Value;
                        TargetPreferenceDto dto = new TargetPreferenceDto();
                        dto.targetKey = t.TargetKey;
                        dto.positive = Round(t.PositiveEvidence);
                        dto.negative = Round(t.NegativeEvidence);
                        dto.updatedUtc = t.LastUpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        targets.Add(dto);
                        targetCount++;
                    }
                    ItemPreferenceDto itemDto = new ItemPreferenceDto();
                    itemDto.itemKey = item.ItemKey;
                    itemDto.correctionEvents = Round(item.CorrectionEvents);
                    itemDto.targets = targets.ToArray();
                    items.Add(itemDto);
                    itemCount++;
                }

                PreferenceFileDto file = new PreferenceFileDto();
                file.version = CurrentVersion;
                file.items = items.ToArray();
                string json = JsonUtility.ToJson(file, true);

                // 1) keep the previous valid main file as a backup (capability-free ModData API).
                string previous = ModHelper.ReadModDataFileText(ModEntry.ModId, FileName);
                if (previous != null)
                {
                    ModHelper.WriteModDataFileText(ModEntry.ModId, BackupFileName, previous);
                }
                // 2) write the candidate to a temporary file.
                ModHelper.WriteModDataFileText(ModEntry.ModId, TempFileName, json);
                // 3) validate the temporary file before replacing anything.
                string tempText = ModHelper.ReadModDataFileText(ModEntry.ModId, TempFileName);
                List<ItemPreference> probe;
                LoadResult probeResult = new LoadResult();
                if (!TryParse(tempText, out probe, probeResult))
                {
                    result.Reason = "temp_validation_failed";
                    TryDelete(TempFileName);
                    return result;
                }
                // 4) replace the main file only after successful validation.
                ModHelper.WriteModDataFileText(ModEntry.ModId, FileName, tempText);
                TryDelete(TempFileName);

                result.Success = true;
                byte[] written = ModHelper.ReadModDataFileBytes(ModEntry.ModId, FileName);
                result.FileBytes = written != null ? written.LongLength : -1;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Reason = "exception: " + e.Message;
                TransferDiagnostics.ErrorThrottled("preference_save", "saving learning data failed: " + e.Message);
            }

            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds;
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.PreferenceSave, result.DurationMs);
            return result;
        }

        /// <summary>
        /// User-initiated reset ONLY. Deletes main + backup after writing an empty validated model.
        /// Must never be called from any lifecycle cleanup path.
        /// </summary>
        internal static SaveResult Reset()
        {
            List<ItemPreference> empty = new List<ItemPreference>();
            SaveResult result = Save(empty);
            if (result.Success)
            {
                TryDelete(BackupFileName);
                TryDelete(TempFileName);
            }
            return result;
        }

        private static bool TryParse(string text, out List<ItemPreference> model, LoadResult result)
        {
            model = new List<ItemPreference>();
            try
            {
                PreferenceFileDto dto = JsonUtility.FromJson<PreferenceFileDto>(text);
                if (dto == null)
                {
                    return false;
                }
                result.DataVersion = dto.version.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Version migration policy: never delete incompatible data. Accept current version;
                // for other versions we preserve files on disk but start with an empty model.
                if (dto.version != CurrentVersion)
                {
                    return false;
                }
                if (dto.items == null)
                {
                    return true; // valid empty file
                }
                for (int i = 0; i < dto.items.Length; i++)
                {
                    ItemPreferenceDto itemDto = dto.items[i];
                    if (itemDto == null || string.IsNullOrEmpty(itemDto.itemKey))
                    {
                        continue;
                    }
                    ItemPreference item = new ItemPreference();
                    item.ItemKey = itemDto.itemKey;
                    item.CorrectionEvents = Sanitize(itemDto.correctionEvents);
                    if (itemDto.targets != null)
                    {
                        for (int j = 0; j < itemDto.targets.Length; j++)
                        {
                            TargetPreferenceDto t = itemDto.targets[j];
                            if (t == null || string.IsNullOrEmpty(t.targetKey))
                            {
                                continue;
                            }
                            TargetPreference tp = new TargetPreference();
                            tp.TargetKey = t.targetKey;
                            tp.PositiveEvidence = Sanitize(t.positive);
                            tp.NegativeEvidence = Sanitize(t.negative);
                            tp.LastUpdatedUtc = ParseUtc(t.updatedUtc);
                            item.Targets[tp.TargetKey] = tp;
                        }
                    }
                    model.Add(item);
                }
                return true;
            }
            catch (Exception)
            {
                model = new List<ItemPreference>();
                return false;
            }
        }

        private static int CountTargets(List<ItemPreference> model)
        {
            int count = 0;
            for (int i = 0; i < model.Count; i++)
            {
                count += model[i].Targets.Count;
            }
            return count;
        }

        private static string SafePath(string fileName)
        {
            try
            {
                string folder = ModHelper.GetModDataFolder(ModEntry.ModId);
                return folder == null ? null : folder + "/" + fileName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TryDelete(string relativeFileName)
        {
            try
            {
                if (!string.IsNullOrEmpty(relativeFileName))
                {
                    ModHelper.DeleteModDataFile(ModEntry.ModId, relativeFileName);
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                return 0f;
            }
            return value;
        }

        private static float Round(float value)
        {
            return (float)Math.Round(value, 3);
        }

        private static DateTime ParseUtc(string text)
        {
            DateTime parsed;
            if (DateTime.TryParse(text, out parsed))
            {
                if (parsed.Kind == DateTimeKind.Unspecified)
                {
                    parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
                return parsed.ToUniversalTime();
            }
            return DateTime.UtcNow;
        }
    }
}
