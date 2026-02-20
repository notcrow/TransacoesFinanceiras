using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BuildingBlocks.Projections;

public class AccountStatement
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid AccountId { get; set; }
    public string HolderName { get; set; } = "";
    public decimal CurrentBalance { get; set; }
    public List<AccountStatementTransaction> Transactions { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class AccountStatementTransaction
{
    [BsonRepresentation(BsonType.String)]
    public Guid TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "";
    public decimal BalanceAfter { get; set; }
    public DateTime ProcessedAt { get; set; }
}
