using Confluent.Kafka;
using BuildingBlocks.Messaging.Events;
using SettlementWorker.Services;
using System.Text.Json;
using BuildingBlocks.Messaging;

namespace SettlementWorker;

public sealed class SettlementConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SettlementConsumer> _logger;
    private readonly IConfiguration _configuration;
    private IConsumer<string, string>? _consumer;
    private string _settledTopic = "transaction-settled";

    public SettlementConsumer(
        IServiceProvider serviceProvider,
        ILogger<SettlementConsumer> logger,
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
            GroupId = _configuration["Kafka:GroupId"] ?? "settlement-worker",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        _consumer = new ConsumerBuilder<string, string>(cfg).Build();

        var authorizedTopic =
            _configuration["Kafka:Topics:TransactionAuthorized"] ?? KafkaTopics.TransactionAuthorized;

        _settledTopic =
            _configuration["Kafka:Topics:TransactionSettled"] ?? KafkaTopics.TransactionSettled;

        _consumer.Subscribe(authorizedTopic);

        _logger.LogInformation(
            "SettlementConsumer started. Subscribed to {AuthorizedTopic}, will publish to {SettledTopic}",
            authorizedTopic,
            _settledTopic);

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

                _logger.LogInformation(
                    "Message received. Topic={Topic}, Partition={Partition}, Offset={Offset}, Key={Key}",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Key);

                TransactionAuthorizedEvent? evt = null;

                try
                {
                    evt = JsonSerializer.Deserialize<TransactionAuthorizedEvent>(result.Message.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to deserialize TransactionAuthorizedEvent. Payload={Payload}",
                        result.Message.Value);
                }

                if (evt is not null)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();

                    await ProcessWithRetry(settlementService, evt, stoppingToken);
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
                _logger.LogError(ex, "Unexpected error in SettlementConsumer loop");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ProcessWithRetry(
        ISettlementService settlementService,
        TransactionAuthorizedEvent evt,
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
                await settlementService.ProcessAsync(evt, _settledTopic, ct);

                _logger.LogInformation(
                    "Transaction {TransactionId} settled successfully on attempt {Attempt}",
                    evt.TransactionId,
                    attempt);

                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Error processing transaction {TransactionId}, attempt {Attempt}. Retrying...",
                    evt.TransactionId,
                    attempt);

                await Task.Delay(delay, ct);
                delay = delay * 2;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing transaction {TransactionId} after {Attempts} attempts.",
                    evt.TransactionId,
                    attempt);
                return;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SettlementConsumer stopping...");

        _consumer?.Close();
        _consumer?.Dispose();

        return base.StopAsync(cancellationToken);
    }
}