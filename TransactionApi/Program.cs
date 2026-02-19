using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using TransactionApi.Domain.Entities;
using TransactionApi.Infrastructure.Persistence;
using TransactionApi.Messaging;
using TransactionApi.Outbox;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Finance Transactions API",
        Version = "v1",
        Description = "API responsável pelo recebimento e autorização de transações financeiras, usando Outbox + Kafka + Workers para liquidação.",
    });

    // Comentários XML (vamos configurar no .csproj)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!)
                                  .AddCheck("self", () => HealthCheckResult.Healthy());

builder.Services.AddControllers();
builder.Services.AddSingleton<IKafkaProducer>(_ =>
{
    var cfg = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
        ClientId = builder.Configuration["Kafka:ClientId"] ?? "transaction-api",
        Acks = Acks.All,
        EnableIdempotence = true
    };

    return new KafkaProducer(cfg);
});

builder.Services.AddHostedService<OutboxPublisherWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!db.Accounts.Any())
    {
        db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            HolderName = "Test User",
            Balance = 20000m,
            Version = 0,
            CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }
}

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.ToString()
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
});

// (Opcional) Remova o /todos do template
app.Run();

