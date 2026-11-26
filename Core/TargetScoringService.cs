using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Fixed scoring model (all weights are code constants, no user configuration):
    ///   Intent   R = 100 * 0.7^rank          (rank by the player's most recent explicit intent)
    ///   Affinity A = ItemDestinationAffinityRules (0-100), a secondary tie-breaker only
    ///   Base     B = 0.85*R + 0.15*A
    ///   Utility  U = 100*(P+1)/(P+N+2)       (Bayesian-smoothed learned preference)
    ///   Weight   L = 0.60 * E/(E+3)          (dynamic trust in the learned model)
    ///   Final    F = (1-L)*B + L*U
    ///
    /// Ranking is driven by the frozen WindowIntentTracker snapshot, not the game's focusStamp,
    /// which is refreshed by unrelated clicks. A candidate never touched by the player falls back
    /// to its native focusStamp so an untouched but sensible window can still be chosen when no
    /// explicit intent exists. Affinity is deliberately down-weighted: the player's last chosen
    /// container wins unless it cannot hold the item. Untrained items get L=0.
    /// </summary>
    internal static class TargetScoringService
    {
        internal const float RecencyWeight = 0.85f;
        internal const float AffinityWeight = 0.15f;
        internal const float RecencyDecay = 0.7f;

        /// <summary>
        /// Scores all candidates in place and returns them ordered best-first.
        /// </summary>
        internal static List<TargetCandidate> ScoreAndRank(GameItem item, List<TargetCandidate> candidates)
        {
            return ScoreAndRank(item, candidates, null);
        }

        /// <summary>Diagnostic-aware scoring. Computations are identical to the plain overload.</summary>
        internal static List<TargetCandidate> ScoreAndRank(GameItem item, List<TargetCandidate> candidates, string traceId)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<TargetCandidate> result = candidates ?? new List<TargetCandidate>();

            // 1) Deterministic ordering by player intent (descending): explicitly touched windows
            //    rank above untouched ones; untouched windows fall back to native focusStamp.
            //    Tie-broken by original collection order. Only valid, slot-compatible candidates rank.
            result.Sort(CompareByRecency);

            string itemKey = StableIdentityResolver.ResolveItem(item);
            float itemLearningWeight = PreferenceLearningService.GetLearningWeight(itemKey);

            for (int rank = 0; rank < result.Count; rank++)
            {
                TargetCandidate c = result[rank];
                c.RecencyScore = 100f * (float)Math.Pow(RecencyDecay, rank);
                c.AffinityScore = ItemDestinationAffinityRules.Score(item, c.TargetKey);
                float baseScore = RecencyWeight * c.RecencyScore + AffinityWeight * c.AffinityScore;

                c.LearnedScore = PreferenceLearningService.GetPreference(itemKey, c.TargetKey);
                c.FinalScore = (1f - itemLearningWeight) * baseScore + itemLearningWeight * c.LearnedScore;

                TransferDiagnostics.CandidateScored(traceId, c.TargetKey, rank, c.IntentStamp,
                    c.RecencyScore, c.AffinityScore, c.LearnedScore, baseScore,
                    itemLearningWeight, c.FinalScore);
            }

            // Detect ties on the top final score before the deterministic tie-break.
            bool tieDetected = false;
            if (result.Count > 1)
            {
                for (int i = 1; i < result.Count; i++)
                {
                    if (result[i].FinalScore == result[0].FinalScore)
                    {
                        tieDetected = true;
                        break;
                    }
                }
            }

            // 2) Final sort by score (descending), stable tie-break by recency then original order.
            result.Sort(CompareByFinalScore);
            sw.Stop();
            TransferDiagnostics.RecordDuration(TransferDiagnostics.Duration.Scoring, sw.Elapsed.TotalMilliseconds);
            if (result.Count > 0)
            {
                TransferDiagnostics.TargetSelected(traceId, result[0].TargetKey, tieDetected, tieDetected);
            }
            return result;
        }

        private static int CompareByRecency(TargetCandidate a, TargetCandidate b)
        {
            return CompareIntent(a, b);
        }

        /// <summary>
        /// A touched window always outranks an untouched one. Among touched windows, the higher
        /// intent stamp wins; among untouched windows, the native focusStamp is the fallback key.
        /// Final tie-break is the stable collection order.
        /// </summary>
        private static int CompareIntent(TargetCandidate a, TargetCandidate b)
        {
            bool aTouched = a.IntentStamp > 0L;
            bool bTouched = b.IntentStamp > 0L;
            if (aTouched != bTouched)
            {
                return aTouched ? -1 : 1;
            }
            long aKey = aTouched ? a.IntentStamp : a.FocusStamp;
            long bKey = bTouched ? b.IntentStamp : b.FocusStamp;
            if (aKey != bKey)
            {
                return aKey > bKey ? -1 : 1;
            }
            return a.OriginalOrder.CompareTo(b.OriginalOrder);
        }

        private static int CompareByFinalScore(TargetCandidate a, TargetCandidate b)
        {
            if (a.FinalScore != b.FinalScore)
            {
                return a.FinalScore > b.FinalScore ? -1 : 1;
            }
            return CompareIntent(a, b);
        }
    }
}
