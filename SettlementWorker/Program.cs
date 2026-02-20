using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SettlementWorker;
using SettlementWorker.Messaging;
using SettlementWorker.Persistence;
using SettlementWorker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // DbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        // Kafka producer (para TransactionSettled)
        services.AddSingleton<IKafkaProducer>(_ =>
        {
            var cfg = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                ClientId = configuration["Kafka:ClientId"] ?? "settlement-worker",
                Acks = Acks.All,
                EnableIdempotence = true
            };

            return new KafkaProducer(cfg);
        });

        // SettlementService
        services.AddScoped<ISettlementService, SettlementService>();

        // Kafka consumer worker
        services.AddHostedService<SettlementConsumer>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .Build();

await host.RunAsync();