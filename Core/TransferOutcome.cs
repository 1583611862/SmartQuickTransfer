namespace SmartQuickTransfer.Core
{
    internal enum TransferOutcome
    {
        NotHandled,
        MovedToWindow,
        MovedToCustomerDesk,
        MovedToFallback,
        RejectedByTarget,
        Failed
    }
}
