using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Linq;
using Amazon.SQS;
using Amazon.SQS.Model;
using EPiServer.Events;
using EPiServer.Logging;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Responsible for receiving messages from a service
    /// </summary>
    public class SubscriptionClient
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const int MaximumNumberOfMessages = 10;
        private const int MaximumWaitTimeSeconds = 20;

        private readonly IAmazonSQS _queueService;
        private readonly string _queueUrl;
        private MessageSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionClient" /> class.
        /// </summary>
        /// <param name="queueService">The queue service.</param>
        /// <param name="queueUrl">The URL to the SQS queue.</param>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "1#", Justification = "The Amazon AWS API uses URL strings.")]
        public SubscriptionClient(IAmazonSQS queueService, string queueUrl)
        {
            _queueService = queueService;
            _queueUrl = queueUrl;
        }

        /// <summary>
        /// Gets or sets the serializer that is used by this instance.
        /// </summary>
        public virtual MessageSerializer Serializer
        {
            get { return _serializer ?? (_serializer = new MessageSerializer()); }
            set { _serializer = value; }
        }

        /// <summary>
        /// Receives any messages placed on the queue.
        /// </summary>
        /// <param name="waitTime">The maximum time that the method should wait for any new messages. This time cannot exceed 20s.</param>
        /// <param name="maximumNumberOfMessages">The maximum number of messages to retrieve in one go. This number cannot exceed 10.</param>
        /// <returns>
        /// An enumerable of <see cref="EventMessage" /> instances. This will be empty if no messages were found before the provided wait time passed.
        /// </returns>
        /// <exception cref="System.ArgumentOutOfRangeException" />
        public virtual IEnumerable<EventMessage> Receive(TimeSpan waitTime, int maximumNumberOfMessages)
        {
            if (waitTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("waitTime", "The wait time cannot be negative.");
            if (maximumNumberOfMessages < 1)
                throw new ArgumentOutOfRangeException("maximumNumberOfMessages", "The maximum number of messages to retrieve cannot be less than one.");

            var request = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                WaitTimeSeconds = Math.Min((int)waitTime.TotalSeconds, MaximumWaitTimeSeconds),
                MaxNumberOfMessages = Math.Min(maximumNumberOfMessages, MaximumNumberOfMessages)
            };
            ReceiveMessageResponse response = _queueService.ReceiveMessageAsync(request).GetAwaiter().GetResult();

            if (response.Messages.Count == 0)
            {
                return Enumerable.Empty<EventMessage>();
            }

            var messages = response.Messages
                .Where(m => m.Body != null)
                .Select(m => Serializer.DeserializeNotification(m.Body)).ToArray();

            DeleteRetrievedMessages(_queueUrl, response.Messages);

            return messages;
        }

        private void DeleteRetrievedMessages(string queueUrl, IEnumerable<Message> messages)
        {
            var deleteMessages = new DeleteMessageBatchRequest
            {
                QueueUrl = queueUrl,
                Entries = messages.Select(m => new DeleteMessageBatchRequestEntry()
                {
                    Id = m.MessageId,
                    ReceiptHandle = m.ReceiptHandle
                }).ToList()
            };

            // We can continue to 
            _queueService.DeleteMessageBatchAsync(deleteMessages)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Logger.Error("An exception occured while attempting to delete event messages", t.Exception);
                        return;
                    }
                    var response = t.Result;
                    if (response.Failed != null && response.Failed.Count > 0 && Logger.IsWarningEnabled())
                    {
                        foreach (var entry in response.Failed)
                        {
                            Logger.Warning("Unable to delete retrieved message: '{0}'. [{1}] \"{2}\"/\"{3}\".", entry.Id, entry.Code, entry.Message, entry.SenderFault);
                        }
                    }
                }, TaskContinuationOptions.NotOnCanceled);
        }
    }
}
