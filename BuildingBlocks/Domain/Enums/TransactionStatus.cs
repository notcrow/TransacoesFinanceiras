namespace BuildingBlocks.Domain.Enums;

public enum TransactionStatus
{
    Pending = 1,
    Authorized = 2,
    Rejected = 3,
    Settled = 4,
    PendingReview = 5
}
