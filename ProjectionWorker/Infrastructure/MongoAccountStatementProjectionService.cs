using MongoDB.Driver;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Projections;

namespace ProjectionWorker.Infrastructure;

public class MongoAccountStatementProjectionService : IAccountStatementProjectionService
{
    private readonly MongoContext _context;

    public MongoAccountStatementProjectionService(MongoContext context)
    {
        _context = context;
    }

    public async Task ApplyAsync(TransactionSettledEvent evt, CancellationToken ct)
    {
        var collection = _context.AccountStatements;

        var existing = await collection
            .Find(x => x.AccountId == evt.AccountId)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            var newStatement = new AccountStatement
            {
                AccountId = evt.AccountId,
                CurrentBalance = evt.CurrentBalance,
                LastUpdated = DateTime.UtcNow,
                Transactions =
                {
                    new AccountStatementTransaction
                    {
                        TransactionId = evt.TransactionId,
                        BalanceAfter = evt.CurrentBalance,
                        ProcessedAt = DateTime.UtcNow
                    }
                }
            };

            await collection.InsertOneAsync(newStatement, cancellationToken: ct);
            return;
        }

        // ✅ Idempotência: se a transação já está registrada, não faz nada
        if (existing.Transactions.Any(t => t.TransactionId == evt.TransactionId))
            return;

        var update = Builders<AccountStatement>.Update
            .Set(x => x.CurrentBalance, evt.CurrentBalance)
            .Set(x => x.LastUpdated, DateTime.UtcNow)
            .Push(x => x.Transactions, new AccountStatementTransaction
            {
                TransactionId = evt.TransactionId,
                BalanceAfter = evt.CurrentBalance,
                ProcessedAt = DateTime.UtcNow
            });

        await collection.UpdateOneAsync(
            x => x.AccountId == evt.AccountId,
            update,
            cancellationToken: ct);
    }
}