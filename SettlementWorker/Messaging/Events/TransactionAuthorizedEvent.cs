namespace SettlementWorker.Messaging.Events;

public record TransactionAuthorizedEvent(
    Guid TransactionId,
    Guid AccountId,
    decimal Amount,
    string Type,
    string CorrelationId,
    DateTime OccurredAtUtc
);
