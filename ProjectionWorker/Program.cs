using BuildingBlocks.Projections;
using ProjectionWorker;
using ProjectionWorker.Infrastructure;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<MongoContext>();
        services.AddScoped<IAccountStatementProjectionService, MongoAccountStatementProjectionService>();
        services.AddHostedService<ProjectionConsumer>();
    })
    .Build();

await host.RunAsync();