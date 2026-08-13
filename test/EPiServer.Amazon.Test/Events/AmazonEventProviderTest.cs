using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using EPiServer.Amazon.Blobs;
using EPiServer.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class AmazonEventProviderTest
    {
        private const int WaitTimeout = 3000;
        private readonly AmazonEventClientOptions _amazonEventClientOptions;
        private readonly IOptions<EventsOptions> _eventsOptions = Options.Create(new EventsOptions());

        public AmazonEventProviderTest()
        {
            _amazonEventClientOptions = new AmazonEventClientOptions
            {
                TopicName = "topic-name",
                Region = "eu-west-1"
            };
        }

        [Fact]
        public async Task Initialize_WhenBothProfileNameAndAccessKeyIsProvided_ShouldThrowException()
        {
            _amazonEventClientOptions.ProfileName = "Some profile";
            _amazonEventClientOptions.AccessKey = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.SecretKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenAccessKeyButNoSecretKeyIsProvided_ShouldThrowException()
        {
            _amazonEventClientOptions.AccessKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenSecretKeyButNoAccessKeyIsProvided_ShouldThrowException()
        {
            _amazonEventClientOptions.SecretKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenTopicNameWithSymbolsIsProvided_ShouldThrowException()
        {
            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.TopicName = "topic&sons";
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenNoTopicNameIsProvided_ShouldThrowException()
        {
            _amazonEventClientOptions.TopicName = "";
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenDeleteQueueLimitIsZero_ShouldThrowException()
        {
            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.DeleteQueueLimit = TimeSpan.Zero;
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenDeleteQueueLimitIsSameAsQueueExpiration_ShouldThrowException()
        {
            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.DeleteQueueLimit = new TimeSpan(2, 0, 0);
            _amazonEventClientOptions.QueueExpiration = new TimeSpan(2, 0, 0);
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenDeleteQueueLimitIsLargerThanQueueExpiration_ShouldThrowException()
        {
            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.DeleteQueueLimit = new TimeSpan(2, 0, 0);
            _amazonEventClientOptions.QueueExpiration = new TimeSpan(1, 0, 0);
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.InitializeAsync());
        }

        [Fact]
        public void SendMessage_WhenNotInitialized_ShouldThrow()
        {
            var provider = new AmazonEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, _eventsOptions);
            Assert.Throws<InvalidOperationException>(() => provider.SendMessage(new EventMessage()));
        }

        [Fact]
        public void SendMessage_ShouldCallClientOnSepearateThread()
        {
            var msg = new EventMessage();
            var client = new Mock<AmazonEventClient>();

            var waitSignal = new ManualResetEvent(false);
            var completeSignal = new ManualResetEvent(false);
            var clientCalled = false;
            client.Setup(x => x.PublishAsync(msg, It.IsAny<CancellationToken>())).Returns(() =>
            {
                // Wait for thread check to complete
                waitSignal.WaitOne(WaitTimeout);

                clientCalled = true;

                // Signal complete
                completeSignal.Set();

                return Task.CompletedTask;
            });

            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            var provider = new TestEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, c => client.Object);
            provider.InitializeAsync().Wait(WaitTimeout);

            provider.SendMessage(msg);

            // Thread Assertion
            Assert.False(clientCalled, "Message processed on same thread");

            waitSignal.Set();

            // Wait for other thread to complete
            completeSignal.WaitOne(WaitTimeout);

            // Functionality Assertion
            Assert.True(clientCalled, "Message was never processed");
        }

        [Fact]
        public void SendMessage_ShouldNotPublishMoreConcurrentlyThanMaxSendThreads()
        {
            const int messageCount = 5;
            var client = new ConcurrencyProbeClient();

            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            _amazonEventClientOptions.MaxSendThreads = 1;

            var provider = new TestEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, c => client);
            provider.InitializeAsync().Wait(WaitTimeout);

            for (var i = 0; i < messageCount; i++)
            {
                provider.SendMessage(new EventMessage { SequenceNumber = i });
            }

            Assert.True(SpinWait.SpinUntil(() => client.Completed == messageCount, WaitTimeout), "Not every message was published");
            Assert.Equal(1, client.MaxInFlight);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldPublishTheMessage()
        {
            var client = new ConcurrencyProbeClient();

            _amazonEventClientOptions.ProfileName = Guid.NewGuid().ToString("n");
            var provider = new TestEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, c => client);
            await provider.InitializeAsync();

            await provider.SendMessageAsync(new EventMessage(), CancellationToken.None);

            Assert.True(SpinWait.SpinUntil(() => client.Completed == 1, WaitTimeout), "The message was never published");
        }

        [Fact]
        public void MessageReceives_WhenMessageIsReceived_ShouldCallEventHandlerOnSepearateThread()
        {
            var client = new Mock<AmazonEventClient>();

            var assertSignal = new ManualResetEvent(false);
            var blockSignal = new ManualResetEvent(false);

            var secondMessageReceived = false;

            var messageSource = new BlockingCollection<EventMessage>();

            client.Setup(x => x.Receive()).Returns(() =>
            {
                EventMessage m;
                messageSource.TryTake(out m, WaitTimeout);
                if (m.SequenceNumber > 1)
                {
                    secondMessageReceived = true;
                    assertSignal.Set();
                }
                return new[] { m };
            });

            var provider = new TestEventProvider(new NullLogger<AmazonEventProvider>(), _amazonEventClientOptions, c => client.Object);
            // Block event handler thread(s)
            provider.MessageReceived += (s, e) => blockSignal.WaitOne(WaitTimeout);
            provider.InitializeAsync().Wait(WaitTimeout);

            messageSource.Add(new EventMessage { SequenceNumber = 1 });
            messageSource.Add(new EventMessage { SequenceNumber = 2 });

            assertSignal.WaitOne(WaitTimeout);

            Assert.True(secondMessageReceived, "Second event was never raised");

            // Release event handler threads
            blockSignal.Set();
        }

        /// <summary>
        /// Records how many publish operations are in flight at the same time. If the send queue does not
        /// await <see cref="AmazonEventClient.PublishAsync"/>, every message is launched at once and
        /// <see cref="AmazonEventClientOptions.MaxSendThreads"/> stops limiting anything.
        /// </summary>
        private class ConcurrencyProbeClient : AmazonEventClient
        {
            private readonly object _sync = new object();
            private int _inFlight;

            public int MaxInFlight { get; private set; }

            public int Completed { get; private set; }

            public override async Task PublishAsync(EventMessage message, CancellationToken cancellationToken = default)
            {
                lock (_sync)
                {
                    _inFlight++;
                    MaxInFlight = Math.Max(MaxInFlight, _inFlight);
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);

                lock (_sync)
                {
                    _inFlight--;
                    Completed++;
                }
            }

            public override IEnumerable<EventMessage> Receive()
            {
                Thread.Sleep(WaitTimeout);
                return Enumerable.Empty<EventMessage>();
            }
        }

        private class TestEventProvider : AmazonEventProvider
        {
            private Func<AmazonEventClientOptions, AmazonEventClient> _clientFactory;

            public TestEventProvider(ILogger<AmazonEventProvider> logger,
                AmazonEventClientOptions amazonEventClientOptions,
                Func<AmazonEventClientOptions, AmazonEventClient> clientFactory = null)
                : base(logger, amazonEventClientOptions, Options.Create(new EventsOptions()))
            {
                _clientFactory = clientFactory ?? (c => base.CreateAndInitializeClient(c));
            }

            protected override AmazonEventClient CreateAndInitializeClient(AmazonEventClientOptions clientConfiguration)
            {
                return _clientFactory(clientConfiguration);
            }
        }
    }
}
