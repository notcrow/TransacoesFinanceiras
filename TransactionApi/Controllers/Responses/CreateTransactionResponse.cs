using TransactionApi.Domain.Enums;

namespace TransactionApi.Controllers.Responses;

/// <summary>
/// Resposta da criação de transação.
/// </summary>
public record CreateTransactionResponse(Guid TransactionId,TransactionStatus Status);
