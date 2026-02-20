using BuildingBlocks.Domain.Enums;

namespace TransactionApi.Application.Responses;

/// <summary>
/// Resposta da criação de transação.
/// </summary>
public record CreateTransactionResponse(Guid TransactionId,TransactionStatus Status);
