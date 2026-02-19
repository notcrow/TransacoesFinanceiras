namespace SettlementWorker.Messaging.Events;

public record TransactionSettledEvent(
    Guid TransactionId,
    Guid AccountId,
    decimal NewBalance,
    string CorrelationId,
    DateTime OccurredAtUtc
);
