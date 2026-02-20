namespace BuildingBlocks.Messaging.Events;

public record TransactionSettledEvent(
    Guid TransactionId,
    Guid AccountId,
    decimal CurrentBalance,
    string CorrelationId,
    DateTime OccurredAtUtc
);