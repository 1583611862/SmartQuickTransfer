using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Fixed scoring model (all weights are code constants, no user configuration):
    ///   Recency  R = 100 * 0.7^rank          (rank over valid candidates by native focusStamp)
    ///   Affinity A = ItemDestinationAffinityRules (0-100)
    ///   Base     B = 0.70*R + 0.30*A
    ///   Utility  U = 100*(P+1)/(P+N+2)       (Bayesian-smoothed learned preference)
    ///   Weight   L = 0.60 * E/(E+3)          (dynamic trust in the learned model)
    ///   Final    F = (1-L)*B + L*U
    ///
    /// Untrained items get L=0 and are driven purely by the base score. Sorting is deterministic
    /// for equal focusStamp via the candidate's original collection order.
    /// </summary>
    internal static class TargetScoringService
    {
        internal const float RecencyWeight = 0.70f;
        internal const float AffinityWeight = 0.30f;
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

            // 1) Deterministic ordering by native focusStamp (descending), tie-broken by original
            //    collection order. Ranking is only over already-valid, slot-compatible candidates.
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

                TransferDiagnostics.CandidateScored(traceId, c.TargetKey, c.RecencyScore, c.AffinityScore,
                    c.LearnedScore, baseScore, itemLearningWeight, c.FinalScore, rank);
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
            if (a.FocusStamp != b.FocusStamp)
            {
                return a.FocusStamp > b.FocusStamp ? -1 : 1;
            }
            return a.OriginalOrder.CompareTo(b.OriginalOrder);
        }

        private static int CompareByFinalScore(TargetCandidate a, TargetCandidate b)
        {
            if (a.FinalScore != b.FinalScore)
            {
                return a.FinalScore > b.FinalScore ? -1 : 1;
            }
            if (a.FocusStamp != b.FocusStamp)
            {
                return a.FocusStamp > b.FocusStamp ? -1 : 1;
            }
            return a.OriginalOrder.CompareTo(b.OriginalOrder);
        }
    }
}
