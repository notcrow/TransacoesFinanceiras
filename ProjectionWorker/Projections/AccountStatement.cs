using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProjectionWorker.Projections;

public class AccountStatement
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid AccountId { get; set; }

    public decimal CurrentBalance { get; set; }

    public List<AccountStatementTransaction> Transactions { get; set; } = new();

    public DateTime LastUpdated { get; set; }
}

public class AccountStatementTransaction
{
    public Guid TransactionId { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime ProcessedAt { get; set; }
}