using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using EPiServer.Events;
using EPiServer.Logging;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Responsible for publishing messages to a SNS Topic
    /// </summary>
    public class TopicClient
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IAmazonSimpleNotificationService _notificationService;
        private readonly string _topicArn;
        private readonly MessageSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="TopicClient"/> class.
        /// </summary>
        /// <param name="notificationService">The notification service client used by this instance.</param>
        /// <param name="topicArn">The ARN of the Topic to publish to.</param>
        /// <param name="serializer">The serializer used to write the messages published by this instance.</param>
        /// <exception cref="System.ArgumentNullException" />
        public TopicClient(IAmazonSimpleNotificationService notificationService, string topicArn, MessageSerializer serializer)
        {
            if (notificationService == null)
                throw new ArgumentNullException("notificationService");
            if (topicArn == null)
                throw new ArgumentNullException("topicArn");
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            _notificationService = notificationService;
            _topicArn = topicArn;
            _serializer = serializer;
        }

        /// <summary>
        /// Gets the serializer that is used by this instance.
        /// </summary>
        public virtual MessageSerializer Serializer
        {
            get { return _serializer; }
        }

        /// <summary>
        /// Publishes the specified message to the notification service, blocking until it has been acknowledged.
        /// </summary>
        /// <param name="message">The message to publish.</param>
        /// <exception cref="System.ArgumentNullException" />
        public virtual void Publish(EventMessage message)
        {
            PublishAsync(message).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Publishes the specified message to the notification service.
        /// </summary>
        /// <remarks>
        /// The returned task does not complete until the notification service has acknowledged the message,
        /// so that a caller holds its slot in the send queue for the duration of the request. This is what
        /// makes <see cref="AmazonEventClientOptions.MaxSendThreads"/> an effective limit on requests in flight.
        /// </remarks>
        /// <param name="message">The message to publish.</param>
        /// <param name="cancellationToken">A token used to cancel the request.</param>
        /// <exception cref="System.ArgumentNullException" />
        public virtual async Task PublishAsync(EventMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            try
            {
                var serializedMessage = Serializer.Serialize(message);
                var publishRequest = new PublishRequest
                {
                    TopicArn = _topicArn,
                    Message = serializedMessage
                };

                await _notificationService.PublishAsync(publishRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Remote events are best effort. An escaping exception would fault the send queue
                // that calls this method, dropping every later event until the application restarts.
                Logger.Error(string.Format(CultureInfo.InvariantCulture, "Failed to send event: {0}", ex.Message), ex);
            }
        }
    }
}
