using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using SettlementWorker.Messaging;
using SettlementWorker.Messaging.Events;
using SettlementWorker.Persistence;
using SettlementWorker.Services;
using System.Text.Json;

namespace SettlementWorker;

public sealed class SettlementConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SettlementConsumerWorker> _logger;
    private readonly IKafkaProducer _producer;

    public SettlementConsumerWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SettlementConsumerWorker> logger, IKafkaProducer producer)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _producer = producer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _config["Kafka:BootstrapServers"],
            GroupId = _config["Kafka:GroupId"] ?? "settlement-worker",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        var authorizedTopic = _config["Kafka:Topics:Authorized"] ?? "transaction-authorized";
        var settledTopic = _config["Kafka:Topics:Settled"] ?? "transaction-settled";
        var dltTopic = _config["Kafka:Topics:DeadLetter"] ?? "transaction-dead-letter";

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(authorizedTopic);

        _logger.LogInformation("SettlementWorker consuming {Topic}", authorizedTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);
                if (cr?.Message?.Value is null) continue;

                var evt = JsonSerializer.Deserialize<TransactionAuthorizedEvent>(cr.Message.Value);
                if (evt is null) throw new InvalidOperationException("Invalid event payload.");

                var handled = ProcessWithRetryAsync(evt, settledTopic, dltTopic, stoppingToken)
                    .GetAwaiter().GetResult();

                if (handled)
                    consumer.Commit(cr);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consume loop error");
                Thread.Sleep(500);
            }
        }

        consumer.Close();
        return Task.CompletedTask;
    }

    private async Task<bool> ProcessWithRetryAsync(TransactionAuthorizedEvent evt, string settledTopic, string dltTopic, CancellationToken ct)
    {
        var maxAttempts = 5;
        var baseDelayMs = 250;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await ProcessOnceAsync(evt, settledTopic, ct);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Concurrency conflict attempt {Attempt}/{Max} TxId={TxId}", attempt, maxAttempts, evt.TransactionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Settlement failed attempt {Attempt}/{Max} TxId={TxId}", attempt, maxAttempts, evt.TransactionId);
            }

            if (attempt == maxAttempts)
            {
                var dltPayload = JsonSerializer.Serialize(new
                {
                    reason = "Settlement failed after retries",
                    evt.TransactionId,
                    evt.AccountId,
                    evt.Amount,
                    evt.Type,
                    evt.CorrelationId,
                    evt.OccurredAtUtc,
                    occurredAtUtc = DateTime.UtcNow
                });

                await _producer.ProduceAsync(dltTopic,
                                             evt.TransactionId.ToString("N"),
                                             dltPayload,
                                             new Dictionary<string, string> { ["CorrelationId"] = evt.CorrelationId, ["EventType"] = "SettlementFailed" },
                                             ct);

                _logger.LogError("Sent to DLT TxId={TxId}", evt.TransactionId);
                return true;
            }

            var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
            await Task.Delay(delay, ct);
        }

        return false;
    }

    private async Task ProcessOnceAsync(TransactionAuthorizedEvent evt, string settledTopic, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();

        await settlementService.ProcessAsync(evt, settledTopic, ct);
    }

}
