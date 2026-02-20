using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TransactionApi.Controllers;
using TransactionApi.Controllers.Requests;
using TransactionApi.Controllers.Responses;
using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Enums;
using BuildingBlocks.Infrastructure;
using Xunit;

namespace TransactionApi.Tests.Controllers
{
    public class TransactionsControllerTests
    {
        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenAmountIsZeroOrNegative()
        {
            // Arrange
            await using var db = DbContextFactory.Create();
            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: Guid.NewGuid(),
                Amount: 0m,
                Type: "Debit");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert
            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenTransactionTypeIsInvalid()
        {
            // Arrange
            await using var db = DbContextFactory.Create();
            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: Guid.NewGuid(),
                Amount: 50m,
                Type: "INVALID");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert
            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnNotFound_WhenAccountDoesNotExist()
        {
            // Arrange
            await using var db = DbContextFactory.Create();
            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: Guid.NewGuid(), // não existe
                Amount: 50m,
                Type: "Debit");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert
            result.Result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenInsufficientBalance()
        {
            // Arrange
            await using var db = DbContextFactory.Create();

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 10m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: account.Id,
                Amount: 50m,   // maior que o saldo
                Type: "Debit");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert
            result.Result.Should().BeOfType<BadRequestObjectResult>();

            db.Transactions.Should().BeEmpty();
            db.Outbox.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ShouldCreateTransactionAndOutbox_WhenValidDebit()
        {
            // Arrange
            await using var db = DbContextFactory.Create();

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 20_000m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: account.Id,
                Amount: 50m,
                Type: "Debit");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert HTTP
            var ok = result.Result as OkObjectResult;
            ok.Should().NotBeNull();

            var response = ok!.Value as CreateTransactionResponse;
            response.Should().NotBeNull();
            response!.Status.Should().Be(TransactionStatus.Authorized);

            // Assert banco
            db.Transactions.Should().HaveCount(1);
            db.Outbox.Should().HaveCount(1);
        }

        [Fact]
        public async Task Create_ShouldSetStatusPendingReview_WhenHighValueDebit()
        {
            // Arrange
            await using var db = DbContextFactory.Create();

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 50_000m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db);

            var request = new CreateTransactionRequest(
                AccountId: account.Id,
                Amount: 20_000m,   // > 10_000
                Type: "Debit");

            // Act
            var result = await controller.Create(request, CancellationToken.None);

            // Assert
            var ok = result.Result as OkObjectResult;
            ok.Should().NotBeNull();

            var response = ok!.Value as CreateTransactionResponse;
            response.Should().NotBeNull();
            response!.Status.Should().Be(TransactionStatus.PendingReview);
        }
    }
}
