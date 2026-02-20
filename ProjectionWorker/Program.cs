using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectionWorker;
using ProjectionWorker.Infrastructure;
using BuildingBlocks.Projections;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Infra de Mongo
        services.AddSingleton<MongoContext>();

        // Serviço de projeção (aplicação)
        services.AddScoped<IAccountStatementProjectionService, MongoAccountStatementProjectionService>();

        // Worker Kafka
        services.AddHostedService<ProjectionConsumer>();
    })
    .Build();

await host.RunAsync();