using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using TransactionApi.Application.Business;
using TransactionApi.Application.Requests;
using TransactionApi.Application.Responses;

namespace TransactionApi.Controllers;

[ApiController]
[Route("transactions")]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITransactionsBusiness _transactionBusiness;

    public TransactionsController(AppDbContext db, ITransactionsBusiness transactionsBusiness)
    {
        _db = db;
        _transactionBusiness = transactionsBusiness;
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
        var result = await _transactionBusiness.CreateTransactionAsync(request, ct);

        if (!result.Success)
        {
            return result.ErrorType switch
            {
                TransactionErrorType.Validation => BadRequest(result.ErrorMessage),
                TransactionErrorType.NotFound => NotFound(result.ErrorMessage),
                _ => StatusCode(StatusCodes.Status500InternalServerError, result.ErrorMessage)
            };
        }

        // Persistência com outbox
        _db.Transactions.Add(result.Transaction!);
        if (result.Outbox != null) _db.Outbox.Add(result.Outbox);

        await _db.SaveChangesAsync(ct);
        return Ok(new CreateTransactionResponse(result.Transaction!.Id, result.Transaction.Status));
    }
}
