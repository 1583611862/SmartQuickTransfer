using System;
using System.Collections.Generic;

namespace SmartQuickTransfer.Core
{
    [Serializable]
    internal sealed class TargetPreference
    {
        internal string TargetKey = "";
        internal float PositiveEvidence;
        internal float NegativeEvidence;
        internal DateTime LastUpdatedUtc;
    }

    [Serializable]
    internal sealed class ItemPreference
    {
        internal string ItemKey = "";
        /// <summary>Total weight of valid correction events for this item (one event counts once).</summary>
        internal float CorrectionEvents;
        internal Dictionary<string, TargetPreference> Targets = new Dictionary<string, TargetPreference>();
    }

    /// <summary>
    /// Lightweight online preference learning. No neural network, no model file training, no network.
    /// Bayesian-smoothed utility U = 100*(P+1)/(P+N+2), learning weight L = 0.60*E/(E+3),
    /// final score = (1-L)*BaseScore + L*U. Evidence and event count decay with a 45-day half-life.
    /// </summary>
    internal static class PreferenceLearningService
    {
        internal const int MaxPersistedItems = 512;
        internal const int MaxPersistedTargetsPerItem = 32;
        internal const float HalfLifeDays = 45f;
        internal const float MaxLearningWeight = 0.60f;
        internal const float ConfidenceDenominator = 3f;

        private static List<ItemPreference> Model = new List<ItemPreference>();
        private static bool Loaded;
        private static DateTime LastSaveUtc = DateTime.MinValue;
        private const float MinSaveIntervalSeconds = 8f;

        internal static void Initialize()
        {
            if (Loaded)
            {
                return;
            }
            Model = PreferenceStore.Load();
            Loaded = true;
            ModEntry.LogInfo("learning model loaded: " + Model.Count + " item(s).");
        }

        internal static void ResetForTests()
        {
            Model = new List<ItemPreference>();
            Loaded = true;
        }

        internal static void Shutdown()
        {
            if (!Loaded)
            {
                return;
            }
            PreferenceStore.Save(Model);
            LastSaveUtc = DateTime.UtcNow;
        }

        private static ItemPreference FindOrCreate(string itemKey)
        {
            for (int i = 0; i < Model.Count; i++)
            {
                if (Model[i].ItemKey == itemKey)
                {
                    return Model[i];
                }
            }
            ItemPreference item = new ItemPreference();
            item.ItemKey = itemKey;
            Model.Add(item);
            return item;
        }

        /// <summary>
        /// Apply one correction: negative feedback to the wrongly predicted target, positive
        /// feedback to the target the player actually chose. Weight W scales both; the correction
        /// event count increases once (never doubled for the two target updates).
        /// </summary>
        internal static void ApplyCorrection(string itemKey, string predictedTargetKey, string actualTargetKey, float weight)
        {
            if (string.IsNullOrEmpty(itemKey) || weight <= 0f)
            {
                return;
            }
            Initialize();

            if (string.IsNullOrEmpty(predictedTargetKey))
            {
                predictedTargetKey = null;
            }
            if (string.IsNullOrEmpty(actualTargetKey))
            {
                actualTargetKey = null;
            }
            if (predictedTargetKey == null && actualTargetKey == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            ItemPreference item = FindOrCreate(itemKey);

            // One event counts once, with the same partial weight as the feedback updates.
            item.CorrectionEvents = Decay(item.CorrectionEvents, item.Targets.Count > 0 ? OldestUpdate(item) : now, now) + weight;

            if (predictedTargetKey != null)
            {
                TargetPreference tp = GetOrCreateTarget(item, predictedTargetKey, now);
                tp.PositiveEvidence = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now);
                tp.NegativeEvidence = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now) + weight;
                tp.LastUpdatedUtc = now;
                ModEntry.LogInfo("learning updated: item=" + itemKey + ", target=" + predictedTargetKey
                    + ", positive=" + Round(tp.PositiveEvidence) + ", negative=" + Round(tp.NegativeEvidence));
            }
            if (actualTargetKey != null && actualTargetKey != predictedTargetKey)
            {
                TargetPreference tp = GetOrCreateTarget(item, actualTargetKey, now);
                tp.PositiveEvidence = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now) + weight;
                tp.NegativeEvidence = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now);
                tp.LastUpdatedUtc = now;
                ModEntry.LogInfo("learning updated: item=" + itemKey + ", target=" + actualTargetKey
                    + ", positive=" + Round(tp.PositiveEvidence) + ", negative=" + Round(tp.NegativeEvidence));
            }

            Prune();
            MaybeSave();
        }

        private static TargetPreference GetOrCreateTarget(ItemPreference item, string key, DateTime now)
        {
            TargetPreference tp;
            if (!item.Targets.TryGetValue(key, out tp))
            {
                tp = new TargetPreference();
                tp.TargetKey = key;
                tp.LastUpdatedUtc = now;
                item.Targets[key] = tp;
            }
            return tp;
        }

        private static DateTime OldestUpdate(ItemPreference item)
        {
            DateTime oldest = DateTime.UtcNow;
            foreach (KeyValuePair<string, TargetPreference> kv in item.Targets)
            {
                if (kv.Value.LastUpdatedUtc < oldest)
                {
                    oldest = kv.Value.LastUpdatedUtc;
                }
            }
            return oldest;
        }

        /// <summary>Learning weight for an item: 0 with no corrections, approaching 0.60 max.</summary>
        internal static float GetLearningWeight(string itemKey)
        {
            if (!Loaded)
            {
                return 0f;
            }
            ItemPreference item = Find(itemKey);
            if (item == null || item.CorrectionEvents <= 0f)
            {
                return 0f;
            }
            float e = Decay(item.CorrectionEvents, OldestUpdate(item), DateTime.UtcNow);
            return MaxLearningWeight * (e / (e + ConfidenceDenominator));
        }

        /// <summary>
        /// Bayesian-smoothed preference U for (item, target). Returns 50 (neutral) for untrained
        /// items so they are completely unaffected by the learning model.
        /// </summary>
        internal static float GetPreference(string itemKey, string targetKey)
        {
            if (!Loaded || string.IsNullOrEmpty(targetKey))
            {
                return 50f;
            }
            ItemPreference item = Find(itemKey);
            if (item == null)
            {
                return 50f;
            }
            TargetPreference tp;
            if (!item.Targets.TryGetValue(targetKey, out tp))
            {
                return 50f;
            }
            DateTime now = DateTime.UtcNow;
            float p = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now);
            float n = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now);
            return 100f * (p + 1f) / (p + n + 2f);
        }

        private static ItemPreference Find(string itemKey)
        {
            for (int i = 0; i < Model.Count; i++)
            {
                if (Model[i].ItemKey == itemKey)
                {
                    return Model[i];
                }
            }
            return null;
        }

        private static float Decay(float value, DateTime lastUtc, DateTime nowUtc)
        {
            if (value <= 0f)
            {
                return 0f;
            }
            double days = (nowUtc - lastUtc).TotalDays;
            if (days <= 0)
            {
                return value;
            }
            double factor = Math.Pow(0.5, days / HalfLifeDays);
            return value * (float)factor;
        }

        private static void Prune()
        {
            // Remove items that have neither meaningful evidence nor corrections (post-decay guards
            // keep values positive, so this mainly caps unbounded growth).
            for (int i = Model.Count - 1; i >= 0; i--)
            {
                ItemPreference item = Model[i];
                if (item.Targets.Count == 0 && item.CorrectionEvents <= 0.001f)
                {
                    Model.RemoveAt(i);
                }
            }
            while (Model.Count > MaxPersistedItems)
            {
                Model.RemoveAt(0);
            }
        }

        private static void MaybeSave()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - LastSaveUtc).TotalSeconds >= MinSaveIntervalSeconds)
            {
                PreferenceStore.Save(Model);
                LastSaveUtc = now;
            }
        }

        private static string Round(float value)
        {
            return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
