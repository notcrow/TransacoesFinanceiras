using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Enums;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Outbox;
using TransactionApi.Application.Requests;
using TransactionApi.Application.Responses;

namespace TransactionApi.Application.Business;

public interface ITransactionsBusiness
{
    Task<TransactionResult> CreateTransactionAsync(
        CreateTransactionRequest request,
        CancellationToken ct);
}

public sealed class TransactionsBusiness : ITransactionsBusiness
{
    private readonly AppDbContext _db;

    public TransactionsBusiness(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TransactionResult> CreateTransactionAsync(CreateTransactionRequest request,CancellationToken ct)
    {
        if (request.Amount <= 0)
            return TransactionResult.ValidationError("O valor deve ser maior que 0.");

        if (!Enum.TryParse<TransactionType>(request.Type, true, out var type))
            return TransactionResult.ValidationError("Tipo inválido (Debit ou Credit).");

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);

        if (account is null)
            return TransactionResult.NotFound("Conta não encontrada.");

        var status = TransactionStatus.Authorized;

        if (type == TransactionType.Debit)
        {
            if (account.Balance < request.Amount)
                return TransactionResult.ValidationError("Saldo insuficiente.");

            if (request.Amount >= 10000m)
                status = TransactionStatus.PendingReview;
        }

        var now = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = request.Amount,
            Type = type,
            Status = status,
            CreatedAt = now
        };

        OutboxMessage? outbox = null;

        if (status == TransactionStatus.Authorized)
        {
            var evt = new TransactionAuthorizedEvent(
                TransactionId: transaction.Id,
                AccountId: account.Id,
                Amount: transaction.Amount,
                Type: transaction.Type.ToString(),
                CorrelationId: Guid.NewGuid().ToString("N"),
                OccurredAtUtc: now);

            outbox = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = nameof(TransactionAuthorizedEvent),
                Payload = JsonSerializer.Serialize(evt),
                OccurredAt = now,
                Processed = false
            };
        }

        return TransactionResult.Ok(transaction, outbox);
    }
}