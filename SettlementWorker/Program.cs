using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using SettlementWorker;
using SettlementWorker.Messaging;
using SettlementWorker.Persistence;
using SettlementWorker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IKafkaProducer>(_ =>
{
    var cfg = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
        ClientId = "settlement-worker",
        Acks = Acks.All,
        EnableIdempotence = true
    };

    return new KafkaProducer(cfg);
});

builder.Services.AddScoped<ISettlementService, SettlementService>();

builder.Services.AddHostedService<SettlementConsumerWorker>();

var host = builder.Build();
host.Run();
