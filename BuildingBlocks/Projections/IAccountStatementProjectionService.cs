using BuildingBlocks.Messaging.Events;

namespace BuildingBlocks.Projections;

public interface IAccountStatementProjectionService
{
    /// <summary>
    /// Aplica o evento de liquidação na projeção da conta (idempotente).
    /// </summary>
    Task ApplyAsync(TransactionSettledEvent evt, CancellationToken ct);
}