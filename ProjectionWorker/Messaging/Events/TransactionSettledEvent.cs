namespace ProjectionWorker.Messaging.Events;

public record TransactionSettledEvent(
    Guid TransactionId,
    Guid AccountId,
    decimal NewBalance,
    string CorrelationId,
    DateTime OccurredAtUtc
);
