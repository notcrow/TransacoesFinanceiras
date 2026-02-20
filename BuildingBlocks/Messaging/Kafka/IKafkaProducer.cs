using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BuildingBlocks.Messaging.Kafka;

public interface IKafkaProducer
{
    Task ProduceAsync(
        string topic,
        string key,
        string payload,
        IDictionary<string, string>? headers = null,
        CancellationToken ct = default);
}