using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProjectionWorker.ReadModel;

public class AccountStatement
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid AccountId { get; set; }

    public string HolderName { get; set; } = "";

    public decimal CurrentBalance { get; set; }

    public List<StatementTransaction> Transactions { get; set; } = new();

    public DateTime LastUpdatedAt { get; set; }
}

public class StatementTransaction
{
    [BsonRepresentation(BsonType.String)]
    public Guid TransactionId { get; set; }

    public decimal Amount { get; set; }

    public string Type { get; set; } = "";

    public DateTime OccurredAtUtc { get; set; }
}
