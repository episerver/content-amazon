using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using EPiServer.Events;
using EPiServer.Logging;
using System;
using System.Globalization;
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
        private MessageSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="TopicClient"/> class.
        /// </summary>
        /// <param name="notificationService">The notification service client used by this instance.</param>
        /// <param name="topicArn">The ARN of the Topic to publish to.</param>
        /// <exception cref="System.ArgumentNullException" />
        public TopicClient(IAmazonSimpleNotificationService notificationService, string topicArn)
        {
            if (notificationService == null)
                throw new ArgumentNullException("notificationService");
            if (topicArn == null)
                throw new ArgumentNullException("topicArn");

            _notificationService = notificationService;
            _topicArn = topicArn;
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
        /// Publishes the specified message to the notification service.
        /// </summary>
        /// <param name="message">The message to publish.</param>
        /// <exception cref="System.ArgumentNullException" />
        public virtual void Publish(EventMessage message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            var serializedMessage = Serializer.Serialize(message);
            var publishRequest = new PublishRequest
            {
                TopicArn = _topicArn,
                Message = serializedMessage
            };

            _notificationService.PublishAsync(publishRequest)
                .ContinueWith(t =>
                {
                    var e = t.Exception;
                    if (e == null)
                    {
                        return;
                    }

                    foreach (var innerException in e.InnerExceptions)
                    {
                        Logger.Error(string.Format(CultureInfo.InvariantCulture, "Failed to send event: {0}", innerException.Message), innerException);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
