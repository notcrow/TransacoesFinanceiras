using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Enums;
using BuildingBlocks.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TransactionApi.Application.Responses;
using TransactionApi.Controllers;
using TransactionApi.Application.Requests;
using TransactionApi.Application.Business;

namespace TransactionApi.Tests.Controllers
{
    public class TransactionsControllerTests
    {
        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenAmountIsZeroOrNegative()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);
            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(AccountId: Guid.NewGuid(),
                                                       Amount: 0m,
                                                       Type: "Debit");

            var result = await controller.Create(request, CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenTransactionTypeIsInvalid()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);
            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(AccountId: Guid.NewGuid(),
                                                       Amount: 50m,
                                                       Type: "INVALID");

            var result = await controller.Create(request, CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnNotFound_WhenAccountDoesNotExist()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);
            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(AccountId: Guid.NewGuid(),
                                                       Amount: 50m,
                                                       Type: "Debit");

            var result = await controller.Create(request, CancellationToken.None);

            result.Result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task Create_ShouldReturnBadRequest_WhenInsufficientBalance()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 10m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(
                AccountId: account.Id,
                Amount: 50m,
                Type: "Debit");

            var result = await controller.Create(request, CancellationToken.None);

            result.Result.Should().BeOfType<BadRequestObjectResult>();

            db.Transactions.Should().BeEmpty();
            db.Outbox.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ShouldCreateTransactionAndOutbox_WhenValidDebit()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 20_000m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(AccountId: account.Id,
                                                       Amount: 50m,
                                                       Type: "Debit");

            var result = await controller.Create(request, CancellationToken.None);

            var ok = result.Result as OkObjectResult;
            ok.Should().NotBeNull();

            var response = ok!.Value as CreateTransactionResponse;
            response.Should().NotBeNull();
            response!.Status.Should().Be(TransactionStatus.Authorized);

            db.Transactions.Should().HaveCount(1);
            db.Outbox.Should().HaveCount(1);
        }

        [Fact]
        public async Task Create_ShouldSetStatusPendingReview_WhenHighValueDebit()
        {
            await using var db = DbContextFactory.Create();
            var transactionBusiness = new TransactionsBusiness(db);

            var account = new Account
            {
                Id = Guid.NewGuid(),
                HolderName = "User",
                Balance = 50_000m,
                CreatedAt = DateTime.UtcNow
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var controller = new TransactionsController(db, transactionBusiness);

            var request = new CreateTransactionRequest(AccountId: account.Id,
                                                       Amount: 20_000m, 
                                                       Type: "Debit");

            var result = await controller.Create(request, CancellationToken.None);

            var ok = result.Result as OkObjectResult;
            ok.Should().NotBeNull();

            var response = ok!.Value as CreateTransactionResponse;
            response.Should().NotBeNull();
            response!.Status.Should().Be(TransactionStatus.PendingReview);
        }
    }
}
