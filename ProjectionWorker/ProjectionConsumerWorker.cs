using System.Text.Json;
using Confluent.Kafka;
using MongoDB.Driver;
using ProjectionWorker.Messaging.Events;
using ProjectionWorker.ReadModel;

namespace ProjectionWorker;

public sealed class ProjectionConsumerWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ProjectionConsumerWorker> _logger;
    private readonly IMongoCollection<AccountStatement> _collection;

    public ProjectionConsumerWorker(IConfiguration config, ILogger<ProjectionConsumerWorker> logger)
    {
        _config = config;
        _logger = logger;

        var mongoClient = new MongoClient(_config["Mongo:ConnectionString"]);
        var database = mongoClient.GetDatabase(_config["Mongo:Database"]);
        _collection = database.GetCollection<AccountStatement>("account-statements");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _config["Kafka:BootstrapServers"],
            GroupId = _config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var topic = _config["Kafka:Topics:Settled"];

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation("ProjectionWorker consuming {Topic}", topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);
                if (cr?.Message?.Value is null) continue;

                var evt = JsonSerializer.Deserialize<TransactionSettledEvent>(cr.Message.Value);
                if (evt is null) continue;

                Process(evt).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Projection error");
                Thread.Sleep(500);
            }
        }

        consumer.Close();
        return Task.CompletedTask;
    }

    private async Task Process(TransactionSettledEvent evt)
    {
        var filter = Builders<AccountStatement>.Filter.Eq(x => x.AccountId, evt.AccountId);

        var existing = await _collection.Find(filter).FirstOrDefaultAsync();

        if (existing == null)
        {
            existing = new AccountStatement
            {
                AccountId = evt.AccountId,
                HolderName = "Unknown",
                CurrentBalance = evt.NewBalance
            };
        }

        existing.CurrentBalance = evt.NewBalance;
        existing.LastUpdatedAt = DateTime.UtcNow;

        existing.Transactions.Add(new StatementTransaction
        {
            TransactionId = evt.TransactionId,
            Amount = 0, // poderia enriquecer depois
            Type = "Settled",
            OccurredAtUtc = evt.OccurredAtUtc
        });

        await _collection.ReplaceOneAsync(
            filter,
            existing,
            new ReplaceOptions { IsUpsert = true });

        _logger.LogInformation("Projection updated for Account {AccountId}", evt.AccountId);
    }
}
