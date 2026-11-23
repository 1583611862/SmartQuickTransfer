namespace SmartQuickTransfer.Core
{
    internal enum AttemptOutcome
    {
        FullSuccess,
        PartialSuccess,
        NothingMoved
    }

    /// <summary>
    /// Structured result of trying to move an item into one scored candidate. Used by the
    /// multi-candidate loop to decide whether to try the next target (only when NOTHING moved),
    /// stop on a partial move, or record a prediction on a full move.
    /// </summary>
    internal sealed class TransferAttemptResult
    {
        internal AttemptOutcome Outcome;
        internal int RequestedCount;
        internal int MovedCount;
        internal TargetCandidate Candidate;

        internal bool IsPartial
        {
            get { return Outcome == AttemptOutcome.PartialSuccess; }
        }
    }
}
