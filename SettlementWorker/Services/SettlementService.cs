using BuildingBlocks.Domain.Enums;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        public async Task ProcessAsync(TransactionAuthorizedEvent evt,string settledTopic,CancellationToken ct)
        {
            var tx = await _db.Transactions
                .FirstOrDefaultAsync(t => t.Id == evt.TransactionId, ct)
                ?? throw new InvalidOperationException(
                    $"Transaction not found {evt.TransactionId}");

            if (tx.Status == TransactionStatus.Settled)
            {
                _logger.LogInformation(
                    "Transaction already settled {TxId}",
                    evt.TransactionId);
                return;
            }

            var account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Id == evt.AccountId, ct)
                ?? throw new InvalidOperationException(
                    $"Account not found {evt.AccountId}");

            var isDebit = string.Equals(
                evt.Type,
                "Debit",
                StringComparison.OrdinalIgnoreCase);

            var delta = isDebit ? -evt.Amount : evt.Amount;

            account.Balance += delta;
            tx.Status = TransactionStatus.Settled;
            tx.UpdatedAt = DateTime.UtcNow;

            if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
                await _db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }

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
                    [KafkaHeaders.CorrelationId] = evt.CorrelationId,
                    [KafkaHeaders.EventType] = "TransactionSettled"
                },
                ct);

            _logger.LogInformation(
                "Transaction settled {TxId}, Account={AccountId}, NewBalance={Balance}",
                evt.TransactionId,
                evt.AccountId,
                account.Balance);
        }
    }
}