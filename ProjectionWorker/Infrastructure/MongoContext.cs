using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using ProjectionWorker.Projections;

namespace ProjectionWorker.Infrastructure;

public class MongoContext
{
    public IMongoCollection<AccountStatement> AccountStatements { get; }

    public MongoContext(IConfiguration configuration)
    {
        var connectionString = configuration["Mongo:ConnectionString"];
        var databaseName = configuration["Mongo:Database"];

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);

        AccountStatements = database.GetCollection<AccountStatement>("account_statement");
    }
}