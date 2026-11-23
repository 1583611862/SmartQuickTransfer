using System;
using System.Collections.Generic;

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
            List<TargetCandidate> result = candidates ?? new List<TargetCandidate>();

            // 1) Deterministic ordering by native focusStamp (descending), tie-broken by original
            //    collection order. Ranking is only over already-valid, slot-compatible candidates.
            result.Sort(CompareByRecency);

            float itemLearningWeight = PreferenceLearningService.GetLearningWeight(
                StableIdentityResolver.ResolveItem(item));

            for (int rank = 0; rank < result.Count; rank++)
            {
                TargetCandidate c = result[rank];
                c.RecencyScore = 100f * (float)Math.Pow(RecencyDecay, rank);
                c.AffinityScore = ItemDestinationAffinityRules.Score(item, c.TargetKey);
                float baseScore = RecencyWeight * c.RecencyScore + AffinityWeight * c.AffinityScore;

                c.LearnedScore = PreferenceLearningService.GetPreference(
                    StableIdentityResolver.ResolveItem(item), c.TargetKey);
                c.FinalScore = (1f - itemLearningWeight) * baseScore + itemLearningWeight * c.LearnedScore;
            }

            // 2) Final sort by score (descending), stable tie-break by recency then original order.
            result.Sort(CompareByFinalScore);
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
