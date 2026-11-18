namespace SmartQuickTransfer.Core
{
    internal enum TransferOutcome
    {
        NotHandled,
        MovedToWindow,
        MovedToFallback,
        RejectedByTarget,
        Failed
    }
}
