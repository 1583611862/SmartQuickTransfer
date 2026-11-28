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
    /// Lightweight online preference learning (CLASS B / persisted data). No neural network, no
    /// network. Bayesian utility U = 100*(P+1)/(P+N+2); learning weight L = 0.60*E/(E+3);
    /// 45-day half-life. In-memory model is updated immediately on each correction; disk writes are
    /// debounced/batched and driven only by business events (never per-frame, never a background
    /// thread). A dirty flag is retained when a save fails so the next safe point retries.
    /// </summary>
    internal static class PreferenceLearningService
    {
        internal const int MaxPersistedItems = 512;
        internal const int MaxPersistedTargetsPerItem = 32;
        internal const float HalfLifeDays = 45f;
        internal const float MaxLearningWeight = 0.60f;
        internal const float ConfidenceDenominator = 3f;

        // Batch-save policy (business-event driven, no timers/threads).
        private const double DebounceSeconds = 5.0;
        private const double PeriodicSafeSeconds = 120.0;
        private const int ThresholdUpdates = 12;

        private static List<ItemPreference> Model = new List<ItemPreference>();
        private static bool Loaded;
        private static bool Dirty;
        private static int PendingUpdates;
        private static DateTime LastSaveUtc = DateTime.MinValue;
        private static DateTime LastChangeUtc = DateTime.MinValue;
        private static string LastSaveSource = "never";

        internal static bool IsDirty { get { return Dirty; } }
        internal static int ItemCount { get { return Model.Count; } }
        internal static int TargetCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Model.Count; i++)
                {
                    count += Model[i].Targets.Count;
                }
                return count;
            }
        }
        internal static string LastSaveUtcText
        {
            get
            {
                return LastSaveUtc == DateTime.MinValue ? "never" : LastSaveUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
        }

        internal static void Initialize()
        {
            if (Loaded)
            {
                return;
            }
            PreferenceStore.LoadResult load = PreferenceStore.Load();
            Model = load.Model;
            Loaded = true;
            Dirty = false;
            PendingUpdates = 0;
            LastSaveUtc = load.Success ? DateTime.UtcNow : DateTime.MinValue;
            LastSaveSource = load.Success ? "loaded" : "none";
        }

        internal static void ResetForTests()
        {
            Model = new List<ItemPreference>();
            Loaded = true;
            Dirty = false;
            PendingUpdates = 0;
        }

        /// <summary>
        /// Called from a business event (double click). Performs a periodic safe save only when
        /// dirty and enough time has elapsed. Never called from Update/LateUpdate/FixedUpdate.
        /// </summary>
        internal static void TickSafeSave()
        {
            if (!Loaded || !Dirty)
            {
                return;
            }
            DateTime now = DateTime.UtcNow;
            if ((now - LastSaveUtc).TotalSeconds >= PeriodicSafeSeconds
                || (now - LastChangeUtc).TotalSeconds >= DebounceSeconds)
            {
                SaveNow("periodic_safe");
            }
        }

        /// <summary>Flush at game exit / mod unload: save dirty data first, then release state.</summary>
        internal static void Shutdown(string reason = "lifecycle")
        {
            if (!Loaded)
            {
                return;
            }
            if (Dirty)
            {
                SaveNow(reason);
            }
            TransferDiagnostics.SetSnapshot(
                PredictionLedger.Count, ItemCount, TargetCount, Dirty, LastSaveUtcText);
        }

        /// <summary>
        /// USER-INITIATED reset only. Never called by game exit, mod unload, disable, or any
        /// temporary-data cleanup. Deletes persisted learning data via the safe reset flow.
        /// </summary>
        internal static bool ResetPreferenceStore(string reason)
        {
            Initialize();
            PreferenceStore.SaveResult reset = PreferenceStore.Reset();
            if (reset.Success)
            {
                Model = new List<ItemPreference>();
                Dirty = false;
                PendingUpdates = 0;
                LastSaveUtc = DateTime.UtcNow;
                LastSaveSource = "reset";
                TransferDiagnostics.PreferenceReset(true, reason);
            }
            else
            {
                TransferDiagnostics.PreferenceReset(false, reason + " (" + reset.Reason + ")");
            }
            return reset.Success;
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
        /// Apply one correction: negative feedback to the wrongly predicted target, positive to the
        /// target the player chose. Weight W scales both; the event count increases once only.
        /// Returns detailed before/after values so the caller can log the ACTUAL model update.
        /// </summary>
        internal static LearningUpdateInfo ApplyCorrection(string itemKey, string predictedTargetKey,
            string actualTargetKey, float weight)
        {
            LearningUpdateInfo info = new LearningUpdateInfo();
            info.Success = false;
            info.ItemKey = itemKey;
            info.OldTarget = predictedTargetKey;
            info.NewTarget = actualTargetKey;
            info.FeedbackWeight = weight;
            if (string.IsNullOrEmpty(itemKey) || weight <= 0f)
            {
                return info;
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
                return info;
            }

            DateTime now = DateTime.UtcNow;
            ItemPreference item = FindOrCreate(itemKey);

            float eventsBefore = item.CorrectionEvents;
            item.CorrectionEvents = Decay(item.CorrectionEvents, item.Targets.Count > 0 ? OldestUpdate(item) : now, now) + weight;
            info.CorrectionEvents = item.CorrectionEvents;

            if (predictedTargetKey != null)
            {
                TargetPreference tp = GetOrCreateTarget(item, predictedTargetKey, now);
                info.OldPositiveBefore = tp.PositiveEvidence;
                info.OldNegativeBefore = tp.NegativeEvidence;
                tp.PositiveEvidence = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now);
                tp.NegativeEvidence = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now) + weight;
                tp.LastUpdatedUtc = now;
                info.OldPositiveAfter = tp.PositiveEvidence;
                info.OldNegativeAfter = tp.NegativeEvidence;
            }
            if (actualTargetKey != null && actualTargetKey != predictedTargetKey)
            {
                TargetPreference tp = GetOrCreateTarget(item, actualTargetKey, now);
                info.NewPositiveBefore = tp.PositiveEvidence;
                info.NewNegativeBefore = tp.NegativeEvidence;
                tp.PositiveEvidence = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now) + weight;
                tp.NegativeEvidence = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now);
                tp.LastUpdatedUtc = now;
                info.NewPositiveAfter = tp.PositiveEvidence;
                info.NewNegativeAfter = tp.NegativeEvidence;
            }

            info.Confidence = item.CorrectionEvents / (item.CorrectionEvents + ConfidenceDenominator);
            info.LearningWeight = MaxLearningWeight * info.Confidence;

            Prune();
            Dirty = true;
            PendingUpdates++;
            LastChangeUtc = now;
            info.Success = true;

            // Threshold-triggered early save; otherwise debounce/periodic happens on business ticks.
            if (PendingUpdates >= ThresholdUpdates)
            {
                SaveNow("threshold");
            }
            return info;
        }

        /// <summary>
        /// Record a genuine player drag as direct evidence that an item belongs in the destination.
        /// This is the natural way players express intent ("put this in B"), unlike the older
        /// correction path which required dragging back OUT of our predicted target. Only positive
        /// evidence is added; no target is punished because there was no wrong prediction to negate.
        /// Reuses the same persisted structure (positive evidence, event count, decay, half-life,
        /// debounced save) so the on-disk data version is unchanged.
        /// </summary>
        internal static LearningUpdateInfo RecordDirectPreference(string itemKey, string actualTargetKey,
            float weight)
        {
            LearningUpdateInfo info = new LearningUpdateInfo();
            info.Success = false;
            info.ItemKey = itemKey;
            info.NewTarget = actualTargetKey;
            info.FeedbackWeight = weight;
            if (string.IsNullOrEmpty(itemKey) || string.IsNullOrEmpty(actualTargetKey) || weight <= 0f)
            {
                return info;
            }
            Initialize();

            DateTime now = DateTime.UtcNow;
            ItemPreference item = FindOrCreate(itemKey);

            float eventsBefore = item.CorrectionEvents;
            item.CorrectionEvents = Decay(item.CorrectionEvents, item.Targets.Count > 0 ? OldestUpdate(item) : now, now) + weight;
            info.CorrectionEvents = item.CorrectionEvents;

            TargetPreference tp = GetOrCreateTarget(item, actualTargetKey, now);
            info.NewPositiveBefore = tp.PositiveEvidence;
            info.NewNegativeBefore = tp.NegativeEvidence;
            tp.PositiveEvidence = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now) + weight;
            tp.NegativeEvidence = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now);
            tp.LastUpdatedUtc = now;
            info.NewPositiveAfter = tp.PositiveEvidence;
            info.NewNegativeAfter = tp.NegativeEvidence;

            info.Confidence = item.CorrectionEvents / (item.CorrectionEvents + ConfidenceDenominator);
            info.LearningWeight = MaxLearningWeight * info.Confidence;

            Prune();
            Dirty = true;
            PendingUpdates++;
            LastChangeUtc = now;
            info.Success = true;

            if (PendingUpdates >= ThresholdUpdates)
            {
                SaveNow("threshold");
            }
            return info;
        }

        internal sealed class LearningUpdateInfo
        {
            internal bool Success;
            internal string ItemKey = "";
            internal string OldTarget = "";
            internal string NewTarget = "";
            internal float OldPositiveBefore;
            internal float OldPositiveAfter;
            internal float OldNegativeBefore;
            internal float OldNegativeAfter;
            internal float NewPositiveBefore;
            internal float NewPositiveAfter;
            internal float NewNegativeBefore;
            internal float NewNegativeAfter;
            internal float CorrectionEvents;
            internal float Confidence;
            internal float LearningWeight;
            internal float FeedbackWeight;
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

        /// <summary>Bayesian-smoothed utility U. Returns neutral 50 for untrained items.</summary>
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

            TargetPreference tp = FindPreferenceWithAliases(item, targetKey);
            if (tp == null)
            {
                return 50f;
            }
            DateTime now = DateTime.UtcNow;
            float p = Decay(tp.PositiveEvidence, tp.LastUpdatedUtc, now);
            float n = Decay(tp.NegativeEvidence, tp.LastUpdatedUtc, now);
            return 100f * (p + 1f) / (p + n + 2f);
        }

        /// <summary>
        /// Resolve a stored preference by exact key first, then by equivalent legacy aliases.
        /// Older releases keyed machine/container destinations from the window owner while the
        /// manual-drag path used the inventory key; the same physical container could therefore
        /// be stored under "machine:&lt;id&gt;", "container:&lt;id&gt;" or "storage:&lt;id&gt;".
        /// Reading aliases keeps that history alive without rewriting or clearing any data.
        /// </summary>
        private static TargetPreference FindPreferenceWithAliases(ItemPreference item, string targetKey)
        {
            TargetPreference tp;
            if (item.Targets.TryGetValue(targetKey, out tp))
            {
                return tp;
            }

            string suffix = StripTargetPrefix(targetKey);
            if (string.IsNullOrEmpty(suffix))
            {
                return null;
            }

            string[] prefixes = { StableIdentityResolver.PrefixStorage,
                StableIdentityResolver.PrefixMachine, StableIdentityResolver.PrefixContainer };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (item.Targets.TryGetValue(prefixes[i] + suffix, out tp))
                {
                    return tp;
                }
            }
            return null;
        }

        private static string StripTargetPrefix(string key)
        {
            string[] prefixes = { StableIdentityResolver.PrefixStorage,
                StableIdentityResolver.PrefixMachine, StableIdentityResolver.PrefixContainer,
                StableIdentityResolver.PrefixUnknown };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (key.StartsWith(prefixes[i], StringComparison.Ordinal))
                {
                    return key.Substring(prefixes[i].Length);
                }
            }
            return key;
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

        /// <summary>Save the current model now. Dirty is cleared only on confirmed success.</summary>
        private static void SaveNow(string reason)
        {
            TransferDiagnostics.PreferenceSaveStarted(Dirty, PendingUpdates);
            PreferenceStore.SaveResult save = PreferenceStore.Save(Model);
            if (save.Success)
            {
                Dirty = false;
                PendingUpdates = 0;
                LastSaveUtc = DateTime.UtcNow;
                LastSaveSource = reason;
            }
            // On failure we deliberately keep Dirty=true and PendingUpdates so a later safe point retries.
            TransferDiagnostics.PreferenceSaved(
                save.Success, Dirty, save.FilePath, save.FileBytes,
                save.DurationMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                save.Success ? reason : save.Reason);
        }
    }
}
