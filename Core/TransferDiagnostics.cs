using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Single, unified entry point for runtime diagnostics. Every business module reports through
    /// this service instead of hand-writing log strings. Output is a fixed key=value structure so
    /// a long Player.log can be parsed programmatically after hours of real play.
    ///
    /// Design rules:
    ///   - Detailed events are compiled out unless SQT_DIAGNOSTIC is defined (Diagnostic Build).
    ///     When disabled, each method returns before ANY string allocation happens.
    ///   - All writes are isolated in try/catch: a logging failure can never break a transfer or
    ///     recurse back into the mod.
    ///   - Only real business events are recorded. No per-frame polling, no full object dumps.
    ///   - Statistics and timings are accumulated in memory and emitted as SUMMARY events.
    /// </summary>
    internal static class TransferDiagnostics
    {
#if SQT_DIAGNOSTIC
        internal static readonly bool DetailedEnabled = true;
#else
        internal static readonly bool DetailedEnabled = false;
#endif

        // ---- correlation ---------------------------------------------------------------
        private static string SessionId = "";
        private static string GameVersionRaw = "unknown";
        private static string ModVersionRaw = "unknown";
        private static string CurrentTraceId = "000000";
        private static long TraceCounter;
        private static long PredictionCounter;

        // ---- statistics -----------------------------------------------------------------
        internal static class Stat
        {
            internal const string TotalDoubleClicks = "total_double_clicks";
            internal const string NormalTransfers = "normal_transfers";
            internal const string MachineScoredTransfers = "machine_scored_transfers";
            internal const string MachineReturnFallbacks = "machine_return_fallbacks";
            internal const string NormalFallbacks = "normal_fallbacks";
            internal const string SpecialBranchTransfers = "special_branch_transfers";
            internal const string SuccessfulTransfers = "successful_transfers";
            internal const string FailedTransfers = "failed_transfers";
            internal const string PartialTransfers = "partial_transfers";
            internal const string CandidateScanCount = "candidate_scan_count";
            internal const string CandidateRejectedCount = "candidate_rejected_count";
            internal const string ManualDragCount = "manual_drag_count";
            internal const string CorrectionMatchedCount = "correction_matched_count";
            internal const string CorrectionRejectedCount = "correction_rejected_count";
            internal const string LearningUpdateCount = "learning_update_count";
            internal const string DirectPreferenceCount = "direct_preference_count";
            internal const string PredictionExpiredCount = "prediction_expired_count";
            internal const string PredictionClearedCount = "prediction_cleared_count";
            internal const string PersistenceSuccessCount = "persistence_success_count";
            internal const string PersistenceFailureCount = "persistence_failure_count";
        }

        private static readonly string[] StatOrder =
        {
            Stat.TotalDoubleClicks, Stat.NormalTransfers, Stat.MachineScoredTransfers,
            Stat.MachineReturnFallbacks, Stat.NormalFallbacks, Stat.SpecialBranchTransfers,
            Stat.SuccessfulTransfers, Stat.FailedTransfers, Stat.PartialTransfers,
            Stat.CandidateScanCount, Stat.CandidateRejectedCount, Stat.ManualDragCount,
            Stat.CorrectionMatchedCount, Stat.CorrectionRejectedCount, Stat.LearningUpdateCount,
            Stat.DirectPreferenceCount,
            Stat.PredictionExpiredCount, Stat.PredictionClearedCount,
            Stat.PersistenceSuccessCount, Stat.PersistenceFailureCount
        };

        private static readonly Dictionary<string, long> Counters = new Dictionary<string, long>();
        private const long SummaryEveryTraces = 100;

        // ---- timings --------------------------------------------------------------------
        private sealed class DurationStat
        {
            internal long Count;
            internal double TotalMs;
            internal double MaxMs;
        }
        private static readonly Dictionary<string, DurationStat> Durations = new Dictionary<string, DurationStat>();

        internal static class Duration
        {
            internal const string DoubleClick = "double_click_total";
            internal const string CandidateCollection = "candidate_collection";
            internal const string SlotFilter = "native_slot_filter";
            internal const string Scoring = "scoring";
            internal const string Transfer = "transfer";
            internal const string CorrectionMatch = "correction_match";
            internal const string LearningUpdate = "learning_update";
            internal const string PreferenceLoad = "preference_load";
            internal const string PreferenceSave = "preference_save";
        }

        // ---- throttled repeats ----------------------------------------------------------
        private static readonly Dictionary<string, long> Throttled = new Dictionary<string, long>();
        private const long ThrottleEvery = 50;

        // ================================================================================
        // Session / ids
        // ================================================================================

        internal static void Configure(string modVersion, string manifestGameVersions)
        {
            ModVersionRaw = Str(modVersion);
            GameVersionRaw = string.IsNullOrEmpty(manifestGameVersions)
                ? "unknown"
                : "unknown (manifest_declares=" + manifestGameVersions.Replace(' ', '_') + ")";
        }

        internal static void LogConfiguration(bool enabled, bool keepMachine, bool keepContainer,
            bool includeStorePanels, bool customerDemandDeskPriority, string fallbackArea,
            string machineOutputArea, bool animate)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("SESSION", "configuration",
                    "enabled=" + (enabled ? "true" : "false"),
                    "keep_machine_double_click=" + (keepMachine ? "true" : "false"),
                    "keep_container_double_click=" + (keepContainer ? "true" : "false"),
                    "include_store_panels=" + (includeStorePanels ? "true" : "false"),
                    "customer_demand_desk_priority=" + (customerDemandDeskPriority ? "true" : "false"),
                    "fallback_area=" + Str(fallbackArea),
                    "machine_output_area=" + Str(machineOutputArea),
                    "play_flight_animation=" + (animate ? "true" : "false"));
            }
            catch (Exception) { }
        }

        internal static void BeginSession()
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
                TraceCounter = 0;
                PredictionCounter = 0;
                Counters.Clear();
                Durations.Clear();
                Throttled.Clear();
                CurrentTraceId = "000000";
                Event("SESSION", "started",
                    "mod_version=" + ModVersionRaw,
                    "game_version=" + GameVersionRaw,
                    "diagnostic=true",
                    "clock=utc");
            }
            catch (Exception)
            {
                // diagnostics must never affect the mod
            }
        }

        internal static string BeginTrace(string itemKey, string itemCount, string sourceWindow, string sourceInventory)
        {
            if (!DetailedEnabled)
            {
                return "000000";
            }
            try
            {
                TraceCounter++;
                CurrentTraceId = TraceCounter.ToString("000000", CultureInfo.InvariantCulture);
                Increment(Stat.TotalDoubleClicks);
                Event("TRACE", "double_click",
                    "trace_id=" + CurrentTraceId,
                    "item=" + Str(itemKey),
                    "count=" + Str(itemCount),
                    "source_window=" + Str(sourceWindow),
                    "source_inventory=" + Str(sourceInventory));

                if (TraceCounter % SummaryEveryTraces == 0)
                {
                    LogSummary("periodic");
                }
                return CurrentTraceId;
            }
            catch (Exception)
            {
                return "000000";
            }
        }

        internal static void EndTrace(string durationMs = null)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "trace_end",
                    "trace_id=" + CurrentTraceId,
                    "duration_ms=" + (durationMs == null ? "unavailable" : durationMs));
                CurrentTraceId = "000000";
            }
            catch (Exception)
            {
                // ignore
            }
        }

        internal static string NextPredictionId()
        {
            if (!DetailedEnabled)
            {
                return "000000";
            }
            try
            {
                PredictionCounter++;
                return PredictionCounter.ToString("000000", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "000000";
            }
        }

        // ================================================================================
        // Business events
        // ================================================================================

        internal static void Branch(string traceId, string branch)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (branch == "normal")
                {
                    Increment(Stat.NormalTransfers);
                }
                else if (branch == "machine_scored")
                {
                    Increment(Stat.MachineScoredTransfers);
                }
                else if (branch == "machine_return")
                {
                    Increment(Stat.MachineReturnFallbacks);
                }
                else if (branch == "normal_fallback")
                {
                    Increment(Stat.NormalFallbacks);
                }
                else
                {
                    Increment(Stat.SpecialBranchTransfers);
                }
                Event("TRACE", "branch",
                    "trace_id=" + Str(traceId),
                    "branch=" + Str(branch));
            }
            catch (Exception) { }
        }

        internal static void CandidateScan(string traceId, int totalWindows, int eligibleCount)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.CandidateScanCount);
                Event("TRACE", "candidate_scan",
                    "trace_id=" + Str(traceId),
                    "visible_windows=" + totalWindows.ToString(CultureInfo.InvariantCulture),
                    "eligible_count=" + eligibleCount.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
        }

        internal static void CandidateRejected(string traceId, string window, string reason)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.CandidateRejectedCount);
                Event("TRACE", "candidate_rejected",
                    "trace_id=" + Str(traceId),
                    "window=" + Str(window),
                    "reason=" + Str(reason));
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Machine-source decision summary: one line per double-click so a trace shows whether the
        /// smart container route was taken and why, without re-running any side-effectful check.
        /// </summary>
        internal static void MachineDecision(string traceId, string sourceWindow, string sourceInventory,
            long sourceIntentStamp, int intendedCount, bool usedScoredTarget)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "machine_decision",
                    "trace_id=" + Str(traceId),
                    "source_window=" + Str(sourceWindow),
                    "source_inventory=" + Str(sourceInventory),
                    "source_intent_stamp=" + sourceIntentStamp.ToString(CultureInfo.InvariantCulture),
                    "intended_candidates=" + intendedCount.ToString(CultureInfo.InvariantCulture),
                    "used_scored_target=" + (usedScoredTarget ? "true" : "false"));
            }
            catch (Exception) { }
        }

        internal static void CandidateScored(string traceId, string target, int rank, long intentStamp,
            float recency, float affinity,
            float learned, float baseScore, float learningWeight, float finalScore)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "candidate_scored",
                    "trace_id=" + Str(traceId),
                    "target=" + Str(target),
                    "rank=" + rank.ToString(CultureInfo.InvariantCulture),
                    "intent_stamp=" + intentStamp.ToString(CultureInfo.InvariantCulture),
                    "recency_score=" + Num(recency),
                    "affinity_score=" + Num(affinity),
                    "learned_score=" + Num(learned),
                    "base_score=" + Num(baseScore),
                    "learning_weight=" + Num(learningWeight),
                    "final_score=" + Num(finalScore));
            }
            catch (Exception) { }
        }

        internal static void TargetSelected(string traceId, string target, bool tieBroken, bool tieDetected)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "target_selected",
                    "trace_id=" + Str(traceId),
                    "target=" + Str(target),
                    "tie_detected=" + (tieDetected ? "true" : "false"),
                    "tie_broken_by=" + (tieBroken ? "deterministic_order" : "score"));
            }
            catch (Exception) { }
        }

        internal static void NoCandidate(string traceId)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "candidate_scan",
                    "trace_id=" + Str(traceId),
                    "event_detail=no_available_candidate",
                    "fallback=true");
            }
            catch (Exception) { }
        }

        internal static void TransferResult(string traceId, string predictedTarget, string actualTarget,
            int requested, int moved, string outcome, bool partial, bool tryNext, string finalArea, string durationMs)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (moved > 0)
                {
                    Increment(Stat.SuccessfulTransfers);
                }
                else
                {
                    Increment(Stat.FailedTransfers);
                }
                if (partial)
                {
                    Increment(Stat.PartialTransfers);
                }
                Event("TRACE", "transfer_result",
                    "trace_id=" + Str(traceId),
                    "requested=" + requested.ToString(CultureInfo.InvariantCulture),
                    "moved=" + moved.ToString(CultureInfo.InvariantCulture),
                    "leftover=" + (requested - moved).ToString(CultureInfo.InvariantCulture),
                    "predicted_target=" + Str(predictedTarget),
                    "actual_target=" + Str(actualTarget),
                    "outcome=" + Str(outcome),
                    "partial=" + (partial ? "true" : "false"),
                    "try_next=" + (tryNext ? "true" : "false"),
                    "final_area=" + Str(finalArea),
                    "duration_ms=" + (durationMs == null ? "unavailable" : durationMs));
            }
            catch (Exception) { }
        }

        internal static void PredictionRegistered(string traceId, string predictionId, string itemKey,
            string sourceKey, string targetKey, int requested, int moved, long pendingCount)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("TRACE", "prediction_registered",
                    "trace_id=" + Str(traceId),
                    "prediction_id=" + Str(predictionId),
                    "item=" + Str(itemKey),
                    "source=" + Str(sourceKey),
                    "predicted_target=" + Str(targetKey),
                    "requested=" + requested.ToString(CultureInfo.InvariantCulture),
                    "moved=" + moved.ToString(CultureInfo.InvariantCulture),
                    "pending_count=" + pendingCount.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
        }

        internal static void PredictionLifecycle(string eventName, string predictionId, string reason, long pendingCount)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (eventName == "expired")
                {
                    Increment(Stat.PredictionExpiredCount);
                }
                else if (eventName == "cleared")
                {
                    Increment(Stat.PredictionClearedCount);
                }
                Event("LIFECYCLE", "prediction_" + eventName,
                    "prediction_id=" + Str(predictionId),
                    "reason=" + Str(reason),
                    "pending_count=" + pendingCount.ToString(CultureInfo.InvariantCulture),
                    "preference_store_affected=false");
            }
            catch (Exception) { }
        }

        internal static void ManualDrag(string itemKey, string source, string destination, int moved,
            bool matched, string rejectReason)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.ManualDragCount);
                Event("DRAG", "manual_transfer",
                    "item=" + Str(itemKey),
                    "source=" + Str(source),
                    "destination=" + Str(destination),
                    "moved=" + moved.ToString(CultureInfo.InvariantCulture),
                    "matched=" + (matched ? "true" : "false"),
                    "reject_reason=" + Str(rejectReason));
            }
            catch (Exception) { }
        }

        internal static void CorrectionMatched(string predictionId, string itemKey, string oldTarget,
            string newTarget, string association, float intervalSeconds, int autoMoved, int manualMoved, float ratio)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.CorrectionMatchedCount);
                Event("CORRECTION", "matched",
                    "prediction_id=" + Str(predictionId),
                    "item=" + Str(itemKey),
                    "old_target=" + Str(oldTarget),
                    "new_target=" + Str(newTarget),
                    "association=" + Str(association),
                    "interval_s=" + Num(intervalSeconds),
                    "auto_moved=" + autoMoved.ToString(CultureInfo.InvariantCulture),
                    "manual_moved=" + manualMoved.ToString(CultureInfo.InvariantCulture),
                    "ratio=" + Num(ratio));
            }
            catch (Exception) { }
        }

        internal static void CorrectionRejected(string reason)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.CorrectionRejectedCount);
                Event("CORRECTION", "rejected", "reason=" + Str(reason));
            }
            catch (Exception) { }
        }

        internal static void LearningUpdate(string predictionId, string itemKey, string oldTarget, string newTarget,
            float oldTargetPositiveBefore, float oldTargetPositiveAfter,
            float oldTargetNegativeBefore, float oldTargetNegativeAfter,
            float newTargetPositiveBefore, float newTargetPositiveAfter,
            float newTargetNegativeBefore, float newTargetNegativeAfter,
            float correctionEvents, float confidence, float learningWeight, float feedbackWeight, bool success)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (success)
                {
                    Increment(Stat.LearningUpdateCount);
                }
                Event("LEARNING", "updated",
                    "prediction_id=" + Str(predictionId),
                    "item=" + Str(itemKey),
                    "old_target=" + Str(oldTarget),
                    "new_target=" + Str(newTarget),
                    "old_target_positive=" + Num(oldTargetPositiveBefore) + "->" + Num(oldTargetPositiveAfter),
                    "old_target_negative=" + Num(oldTargetNegativeBefore) + "->" + Num(oldTargetNegativeAfter),
                    "new_target_positive=" + Num(newTargetPositiveBefore) + "->" + Num(newTargetPositiveAfter),
                    "new_target_negative=" + Num(newTargetNegativeBefore) + "->" + Num(newTargetNegativeAfter),
                    "correction_events=" + Num(correctionEvents),
                    "confidence=" + Num(confidence),
                    "learning_weight=" + Num(learningWeight),
                    "feedback_weight=" + Num(feedbackWeight),
                    "updated=" + (success ? "true" : "false"));
            }
            catch (Exception) { }
        }

        internal static void DirectPreference(string itemKey, string target, bool success, float learningWeight)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (success)
                {
                    Increment(Stat.DirectPreferenceCount);
                }
                Event("LEARNING", "direct_preference",
                    "item=" + Str(itemKey),
                    "target=" + Str(target),
                    "success=" + (success ? "true" : "false"),
                    "learning_weight=" + Num(learningWeight));
            }
            catch (Exception) { }
        }

        // ================================================================================
        // Lifecycle events (always-on subset for errors, detailed otherwise)
        // ================================================================================

        internal static void PreferenceLoaded(bool fileExists, bool success, int itemCount, int targetCount,
            bool recoveredFromBackup, string filePath, string dataVersion, string loadDurationMs)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("LIFECYCLE", "preference_loaded",
                    "file_exists=" + (fileExists ? "true" : "false"),
                    "success=" + (success ? "true" : "false"),
                    "item_count=" + itemCount.ToString(CultureInfo.InvariantCulture),
                    "target_count=" + targetCount.ToString(CultureInfo.InvariantCulture),
                    "recovered_from_backup=" + (recoveredFromBackup ? "true" : "false"),
                    "data_version=" + Str(dataVersion),
                    "path=" + Str(filePath),
                    "duration_ms=" + (loadDurationMs == null ? "unavailable" : loadDurationMs));
            }
            catch (Exception) { }
        }

        internal static void PreferenceSaveStarted(bool dirty, int pendingUpdates)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("LIFECYCLE", "preference_save_started",
                    "dirty=" + (dirty ? "true" : "false"),
                    "pending_updates=" + pendingUpdates.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
        }

        internal static void PreferenceSaved(bool success, bool dirty, string filePath, long fileBytes,
            string saveDurationMs, string reason)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                if (success)
                {
                    Increment(Stat.PersistenceSuccessCount);
                }
                else
                {
                    Increment(Stat.PersistenceFailureCount);
                }
                Event("LIFECYCLE", success ? "preference_saved" : "preference_save_failed",
                    "success=" + (success ? "true" : "false"),
                    "dirty=" + (dirty ? "true" : "false"),
                    "reason=" + Str(reason),
                    "path=" + Str(filePath),
                    "file_bytes=" + (fileBytes < 0 ? "unavailable" : fileBytes.ToString(CultureInfo.InvariantCulture)),
                    "file_preserved=" + (success ? "n/a" : "true"),
                    "duration_ms=" + (saveDurationMs == null ? "unavailable" : saveDurationMs));
            }
            catch (Exception) { }
        }

        internal static void PendingCleared(string reason, int count)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Increment(Stat.PredictionClearedCount);
                Event("LIFECYCLE", "pending_predictions_cleared",
                    "reason=" + Str(reason),
                    "count=" + count.ToString(CultureInfo.InvariantCulture),
                    "preference_store_affected=false");
            }
            catch (Exception) { }
        }

        internal static void PreferenceReset(bool success, string reason)
        {
            // Reset is a real, user-initiated action; keep it visible even in lean builds via error-free info.
            try
            {
                Event("LIFECYCLE", "preference_reset",
                    "success=" + (success ? "true" : "false"),
                    "reason=" + Str(reason));
            }
            catch (Exception) { }
        }

        internal static void ModUnloading(bool pendingCleared, bool preferenceSaved, bool preferenceDeleted)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Event("LIFECYCLE", "mod_unloading",
                    "pending_cleared=" + (pendingCleared ? "true" : "false"),
                    "preference_saved=" + (preferenceSaved ? "true" : "false"),
                    "preference_deleted=" + (preferenceDeleted ? "true" : "false"));
            }
            catch (Exception) { }
        }

        // ================================================================================
        // Timing / stats
        // ================================================================================

        internal static void RecordDuration(string key, double milliseconds)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                DurationStat stat;
                if (!Durations.TryGetValue(key, out stat))
                {
                    stat = new DurationStat();
                    Durations[key] = stat;
                }
                stat.Count++;
                stat.TotalMs += milliseconds;
                if (milliseconds > stat.MaxMs)
                {
                    stat.MaxMs = milliseconds;
                }
            }
            catch (Exception) { }
        }

        internal static void Increment(string key)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                long value;
                Counters.TryGetValue(key, out value);
                Counters[key] = value + 1;
            }
            catch (Exception) { }
        }

        /// <summary>Throttled error: count every occurrence but only emit every N-th.</summary>
        internal static void ErrorThrottled(string key, string message)
        {
            // Errors remain visible in lean builds; only the throttling bookkeeping is detailed.
            try
            {
                long count = 0;
                if (DetailedEnabled)
                {
                    Throttled.TryGetValue(key, out count);
                    count++;
                    Throttled[key] = count;
                    if (count % ThrottleEvery != 1)
                    {
                        return;
                    }
                }
                Write("[SmartQuickTransfer][ERROR] utc=" + UtcNow() + " session_id=" + SessionId
                    + " key=" + Str(key) + " occurrences=" + (DetailedEnabled ? count.ToString(CultureInfo.InvariantCulture) : "unavailable")
                    + " message=" + Str(message), true);
            }
            catch (Exception)
            {
                // never recurse
            }
        }

        internal static void LogSummary(string reason)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                // reason + StatOrder + (pending, items, targets, dirty, last_save, uptime) = StatOrder.Length + 7
                string[] fields = new string[StatOrder.Length + 7];
                int n = 0;
                fields[n++] = "reason=" + Str(reason);
                for (int i = 0; i < StatOrder.Length; i++)
                {
                    long value;
                    Counters.TryGetValue(StatOrder[i], out value);
                    fields[n++] = StatOrder[i] + "=" + value.ToString(CultureInfo.InvariantCulture);
                }
                fields[n++] = "pending_predictions=" + GetCounter("pending_snapshot");
                fields[n++] = "learning_items=" + GetCounter("learning_items_snapshot");
                fields[n++] = "learning_targets=" + GetCounter("learning_targets_snapshot");
                fields[n++] = "dirty=" + (GetCounter("dirty_snapshot") != 0 ? "true" : "false");
                fields[n++] = "last_save_utc=" + Str(GetText("last_save_utc"));
                fields[n++] = "uptime_s=" + Num(UptimeSeconds());
                Event("SUMMARY", "stats", fields);

                foreach (KeyValuePair<string, DurationStat> kv in Durations)
                {
                    DurationStat d = kv.Value;
                    Event("SUMMARY", "duration",
                        "operation=" + kv.Key,
                        "count=" + d.Count.ToString(CultureInfo.InvariantCulture),
                        "avg_ms=" + Num(d.Count > 0 ? d.TotalMs / d.Count : 0),
                        "max_ms=" + Num(d.MaxMs));
                }
            }
            catch (Exception) { }
        }

        internal static void SetSnapshot(long pendingPredictions, long learningItems, long learningTargets,
            bool dirty, string lastSaveUtc)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                Counters["pending_snapshot"] = pendingPredictions;
                Counters["learning_items_snapshot"] = learningItems;
                Counters["learning_targets_snapshot"] = learningTargets;
                Counters["dirty_snapshot"] = dirty ? 1 : 0;
                Snapshot["last_save_utc"] = string.IsNullOrEmpty(lastSaveUtc) ? "never" : lastSaveUtc;
            }
            catch (Exception) { }
        }

        private static readonly Dictionary<string, string> Snapshot = new Dictionary<string, string>();

        private static string GetText(string key)
        {
            string v;
            return Snapshot.TryGetValue(key, out v) ? v : "never";
        }

        private static long GetCounter(string key)
        {
            long v;
            return Counters.TryGetValue(key, out v) ? v : 0;
        }

        private static readonly Stopwatch Uptime = Stopwatch.StartNew();
        private static double UptimeSeconds()
        {
            return Uptime.Elapsed.TotalSeconds;
        }

        /// <summary>Final flush at session end: snapshot state, emit final summary, close session.</summary>
        internal static void Flush(long pendingPredictions, long learningItems, long learningTargets,
            bool dirty, string lastSaveUtc)
        {
            if (!DetailedEnabled)
            {
                return;
            }
            try
            {
                SetSnapshot(pendingPredictions, learningItems, learningTargets, dirty, lastSaveUtc);
                LogSummary("session_end");
                Event("SESSION", "ended",
                    "session_id=" + SessionId,
                    "uptime_s=" + Num(UptimeSeconds()));
            }
            catch (Exception) { }
        }

        // ================================================================================
        // Low-level emit
        // ================================================================================

        private static void Event(string tag, string eventName, params string[] fields)
        {
            string line = "[SmartQuickTransfer][" + tag + "] utc=" + UtcNow()
                + " session_id=" + Str(SessionId)
                + " event=" + eventName;
            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!string.IsNullOrEmpty(fields[i]))
                    {
                        line += " " + fields[i];
                    }
                }
            }
            Write(line, false);
        }

        private static void Write(string line, bool isError)
        {
            try
            {
                if (isError)
                {
                    ModEntry.LogError(line);
                }
                else
                {
                    ModEntry.LogInfo(line);
                }
            }
            catch (Exception)
            {
                // Diagnostics must never throw into gameplay, never recurse.
            }
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static string Str(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : value;
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Num(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
