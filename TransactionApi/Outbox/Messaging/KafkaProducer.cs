using Confluent.Kafka;

namespace TransactionApi.Outbox.Messaging
{
    public interface IKafkaProducer
    {
        Task ProduceAsync(
            string topic,
            string key,
            string payload,
            IDictionary<string, string>? headers,
            CancellationToken ct = default);
    }

    public sealed class KafkaProducer : IKafkaProducer, IDisposable
    {
        private readonly IProducer<string, string> _producer;

        public KafkaProducer(ProducerConfig config)
        {
            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        public async Task ProduceAsync(
            string topic,
            string key,
            string payload,
            IDictionary<string, string>? headers,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var message = new Message<string, string>
            {
                Key = key,
                Value = payload,
                Headers = new Headers()
            };

            if (headers is not null)
            {
                foreach (var (k, v) in headers)
                {
                    message.Headers.Add(k, System.Text.Encoding.UTF8.GetBytes(v));
                }
            }

            // ProduceAsync não aceita CancellationToken, então só usamos ct antes
            await _producer.ProduceAsync(topic, message).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}