using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Projections;
using Confluent.Kafka;
using System.Text.Json;

namespace ProjectionWorker;

public sealed class ProjectionConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectionConsumer> _logger;
    private readonly IConfiguration _configuration;
    private IConsumer<string, string>? _consumer;

    public ProjectionConsumer(
        IServiceProvider serviceProvider,
        ILogger<ProjectionConsumer> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"],
            GroupId = _configuration["Kafka:GroupId"] ?? "projection-worker",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        _consumer = new ConsumerBuilder<string, string>(cfg).Build();

        var topic =
            _configuration["Kafka:Topics:TransactionSettled"] ?? KafkaTopics.TransactionSettled;

        _consumer.Subscribe(topic);

        _logger.LogInformation(
            "ProjectionConsumer started. Subscribed to topic {Topic}",
            topic);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_consumer is null)
        {
            _logger.LogError("Kafka consumer not initialized.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = _consumer.Consume(stoppingToken);
                if (result is null)
                    continue;

                TransactionSettledEvent? evt = null;

                try
                {
                    evt = JsonSerializer.Deserialize<TransactionSettledEvent>(result.Message.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error deserializing TransactionSettledEvent. Payload={Payload}",
                        result.Message.Value);
                }

                if (evt is not null)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var projectionService =
                        scope.ServiceProvider.GetRequiredService<IAccountStatementProjectionService>();

                    await ProcessWithRetry(projectionService, evt, stoppingToken);
                }

                _consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ProjectionConsumer loop");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ProcessWithRetry(
        IAccountStatementProjectionService projectionService,
        TransactionSettledEvent evt,
        CancellationToken ct)
    {
        var maxAttempts = 3;
        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(200);

        while (attempt < maxAttempts && !ct.IsCancellationRequested)
        {
            attempt++;

            try
            {
                await projectionService.ApplyAsync(evt, ct);

                _logger.LogInformation(
                    "Projection applied for Transaction {TransactionId} (Attempt={Attempt})",
                    evt.TransactionId,
                    attempt);

                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Error applying projection for Transaction {TransactionId}, attempt {Attempt}. Retrying...",
                    evt.TransactionId,
                    attempt);

                await Task.Delay(delay, ct);
                delay = delay * 2;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error applying projection for Transaction {TransactionId} after {Attempts} attempts.",
                    evt.TransactionId,
                    attempt);
                return;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ProjectionConsumer stopping...");

        _consumer?.Close();
        _consumer?.Dispose();

        return base.StopAsync(cancellationToken);
    }
}