﻿using Confluent.Kafka;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RockLib.Messaging.Kafka
{
    /// <summary>
    /// An implementation of <see cref="ISender"/> that sends messages to Kafka.
    /// </summary>
    public class KafkaSender : ISender
    {
        private readonly Lazy<IProducer<Null, string>> _producer;

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaSender"/> class.
        /// </summary>
        /// <param name="name">The name of the sender.</param>
        /// <param name="topic">The topic to produce messages to.</param>
        /// <param name="bootstrapServers">List of brokers as a CSV list of broker host or host:port.</param>
        /// <param name="messageTimeoutMs">
        /// Local message timeout. This value is only enforced locally and limits the time
        /// a produced message waits for successful delivery. A time of 0 is infinite. This
        /// is the maximum time librdkafka may use to deliver a message (including retries).
        /// Delivery error occurs when either the retry count or the message timeout are
        /// exceeded.
        /// </param>
        public KafkaSender(string name, string topic, string bootstrapServers, int messageTimeoutMs = 10000)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));

            var config = new ProducerConfig()
            {
                BootstrapServers = bootstrapServers ?? throw new ArgumentNullException(nameof(bootstrapServers)),
                MessageTimeoutMs = messageTimeoutMs
            };

            var producerBuilder = new ProducerBuilder<Null, string>(config);
            producerBuilder.SetErrorHandler(OnError);

            _producer = new Lazy<IProducer<Null, string>>(() => producerBuilder.Build());
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaSender"/> class.
        /// </summary>
        /// <param name="name">The name of the sender.</param>
        /// <param name="topic">The topic to produce messages to.</param>
        /// <param name="producer">The Kafka <see cref="IProducer{TKey, TValue}" /> to use for sending messages.</param>
        public KafkaSender(string name, string topic, IProducer<Null, string> producer)
        {
            if (producer == null)
                throw new ArgumentNullException(nameof(producer));

            Name = name ?? throw new ArgumentNullException(nameof(name));
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));

            _producer = new Lazy<IProducer<Null, string>>(() => producer);
        }

        /// <summary>
        /// Gets the name of this instance of <see cref="KafkaSender"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the topic to subscribe to.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// Gets the <see cref="IProducer{TKey, TValue}" /> for this instance of <see cref="KafkaSender"/>.
        /// </summary>
        public IProducer<Null, string> Producer { get { return _producer.Value; } }

        /// <summary>
        /// Occurs when an error happens on a background thread.
        /// </summary>
        public event EventHandler<ErrorEventArgs> Error;

        /// <summary>
        /// Asynchronously sends the specified message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public Task SendAsync(SenderMessage message, CancellationToken cancellationToken)
        {
            if (message.OriginatingSystem == null)
                message.OriginatingSystem = "Kafka";

            var kafkaMessage = new Message<Null, string> { Value = message.StringPayload };

            if (message.Headers.Count > 0)
            {
                kafkaMessage.Headers = kafkaMessage.Headers ?? new Headers();
                foreach (var header in message.Headers)
                    kafkaMessage.Headers.Add(header.Key, Encoding.UTF8.GetBytes(header.Value.ToString()));
            }

            return _producer.Value.ProduceAsync(Topic, kafkaMessage);
        }

        /// <summary>
        /// Flushes the producer and disposes it.
        /// </summary>
        public void Dispose()
        {
            if (_producer.IsValueCreated)
            {
                _producer.Value.Flush(TimeSpan.FromSeconds(10));
                _producer.Value.Dispose();
            }
        }

        private void OnError(IProducer<Null, string> producer, Error error) 
            => Error?.Invoke(this, new ErrorEventArgs(error.Reason, new KafkaException(error)));
    }
}
