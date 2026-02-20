using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Domain.Entities;

namespace TransactionApi.Application.Business;

public enum TransactionErrorType
{
    None,
    Validation,
    NotFound,
    BusinessRule
}

public sealed class TransactionResult
{
    public bool Success { get; init; }
    public TransactionErrorType ErrorType { get; init; }
    public string? ErrorMessage { get; init; }

    public Transaction? Transaction { get; init; }
    public OutboxMessage? Outbox { get; init; }

    public static TransactionResult Ok(Transaction transaction, OutboxMessage? outbox = null)
        => new() { Success = true, Transaction = transaction, Outbox = outbox };

    public static TransactionResult ValidationError(string message)
        => new() { Success = false, ErrorType = TransactionErrorType.Validation, ErrorMessage = message };

    public static TransactionResult NotFound(string message)
        => new() { Success = false, ErrorType = TransactionErrorType.NotFound, ErrorMessage = message };

    public static TransactionResult BusinessError(string message)
        => new() { Success = false, ErrorType = TransactionErrorType.BusinessRule, ErrorMessage = message };
}