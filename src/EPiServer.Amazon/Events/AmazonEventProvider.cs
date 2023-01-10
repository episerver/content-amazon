using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Amazon;
using EPiServer.Events;
using EPiServer.Events.Providers;
using Microsoft.Extensions.Logging;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Provider that uses the Amazon AWS to propagate events between servers.
    /// It uses the SNS service for publishing events and the SQS service for subscriptions.
    /// </summary>
    /// <example>
    /// The following shows an example on how the <see cref="AmazonEventProvider"/> can be configured.
    /// <code>
    /// {
    ///   "EPiServer" : {
    ///     "AmazonEvent" : {
    ///       "topic" : "EPiServer-Events",
    ///       "accessKey" : "[Your AWS Access Key]",
    ///       "secretKey" : "[Your AWS Secret Access Key]",
    ///       "region" : "ap-southeast-2"
    ///     }
    ///   }
    /// }
    /// </code>
    /// Note that all settings must be the same on all sites that should be communicating using remote events and that the
    /// Topic should be unique between different environments.
    /// </example>
    public class AmazonEventProvider : EventProvider, IDisposable
    {
        private const int QueueProcessingUninitializeTimeout = 1000;
        private const string UnknownRegionDisplayName = "Unknown";
        private readonly ILogger _logger;
        private static readonly IRetryStrategy ReceiveRetryStrategy = new IncrementalRetries(-1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

        private AmazonEventClient _client;
        private ActionBlock<EventMessage> _sendProcessor;
        private ActionBlock<EventMessage> _eventProcessor;
        private CancellationTokenSource _cts;
        private readonly AmazonEventClientOptions _amazonEventClientOptions;

        public AmazonEventProvider(ILogger<AmazonEventProvider> logger,
            AmazonEventClientOptions amazonEventClientOptions)
        {
            _logger = logger;
            _amazonEventClientOptions = amazonEventClientOptions;
        }

        /// <summary>
        /// Gets a value indicating that messages received from the provider does not need to be checked for their integrity
        /// as this is done by the AWSSDK.
        /// </summary>
        /// <value>The property will always return <c>false</c>.</value>
        public override bool ValidateMessageIntegrity
        {
            get { return false; }
        }

        /// <summary>
        /// Initialize provider
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected void CheckConfiguration()
        {
            
            // Profile cannot be used with AccessKey/SecretKey
            if (!string.IsNullOrEmpty(_amazonEventClientOptions.ProfileName) && _amazonEventClientOptions.HasAccessKeyCredentials())
            {
                throw new ArgumentException("Profile Name cannot be combined with AccessKey/SecretKey. Use one or the other all use th default AWSSDK.NET credentials configuration.");
            }

            // Either none or both AccessKey & SecretKey must be set
            if (string.IsNullOrEmpty(_amazonEventClientOptions.AccessKey) ^ string.IsNullOrEmpty(_amazonEventClientOptions.SecretKey))
            {
                throw new ArgumentException("AccessKey must be accompanied by a SecretKey. Alternatively specify Profile Name or use the default AWSSDK.NET credentials configuration.");
            }

            if (string.IsNullOrEmpty(_amazonEventClientOptions.ProfileName) && !_amazonEventClientOptions.HasAccessKeyCredentials())
            {
                _logger.LogDebug("Neither profile nor access/secret keys were specified in the provider configuration. See AWS SDK for .NET documentation for other alternative configuration options.");
            }

            // RegionEndpoint
            RegionEndpoint CheckSystemName()
            {
                try
                {
                    return RegionEndpoint.GetBySystemName(_amazonEventClientOptions.Region);
                }
                catch (Exception)
                {
                    return default;
                }
            }

            var region = !string.IsNullOrEmpty(_amazonEventClientOptions.Region) ? CheckSystemName() : default;
            if (region == default)
            {
                throw new ArgumentException("No region was specified in the provider configuration.");
            }

            // Topic
            if (string.IsNullOrWhiteSpace(_amazonEventClientOptions.TopicName))
            {
                throw new ArgumentException("A Topic name must be provided using the key 'topic'.");
            }
            // Up to 256 alphanumeric characters, hyphens (-) and underscores (_) allowed
            if (!Regex.IsMatch(_amazonEventClientOptions.TopicName, @"^[\w\-_]{1,255}$"))
            {
                throw new ArgumentException("The provided topic does not conform with AWS SNS requirements. Up to 256 alphanumeric characters, hyphens (-) and underscores (_) are allowed.");
            }
            
            //validate delete settings
            if (_amazonEventClientOptions.DeleteQueueLimit <= TimeSpan.Zero || _amazonEventClientOptions.DeleteQueueLimit.TotalMinutes + (2 * QueueCleaner.DeleteCheckTimeout.TotalMinutes) > _amazonEventClientOptions.QueueExpiration.TotalMinutes)
            {
                throw new ArgumentOutOfRangeException("config", $"DeleteQueueLimit cannot be a negative time span and must be at least {(2 * QueueCleaner.DeleteCheckTimeout.TotalMinutes)} minutes less than queue expiration.");
            }

            ReadAutoCleanupQueue();
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Ca")]
        public override void Uninitialize()
        {
            if (_sendProcessor != null)
            {
                _sendProcessor.Complete();
            }
            if (_eventProcessor != null)
            {
                _eventProcessor.Complete();
            }

            if (_cts != null)
            {
                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }
                _cts.Dispose();
                _cts = null;
            }
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            if (_sendProcessor != null)
            {
                try
                {
                    _sendProcessor.Completion.Wait(QueueProcessingUninitializeTimeout);
                }
                catch (AggregateException ae)
                {
                    ae.Handle(ex =>
                    {
                        if (!(ex is TaskCanceledException))
                        {
                            _logger.LogError("Send process threw an exception while waiting to complete.", ex);
                        }
                        return true;
                    });
                }
                _sendProcessor = null;
            }

            if (_eventProcessor != null)
            {
                try
                {
                    _eventProcessor.Completion.Wait(QueueProcessingUninitializeTimeout);
                }
                catch (AggregateException ae)
                {
                    ae.Handle(ex =>
                    {
                        if (!(ex is TaskCanceledException))
                        {
                            _logger.LogError("Event process threw an exception while waiting to complete.", ex);
                        }
                        return true;
                    });
                }
                _eventProcessor = null;
            }

            base.Uninitialize();
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            CheckConfiguration();
            return Task.Factory.StartNew(Initialize);
        }

        /// <inheritdoc />
        public override void SendMessage(EventMessage message)
        {
            if (_sendProcessor == null)
                throw new InvalidOperationException("The provider is either not initialized yet or has been disposed.");

            if (!_sendProcessor.Post(message))
            {
                _logger.LogWarning("Unable to post message to send queue. Queue is likely full");
            }
        }

        /// <summary>
        /// Creates and initializes the <see cref="AmazonEventClient"/> client.
        /// </summary>
        /// <param name="amazonEventClientOptions">The client configuration used to initialize the client with.</param>
        /// <returns>An initialized <see cref="AmazonEventClient"/> instance.</returns>
        protected virtual AmazonEventClient CreateAndInitializeClient(AmazonEventClientOptions amazonEventClientOptions)
        {
            var client = new AmazonEventClient();
            client.Initialize(amazonEventClientOptions);
            return client;
        }

        private void ReadAutoCleanupQueue()
        {
            _logger.LogDebug("Auto cleanup queue is '{0}'", _amazonEventClientOptions.DisableAutoCleanupQueue ? "Disabled" : "Enabled");
        }

        private void Initialize()
        {
            _cts = new CancellationTokenSource();

            _client = CreateAndInitializeClient(_amazonEventClientOptions);

            _sendProcessor = new ActionBlock<EventMessage>(
                m => _client.Publish(m),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _amazonEventClientOptions != null ? _amazonEventClientOptions.MaxSendThreads : 1,
                    CancellationToken = _cts.Token,
                });

            StartReceiving();
        }

        private void StartReceiving()
        {
            _eventProcessor = new ActionBlock<EventMessage>(
                m => OnMessageReceived(new EventMessageEventArgs(m)),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _amazonEventClientOptions != null ? _amazonEventClientOptions.MaxEventThreads : 1,
                    CancellationToken = _cts.Token,
                });

            _logger.LogDebug("Starting continuous receive task.");

            Task.Factory.StartContinuous(() =>
                {
                    var messages = _client.Receive();
                    if (messages != null)
                    {
                        foreach (var m in messages)
                        {
                            _eventProcessor.Post(m);
                        }
                    }
                },
                _cts.Token,
                ReceiveRetryStrategy);
        }

        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_cts", Justification = "Uninitialize method calls Dispose on field.")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            Uninitialize();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
