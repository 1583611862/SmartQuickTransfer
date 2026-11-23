using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Local, versioned JSON persistence of aggregated preferences. Uses the game-provided ModData
    /// folder (<persistentDataPath>/ModData/&lt;modId&gt;), completely separate from ModConfigs.
    /// Only stable keys and aggregate counters are stored - never Unity objects, window references,
    /// inventories, temporary predictions or per-action logs. JSON is written to a temporary file
    /// then moved atomically over the target. Any read/parse failure safely yields an empty model.
    /// </summary>
    internal static class PreferenceStore
    {
        internal const int CurrentVersion = 1;
        private const string FileName = "preferences.json";
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

        /// <summary>Loads all persisted preferences. Corrupt or unknown-version files reset to empty.</summary>
        internal static List<ItemPreference> Load()
        {
            List<ItemPreference> result = new List<ItemPreference>();
            string text = null;
            try
            {
                text = ModHelper.ReadModDataFileText(ModEntry.ModId, FileName);
            }
            catch (Exception e)
            {
                ModEntry.LogError("reading learning data failed: " + e.Message);
                return result;
            }

            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            try
            {
                PreferenceFileDto dto = JsonUtility.FromJson<PreferenceFileDto>(text);
                if (dto == null || dto.version != CurrentVersion || dto.items == null)
                {
                    ModEntry.LogInfo("learning data version mismatch or empty; starting fresh.");
                    return result;
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
                    result.Add(item);
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("learning data was corrupt; resetting to an empty model: " + e.Message);
                return new List<ItemPreference>();
            }

            return result;
        }

        /// <summary>Persists a snapshot atomically. Failures are logged but never thrown to gameplay.</summary>
        internal static void Save(List<ItemPreference> model)
        {
            if (model == null)
            {
                return;
            }
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

                // Atomic replace: write to a temporary file in the same folder, then overwrite.
                ModHelper.WriteModDataFileText(ModEntry.ModId, TempFileName, json);
                string tempPath = ResolveTempPath();
                string finalPath = ResolveFinalPath();
                if (tempPath != null && finalPath != null)
                {
                    System.IO.File.Copy(tempPath, finalPath, true);
                    System.IO.File.Delete(tempPath);
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("saving learning data failed (gameplay unaffected): " + e.Message);
            }
        }

        private static string ResolveFinalPath()
        {
            try { return System.IO.Path.Combine(ModHelper.GetModDataFolder(ModEntry.ModId), FileName); }
            catch (Exception) { return null; }
        }

        private static string ResolveTempPath()
        {
            try { return System.IO.Path.Combine(ModHelper.GetModDataFolder(ModEntry.ModId), TempFileName); }
            catch (Exception) { return null; }
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

        internal static void Delete()
        {
            try
            {
                ModHelper.DeleteModDataFile(ModEntry.ModId, FileName);
                ModHelper.DeleteModDataFile(ModEntry.ModId, TempFileName);
            }
            catch (Exception e)
            {
                ModEntry.LogError("deleting learning data failed: " + e.Message);
            }
        }
    }
}
