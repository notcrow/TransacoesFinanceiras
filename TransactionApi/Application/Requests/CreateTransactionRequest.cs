namespace TransactionApi.Application.Requests;

/// <summary>
/// Request de criação de transação financeira.
/// </summary>
public record CreateTransactionRequest(Guid AccountId,decimal Amount,string Type);
