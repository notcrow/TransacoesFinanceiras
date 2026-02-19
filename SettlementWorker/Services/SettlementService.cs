using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SettlementWorker.Messaging;
using SettlementWorker.Messaging.Events;
using SettlementWorker.Persistence;
using SettlementWorker.Persistence.Entities;

namespace SettlementWorker.Services
{
    public sealed class SettlementService : ISettlementService
    {
        private readonly AppDbContext _db;
        private readonly IKafkaProducer _producer;
        private readonly ILogger<SettlementService> _logger;

        public SettlementService(
            AppDbContext db,
            IKafkaProducer producer,
            ILogger<SettlementService> logger)
        {
            _db = db;
            _producer = producer;
            _logger = logger;
        }

        public async Task ProcessAsync(TransactionAuthorizedEvent evt, string settledTopic, CancellationToken ct)
        {
            // Carrega transação
            var tx = await _db.Transactions
                .FirstOrDefaultAsync(t => t.Id == evt.TransactionId, ct)
                ?? throw new InvalidOperationException($"Transaction not found {evt.TransactionId}");

            const int SettledStatus = 4;

            // Idempotência: já liquidada → sai sem fazer nada
            if (tx.Status == SettledStatus)
            {
                _logger.LogInformation("Transaction already settled {TxId}", evt.TransactionId);
                return;
            }

            // Carrega conta
            var account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Id == evt.AccountId, ct)
                ?? throw new InvalidOperationException($"Account not found {evt.AccountId}");

            // Calcula novo saldo
            var isDebit = string.Equals(evt.Type, "Debit", StringComparison.OrdinalIgnoreCase);
            var delta = isDebit ? -evt.Amount : evt.Amount;

            account.Balance += delta;
            tx.Status = SettledStatus;
            tx.UpdatedAt = DateTime.UtcNow;

            if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                // InMemory não suporta BeginTransaction, só salva
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
                await _db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }

            // Publica evento TransactionSettled
            var settledEvt = new TransactionSettledEvent(
                evt.TransactionId,
                evt.AccountId,
                account.Balance,
                evt.CorrelationId,
                DateTime.UtcNow);

            var payload = JsonSerializer.Serialize(settledEvt);

            await _producer.ProduceAsync(
                settledTopic,
                evt.TransactionId.ToString("N"),
                payload,
                new Dictionary<string, string>
                {
                    ["CorrelationId"] = evt.CorrelationId,
                    ["EventType"] = "TransactionSettled"
                },
                ct);

            _logger.LogInformation(
                "Transaction settled {TxId}, Account={AccountId}, NewBalance={Balance}",
                evt.TransactionId, evt.AccountId, account.Balance);
        }
    }
}
