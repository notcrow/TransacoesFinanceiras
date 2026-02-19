using ProjectionWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<ProjectionConsumerWorker>();

var host = builder.Build();
host.Run();
