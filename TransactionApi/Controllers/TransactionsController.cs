using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransactionApi.Controllers.Requests;
using TransactionApi.Controllers.Responses;
using TransactionApi.Domain.Entities;
using TransactionApi.Domain.Enums;
using TransactionApi.Infrastructure.Persistence;
using TransactionApi.Messaging.Events;
using TransactionApi.Outbox;

namespace TransactionApi.Controllers;

[ApiController]
[Route("transactions")]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Cria uma nova transação financeira.
    /// </summary>
    /// <remarks>
    /// A transação é persistida no PostgreSQL, registrada na Outbox e publicada no Kafka.
    /// Dependendo do valor e saldo disponível, a transação pode ser autorizada, rejeitada
    /// ou marcada como pendente de revisão.
    /// </remarks>
    /// <param name="request">Dados da transação a ser criada.</param>
    /// <param name="ct">Token de cancelamento da requisição HTTP.</param>
    /// <response code="200">Transação criada com sucesso (autorizada ou pendente).</response>
    /// <response code="400">Requisição inválida ou saldo insuficiente.</response>
    /// <response code="500">Erro inesperado no processamento.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreateTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateTransactionResponse>> Create([FromBody] CreateTransactionRequest request,CancellationToken ct)
    {
        if (request.Amount <= 0)
            return BadRequest("O valor da Transação deve ser maior que 0.");

        if (!Enum.TryParse<TransactionType>(request.Type, ignoreCase: true, out var type))
            return BadRequest("O tipo de Transação deve ser 'Debit' ou 'Credit'.");

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
        if (account is null)
            return NotFound("Conta não encontrada.");

        var status = TransactionStatus.Authorized;

        if (type == TransactionType.Debit)
        {
            if (account.Balance - request.Amount < 0)
                return BadRequest("Saldo Insuficiente para Transação.");

            if (request.Amount > 10_000m)
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
            CreatedAt = now,
            UpdatedAt = now
        };

        var correlationId = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

        OutboxMessage? outbox = null;

        if (status == TransactionStatus.Authorized)
        {
            var evt = new TransactionAuthorizedEvent(TransactionId: transaction.Id,
                                                     AccountId: transaction.AccountId,
                                                     Amount: transaction.Amount,
                                                     Type: transaction.Type.ToString(),
                                                     CorrelationId: correlationId,
                                                     OccurredAtUtc: now);

            outbox = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "TransactionAuthorized",
                Payload = JsonSerializer.Serialize(evt),
                OccurredAt = now,
                Processed = false
            };
        }

        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            _db.Transactions.Add(transaction);
            if (outbox is not null)
                _db.Outbox.Add(outbox);

            await _db.SaveChangesAsync(ct);
        }
        else
        {
            await using var dbTransaction = await _db.Database.BeginTransactionAsync(ct);

            _db.Transactions.Add(transaction);
            if (outbox is not null)
                _db.Outbox.Add(outbox);

            await _db.SaveChangesAsync(ct);
            await dbTransaction.CommitAsync(ct);
        }

        return Ok(new CreateTransactionResponse(transaction.Id, transaction.Status));

    }
}
