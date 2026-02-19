using Confluent.Kafka;

namespace SettlementWorker.Messaging;

public interface IKafkaProducer
{
    Task ProduceAsync(string topic, string key, string payload, IDictionary<string, string>? headers, CancellationToken ct);
}

public sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaProducer(ProducerConfig config)
    {
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync(string topic, string key, string payload, IDictionary<string, string>? headers, CancellationToken ct)
    {
        var msg = new Message<string, string> { Key = key, Value = payload, Headers = new Headers() };

        if (headers is not null)
            foreach (var (k, v) in headers)
                msg.Headers.Add(k, System.Text.Encoding.UTF8.GetBytes(v));

        await _producer.ProduceAsync(topic, msg).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
