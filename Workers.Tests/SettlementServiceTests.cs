using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using BuildingBlocks.Domain.Entities;
using SettlementWorker.Services;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Domain.Enums;

namespace Workers.Tests;

public class SettlementServiceTests
{

    [Fact]
    public async Task ProcessAsync_ShouldDebitAccountAndSetTransactionToSettled()
    {
        await using var db = DbContextFactory.Create();

        var account = new Account
        {
            Id = Guid.NewGuid(),
            HolderName = "User",
            Balance = 20000,
            CreatedAt = DateTime.UtcNow
        };

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = 50,
            Type = TransactionType.Debit,
            Status = TransactionStatus.Authorized, 
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(account);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var producerMock = new Mock<IKafkaProducer>();
        var loggerMock = new Mock<ILogger<SettlementService>>();

        var service = new SettlementService(db, producerMock.Object, loggerMock.Object);

        var evt = new TransactionAuthorizedEvent(
            TransactionId: transaction.Id,
            AccountId: account.Id,
            Amount: 50,
            Type: "Debit",
            CorrelationId: "test-correlation",
            OccurredAtUtc: DateTime.UtcNow
        );

        await service.ProcessAsync(evt, KafkaTopics.TransactionSettled, CancellationToken.None);

        var updatedAccount = await db.Accounts.FirstAsync(a => a.Id == account.Id);
        updatedAccount.Balance.Should().Be(19950);

        var updatedTx = await db.Transactions.FirstAsync(t => t.Id == transaction.Id);
        updatedTx.Status.Should().Be(TransactionStatus.Authorized); 

        producerMock.Verify(p =>
            p.ProduceAsync(
                KafkaTopics.TransactionSettled,
                evt.TransactionId.ToString("N"),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
