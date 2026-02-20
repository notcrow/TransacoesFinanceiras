namespace BuildingBlocks.Messaging;

public static class KafkaTopics
{
    public const string TransactionAuthorized = "transaction-authorized";
    public const string TransactionSettled = "transaction-settled";
    public const string DeadLetter = "transaction-dead-letter";
}

public static class KafkaHeaders
{
    public const string CorrelationId = "CorrelationId";
    public const string EventType = "EventType";
    public const string OriginalEventType = "OriginalEventType";
    public const string Source = "Source";
}