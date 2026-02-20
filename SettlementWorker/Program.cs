using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Messaging.Kafka;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using SettlementWorker;
using SettlementWorker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")));

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

        services.AddScoped<ISettlementService, SettlementService>();
        services.AddHostedService<SettlementConsumer>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .Build();

await host.RunAsync();