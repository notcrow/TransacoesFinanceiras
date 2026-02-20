using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TransactionApi.Infrastructure.Persistence;
using TransactionApi.Outbox.Messaging;
using TransactionApi.Outbox.Messaging.Events;

namespace TransactionApi.Outbox
{
    public sealed class OutboxProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IKafkaProducer _producer;
        private readonly ILogger<OutboxProcessor> _logger;
        private readonly IConfiguration _configuration;

        public OutboxProcessor(
            IServiceProvider serviceProvider,
            IKafkaProducer producer,
            ILogger<OutboxProcessor> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _producer = producer;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var authorizedTopic =
                _configuration["Kafka:Topics:TransactionAuthorized"] ?? "transaction-authorized";
            var deadLetterTopic =
                _configuration["Kafka:Topics:DeadLetter"] ?? "transaction-dead-letter";

            _logger.LogInformation("OutboxProcessor iniciado. Topic={Topic}", authorizedTopic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var pending = await db.Outbox
                        .Where(o => !o.Processed)
                        .OrderBy(o => o.OccurredAt)
                        .Take(50)
                        .ToListAsync(stoppingToken);

                    if (pending.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    foreach (var message in pending)
                    {
                        await ProcessMessageAsync(
                            db,
                            message,
                            authorizedTopic,
                            deadLetterTopic,
                            stoppingToken);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro inesperado no loop do OutboxProcessor");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("OutboxProcessor finalizado.");
        }

        private async Task ProcessMessageAsync(
            AppDbContext db,
            OutboxMessage message,
            string authorizedTopic,
            string deadLetterTopic,
            CancellationToken ct)
        {
            var attempt = 0;
            var delay = TimeSpan.FromMilliseconds(200);

            while (attempt < 3 && !ct.IsCancellationRequested)
            {
                attempt++;

                try
                {
                    var headers = new Dictionary<string, string>
                    {
                        ["EventType"] = message.EventType
                    };

                    string key;

                    if (message.EventType == "TransactionAuthorized")
                    {
                        var evt = JsonSerializer.Deserialize<TransactionAuthorizedEvent>(message.Payload);

                        if (evt is not null)
                        {
                            key = evt.TransactionId.ToString("N");

                            if (!string.IsNullOrWhiteSpace(evt.CorrelationId))
                                headers["CorrelationId"] = evt.CorrelationId;
                        }
                        else
                        {
                            key = message.Id.ToString("N");
                        }
                    }
                    else
                    {
                        key = message.Id.ToString("N");
                    }

                    await _producer.ProduceAsync(
                        authorizedTopic,
                        key,
                        message.Payload,
                        headers,
                        ct);

                    message.Processed = true;

                    _logger.LogInformation(
                        "Outbox {MessageId} enviada para Kafka em {Topic} (Tentativa={Attempt})",
                        message.Id,
                        authorizedTopic,
                        attempt);

                    return;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    _logger.LogWarning(
                        ex,
                        "Erro publicando Outbox {MessageId}, tentativa {Attempt}. Retentando...",
                        message.Id,
                        attempt);

                    await Task.Delay(delay, ct);
                    delay = delay * 2;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Erro publicando Outbox {MessageId} após {Attempts} tentativas. Enviando para DLQ...",
                        message.Id,
                        attempt);

                    await SendToDeadLetterAsync(message, deadLetterTopic, ct);
                    message.Processed = true;
                    return;
                }
            }
        }

        private async Task SendToDeadLetterAsync(
            OutboxMessage message,
            string deadLetterTopic,
            CancellationToken ct)
        {
            try
            {
                var headers = new Dictionary<string, string>
                {
                    ["OriginalEventType"] = message.EventType,
                    ["Source"] = "TransactionApi.Outbox"
                };

                await _producer.ProduceAsync(
                    deadLetterTopic,
                    message.Id.ToString("N"),
                    message.Payload,
                    headers,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Falha ao enviar mensagem {MessageId} para DLQ {Topic}",
                    message.Id,
                    deadLetterTopic);
            }
        }
    }
}