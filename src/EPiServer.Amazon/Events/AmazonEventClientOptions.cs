using System;
using EPiServer.ServiceLocation;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Holds configuration information for the <see cref="AmazonEventClient"/>.
    /// </summary>
    [Options(ConfigurationSection = "AmazonEvents")]
    public class AmazonEventClientOptions : AmazonClientOptions
    {
        internal static readonly TimeSpan DefaultQueueExpiration = TimeSpan.FromHours(2);
        private static readonly TimeSpan DefaultQueueWaitTime = TimeSpan.FromSeconds(20);
        internal static readonly TimeSpan DefaultDeleteQueueLimit = TimeSpan.FromMinutes(30);
        private const int DefaultQueueBatchSize = 10;

        /// <summary>
        /// Gets or sets the name of the SNS topic that should be used.
        /// </summary>
        public string TopicName { get; set; }

        /// <summary>
        /// Gets or sets the expiration time of messages placed on the queue.
        /// </summary>
        public TimeSpan QueueExpiration { get; set; } = DefaultQueueExpiration;

        /// <summary>
        /// Gets or sets how long receive requests should wait for new messages.
        /// </summary>
        public TimeSpan QueueWaitTime { get; set; } = DefaultQueueWaitTime;

        /// <summary>
        /// Gets or sets the time for how old a message can be before a queue is considered abandoned
        /// </summary>
        public TimeSpan DeleteQueueLimit { get; set; } = DefaultDeleteQueueLimit;

        /// <summary>
        /// Gets or sets the maximum number of messages that should be retrieved from the queue with each request.
        /// </summary>
        public int QueueBatchSize { get; set; } = DefaultQueueBatchSize;

        /// <summary>
        /// Gets or sets the maximum number of threads that should be used to send messages.
        /// </summary>
        public int MaxSendThreads { get; set; } = 1;

        /// <summary>
        /// Gets or sets the maximum number of threads that should be used to process received messages and raise events.
        /// </summary>
        public int MaxEventThreads { get; set; } = 1;

    }
}
