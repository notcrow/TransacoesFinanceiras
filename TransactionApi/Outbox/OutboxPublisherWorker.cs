using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Kafka;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Persistence;

namespace TransactionApi.Outbox
{
    public sealed class OutboxPublisherWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IKafkaProducer _producer;
        private readonly ILogger<OutboxPublisherWorker> _logger;
        private readonly IConfiguration _config;

        public OutboxPublisherWorker(
            IServiceScopeFactory scopeFactory,
            IKafkaProducer producer,
            IConfiguration config,
            ILogger<OutboxPublisherWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _producer = producer;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxPublisherWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PublishPendingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OutboxPublisherWorker failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            _logger.LogInformation("OutboxPublisherWorker stopping.");
        }

        private async Task PublishPendingAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            const int batchSize = 20;

            var pending = await db.Outbox
                .Where(x => !x.Processed)
                .OrderBy(x => x.OccurredAt)
                .Take(batchSize)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return;

            foreach (var msg in pending)
            {
                ct.ThrowIfCancellationRequested();

                var topic = ResolveTopic(msg.EventType);
                var dltTopic = ResolveDeadLetterTopic();

                var correlationId = TryExtractCorrelationId(msg.Payload) ?? msg.Id.ToString("N");

                var headers = new Dictionary<string, string>
                {
                    [KafkaHeaders.CorrelationId] = correlationId,
                    [KafkaHeaders.EventType] = msg.EventType
                };

                var published = await TryPublishWithRetryAsync(topic, msg, headers, ct);

                if (!published)
                {
                    var dltPayload = JsonSerializer.Serialize(new
                    {
                        originalEventType = msg.EventType,
                        originalOutboxId = msg.Id,
                        occurredAtUtc = msg.OccurredAt,
                        payload = JsonSerializer.Deserialize<object>(msg.Payload)
                    });

                    _logger.LogWarning(
                        "Sending message to DLT. OutboxId={OutboxId} EventType={EventType}",
                        msg.Id, msg.EventType);

                    var dltHeaders = new Dictionary<string, string>(headers)
                    {
                        ["IsDeadLetter"] = "true"
                    };

                    var dltOk = await TryPublishWithRetryAsync(dltTopic, msg, dltHeaders, ct, overridePayload: dltPayload);

                    if (!dltOk)
                    {
                        _logger.LogError(
                            "Failed to publish to DLT. Keeping message unprocessed. OutboxId={OutboxId}",
                            msg.Id);
                        continue; // não marca Processed, tenta de novo no futuro
                    }
                }

                msg.Processed = true;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Outbox processed. OutboxId={OutboxId} EventType={EventType} Topic={Topic}",
                    msg.Id, msg.EventType, published ? topic : dltTopic);
            }
        }

        private string ResolveTopic(string eventType)
        {
            var topic = _config[$"Kafka:Topics:{eventType}"];

            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new InvalidOperationException(
                    $"No Kafka topic configured for EventType '{eventType}'. Configure Kafka:Topics:{eventType}.");
            }

            return topic;
        }

        private string ResolveDeadLetterTopic()
        {
            return _config["Kafka:Topics:DeadLetter"] ?? KafkaTopics.DeadLetter;
        }

        private static string? TryExtractCorrelationId(string jsonPayload)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonPayload);

                if (doc.RootElement.TryGetProperty(KafkaHeaders.CorrelationId, out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    return p.GetString();
                }
            }
            catch
            {
                // ignore parse errors
            }

            return null;
        }

        private async Task<bool> TryPublishWithRetryAsync(
            string topic,
            OutboxMessage msg,
            IDictionary<string, string> headers,
            CancellationToken ct,
            string? overridePayload = null)
        {
            var payload = overridePayload ?? msg.Payload;
            var key = msg.Id.ToString("N");

            const int maxAttempts = 5;
            const int baseDelayMs = 250;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await _producer.ProduceAsync(topic, key, payload, headers, ct);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Publish failed. Attempt={Attempt}/{Max} Topic={Topic} OutboxId={OutboxId}",
                        attempt, maxAttempts, topic, msg.Id);

                    if (attempt == maxAttempts)
                        return false;

                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(delay, ct);
                }
            }

            return false;
        }
    }
}