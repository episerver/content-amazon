using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.Auth.AccessControlPolicy;
using Amazon.Auth.AccessControlPolicy.ActionIdentifiers;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using EPiServer.Events;
using EPiServer.Logging;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Service client responsible for setting up Topics and Subscription Queues in AWS
    /// and passing through event messages.
    /// </summary>
    public class AmazonEventClient : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const int SubscriptionNameMaxLength = 80;
        private AWSCredentials _credentials;
        private IAmazonSQS _queueService;
        private IAmazonSimpleNotificationService _notificationService;
        private string _queueUrl;
        private string _subscriptionArn;
        private TopicClient _topicClient;
        private SubscriptionClient _subscriptionClient;
        private QueueCleaner _queueCleaner;

        /// <summary>
        /// Gets or sets the client configuration to use.
        /// </summary>
        protected AmazonEventClientOptions ClientConfiguration { get; set; }

        /// <summary>
        /// Initializes the client using the specified client configuration.
        /// </summary>
        /// <param name="clientConfiguration">The client configuration.</param>
        /// <exception cref="System.ArgumentNullException" />
        /// <exception cref="System.InvalidOperationException">An instance can only be initialized once.</exception>
        public virtual void Initialize(AmazonEventClientOptions clientConfiguration)
        {
            if (clientConfiguration == null)
                throw new ArgumentNullException("clientConfiguration");

            if (ClientConfiguration != null)
                throw new InvalidOperationException("This instance has already been initialized before.");

            ClientConfiguration = clientConfiguration;

            // Create clients
            _credentials = CreateCredentials();
            _notificationService = CreateNotificationServiceClient(_credentials);
            _queueService = CreateQueueServiceClient(_credentials);

            // Topic
            var topicArn = CreateTopic(_notificationService, clientConfiguration.TopicName);
            _topicClient = new TopicClient(_notificationService, topicArn);

            // Subscription Queue
            string queueArn = CreateAndConfigureQueue(_queueService, topicArn, out _queueUrl);
            _subscriptionArn = SubscribeQueueToTopic(_notificationService, topicArn, queueArn);
            _subscriptionClient = new SubscriptionClient(_queueService, _queueUrl);

            if (!clientConfiguration.DisableAutoCleanupQueue)
            {
                _queueCleaner = new QueueCleaner(_queueService, _notificationService, clientConfiguration.TopicName, _queueUrl, clientConfiguration.QueueExpiration, clientConfiguration.DeleteQueueLimit);
                _queueCleaner.Run();
            }
        }

        /// <summary>
        /// Publishes the specified message to the Amazon SNS topic.
        /// </summary>
        /// <param name="message">The message to publish.</param>
        /// <exception cref="System.ArgumentNullException"/>
        /// <exception cref="System.InvalidOperationException">The Receive method cannot be called before the instance has been initialized.</exception>
        public virtual void Publish(EventMessage message)
        {
            if (message == null)
                throw new ArgumentNullException("message");
            if (_topicClient == null)
                throw new InvalidOperationException("The Receive method cannot be called before the instance has been initialized.");

            _topicClient.Publish(message);
        }

        /// <summary>
        /// Receives any new messages that has been posted to the Amazon SQS queue.
        /// </summary>
        /// <returns>Any messages posted on the queue.</returns>
        /// <exception cref="System.InvalidOperationException">The Receive method cannot be called before the instance has been initialized.</exception>
        public virtual IEnumerable<EventMessage> Receive()
        {
            if (_subscriptionClient == null)
                throw new InvalidOperationException("The Receive method cannot be called before the instance has been initialized.");

            return _subscriptionClient.Receive(ClientConfiguration.QueueWaitTime, ClientConfiguration.QueueBatchSize);
        }

        /// <summary>
        /// Creates the Amazon SNS client that is used by this instance. 
        /// Override this if you need to make any specific configuration settings for the service connection.
        /// </summary>
        /// <param name="credentials">The credentials to use when connecting to the service.</param>
        /// <returns>A new <see cref="IAmazonSimpleNotificationService"/> instance.</returns>
        protected virtual IAmazonSimpleNotificationService CreateNotificationServiceClient(AWSCredentials credentials)
        {
            // Use this construct to ensure we get the right defaults from the AWSClientFactory
            var region = !string.IsNullOrEmpty(ClientConfiguration.Region) ? RegionEndpoint.GetBySystemName(ClientConfiguration.Region) : default;
            if (credentials == null && region == null)
            {
                return new AmazonSimpleNotificationServiceClient();
            }
            if (region == null)
            {
                return new AmazonSimpleNotificationServiceClient(credentials);
            }
            if (credentials == null)
            {
                return new AmazonSimpleNotificationServiceClient(region);
            }
            return new AmazonSimpleNotificationServiceClient(credentials, region);
        }

        /// <summary>
        /// Creates the Amazon SQS client that is used by this instance.
        /// Override this if you need to make any specific configuration settings for the service connection.
        /// </summary>
        /// <param name="credentials">The credentials to use when connecting to the service. Can be null</param>
        /// <returns>
        /// A new <see cref="IAmazonSQS" /> instance.
        /// </returns>
        protected virtual IAmazonSQS CreateQueueServiceClient(AWSCredentials credentials)
        {
            // Use this construct to ensure we get the right defaults from the AWSClientFactory
            var region = !string.IsNullOrEmpty(ClientConfiguration.Region) ? RegionEndpoint.GetBySystemName(ClientConfiguration.Region) : default;
            if (credentials == null && region == null)
            {
                return new AmazonSQSClient();
            }
            if (region == null)
            {
                return new AmazonSQSClient(credentials);
            }
            if (credentials == null)
            {
                return new AmazonSQSClient(region);
            }
            return new AmazonSQSClient(credentials, region);
        }

        /// <summary>
        /// Creates the AWS credentials to use when connecting to the SNS and SQS services.
        /// Override this if you need to use a different type of credential configuration than AccessKey/SecretAccessKey
        /// </summary>
        /// <returns>An instance of any type of <see cref="AWSCredentials"/> or null if the default credentials logic should be used.</returns>
        protected virtual AWSCredentials CreateCredentials()
        {
            if (!string.IsNullOrEmpty(ClientConfiguration.ProfileName))
            {
                var chain = new CredentialProfileStoreChain();
                if (chain.TryGetAWSCredentials(ClientConfiguration.ProfileName, out var profileCredentials))
                {
                    return profileCredentials;
                }
            }
            if (ClientConfiguration.HasAccessKeyCredentials())
            {
                return new BasicAWSCredentials(ClientConfiguration.AccessKey, ClientConfiguration.SecretKey);
            }
            return null;
        }

        /// <summary>
        /// Creates the SNS topic.
        /// </summary>
        /// <param name="notificationService">The SNS client.</param>
        /// <param name="topicName">Name of the topic.</param>
        /// <returns>The Topic ARN identifier.</returns>
        /// <exception cref="System.ArgumentNullException" />
        protected virtual string CreateTopic(IAmazonSimpleNotificationService notificationService, string topicName)
        {
            if (notificationService == null)
                throw new ArgumentNullException("notificationService");
            if (topicName == null)
                throw new ArgumentNullException("topicName");

            var topicResult = notificationService.CreateTopicAsync(new CreateTopicRequest { Name = topicName }).GetAwaiter().GetResult();
            var topicArn = topicResult.TopicArn;

            Logger.Debug("Created and configured SNS topic with ARN '{0}'.", topicArn);

            return topicArn;
        }

        /// <summary>
        /// Creates the and configure the SQS queue that will be used for the subscription.
        /// </summary>
        /// <param name="queueService">The SQS client.</param>
        /// <param name="topicArn">The topic ARN.</param>
        /// <param name="queueUrl">The URL of the queue that was created.</param>
        /// <returns>The queue ARN identifier.</returns>
        /// <exception cref="System.ArgumentNullException" />
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "2#")]
        protected virtual string CreateAndConfigureQueue(IAmazonSQS queueService, string topicArn, out string queueUrl)
        {
            if (queueService == null)
                throw new ArgumentNullException("queueService");
            if (topicArn == null)
                throw new ArgumentNullException("topicArn");

            string subscriptionName = CreateUniqueQueueName();

            var queueResult = queueService.CreateQueueAsync(new CreateQueueRequest { QueueName = subscriptionName }).GetAwaiter().GetResult();
            queueUrl = queueResult.QueueUrl;
            var queueArn = RequestQueueArn(queueService, queueUrl);

            ConfigureQueue(queueService, queueUrl, queueArn, topicArn);

            Logger.Debug("Created and configured SQS queue with ARN '{0}'.", queueArn);

            return queueArn;
        }

        /// <summary>
        /// Creates the access control policy for the SQS queue.
        /// </summary>
        /// <param name="topicArn">The topic ARN identifier.</param>
        /// <param name="queueArn">The queue ARN identifier.</param>
        /// <returns>A <see cref="Policy"/> instance.</returns>
        /// <exception cref="System.ArgumentNullException" />
        protected virtual Policy CreateAccessControlPolicy(string topicArn, string queueArn)
        {
            if (topicArn == null)
                throw new ArgumentNullException("topicArn");
            if (queueArn == null)
                throw new ArgumentNullException("queueArn");

            var statement = new Statement(Statement.StatementEffect.Allow)
            {
                Id = "EPiServerQueueAccess",
                Principals = new[] { Principal.AllUsers },
                Actions = new[] { new ActionIdentifier("sqs:*") },
                Resources = new[] { new Resource(queueArn) },
                Conditions = new[] { ConditionFactory.NewSourceArnCondition(topicArn) }
            };

            return new Policy("EPiServerAccessPolicy", new[] { statement });
        }

        /// <summary>
        /// Subscribes a SQS queue to a SNS topic.
        /// </summary>
        /// <param name="notificationService">The SNS client.</param>
        /// <param name="topicArn">The ARN of the Topic to subscribe to.</param>
        /// <param name="queueArn">The ARN of the Queue that should subscribe to the Topic.</param>
        /// <returns>The Subscription ARN identifier.</returns>
        /// <exception cref="System.ArgumentNullException" />
        protected virtual string SubscribeQueueToTopic(IAmazonSimpleNotificationService notificationService, string topicArn, string queueArn)
        {
            if (notificationService == null)
                throw new ArgumentNullException("notificationService");
            if (topicArn == null)
                throw new ArgumentNullException("topicArn");
            if (queueArn == null)
                throw new ArgumentNullException("queueArn");

            var subscriptionResponse = notificationService.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn
            }).GetAwaiter().GetResult();

            var subscriptionArn = subscriptionResponse.SubscriptionArn;

            Logger.Debug("Subscribed SQS queue '{0}' to SNS topic '{1}' with subscription ARN '{2}'.", queueArn, topicArn, _subscriptionArn);

            return subscriptionArn;
        }

        private void ConfigureQueue(IAmazonSQS queueService, string queueUrl, string queueArn, string topicArn)
        {
            var expirationInSec = ClientConfiguration.QueueExpiration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            var accessPolicy = CreateAccessControlPolicy(topicArn, queueArn);
            var setAttributesRequest = new SetQueueAttributesRequest { QueueUrl = queueUrl };
            setAttributesRequest.Attributes.Add("MessageRetentionPeriod", expirationInSec);
            setAttributesRequest.Attributes.Add("VisibilityTimeout", expirationInSec);
            setAttributesRequest.Attributes.Add("Policy", accessPolicy.ToJson());

            queueService.SetQueueAttributesAsync(setAttributesRequest).GetAwaiter().GetResult();

        }

        private string RequestQueueArn(IAmazonSQS queueService, string queueUrl)
        {
            var getAttributesRequest = new GetQueueAttributesRequest { QueueUrl = queueUrl };
            getAttributesRequest.AttributeNames.Add("QueueArn");
            return queueService.GetQueueAttributesAsync(getAttributesRequest).GetAwaiter().GetResult().QueueARN;
        }

        private void UnsubscribeQueue()
        {
            if (_notificationService != null && !string.IsNullOrEmpty(_subscriptionArn))
            {
                _notificationService.UnsubscribeAsync(new UnsubscribeRequest { SubscriptionArn = _subscriptionArn }).GetAwaiter().GetResult();
                Logger.Debug("Unsubscribed SQS queue with subscription name '{0}' from SNS.", _subscriptionArn);
                _subscriptionArn = null;
            }
        }

        private void DeleteQueue()
        {
            if (_queueService != null && !string.IsNullOrEmpty(_queueUrl))
            {
                _queueService.DeleteQueueAsync(new DeleteQueueRequest { QueueUrl = _queueUrl }).GetAwaiter().GetResult();
                Logger.Debug("Deleted SQS queue with name '{0}'.", _queueUrl);
                _queueUrl = null;
            }
        }

        private string CreateUniqueQueueName()
        {

            var machineName = (Environment.MachineName).Replace('/', '_').Replace(':', '_');
            var uniqueName = string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}", ClientConfiguration.TopicName, machineName, Guid.NewGuid().ToString("N"));

            // Max 80 chars, hyphens ('-') or underscore ('_')
            if (uniqueName.Length > SubscriptionNameMaxLength)
            {
                uniqueName = uniqueName.Substring(0, SubscriptionNameMaxLength);
            }
            return Regex.Replace(uniqueName, @"[^\w\-_]", "_");
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeQueue();
                DeleteQueue();

                if (_queueService != null)
                {
                    _queueService.Dispose();
                    _queueService = null;
                }

                if (_notificationService != null)
                {
                    _notificationService.Dispose();
                    _notificationService = null;
                }

                if(_queueCleaner !=null)
                {
                    _queueCleaner.Dispose();
                    _queueCleaner = null;
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
