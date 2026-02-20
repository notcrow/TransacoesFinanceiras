using ProjectionWorker.Messaging.Events;

namespace ProjectionWorker.Projections;

public interface IAccountStatementProjectionService
{
    /// <summary>
    /// Aplica o evento de liquidação na projeção da conta (idempotente).
    /// </summary>
    Task ApplyAsync(TransactionSettledEvent evt, CancellationToken ct);
}