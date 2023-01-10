using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Threading;
using EPiServer.Events;
using Xunit;

namespace EPiServer.Amazon.Events
{
    // Make test class public to enable
    [Trait("Category", "Integration")]
    internal class AmazonEventIntegrationTest
    {
        private const int WaitTimeout = 3000;

        c
        private static AmazonEventProvider CreateProvider()
        {
            var appSettings = ConfigurationManager.AppSettings;

            var topicName = appSettings["AmazonEventProvider.TopicName"];
            if (string.IsNullOrEmpty(topicName))
            {
                Assert.True(false, "You must configure a topic name for this test to run.");
            }

            var provider = SerAmazonEventProvider();
            var config = new NameValueCollection
            {
                { AmazonEventProvider.ProfileNameKey, appSettings["AmazonProviders.ProfileName"] },
                { AmazonEventProvider.AccessKey, appSettings["AmazonProviders.AccessKey"] },
                { AmazonEventProvider.SecretKey, appSettings["AmazonProviders.SecretKey"] },
                { AmazonEventProvider.RegionKey, appSettings["AmazonProviders.Region"] },
                { AmazonEventProvider.QueueExpirationKey, appSettings["AmazonEventProvider.QueueExpiration"] },
                { AmazonEventProvider.TopicNameKey, topicName }
            };
            var t = provider.InitializeAsync();
            t.Wait();

            return provider;
        }

        [Fact]
        public void SendMessage_ShouldResultInReceivedMessage()
        {
            var provider = CreateProvider();

            // If no provider is configured - Ignore test
            if (provider == null)
                return;

            EventMessage receivedMessage = null;
            var waitHandle = new ManualResetEvent(false);

            provider.MessageReceived += (object sender, EventMessageEventArgs e) =>
            {
                receivedMessage = e.Message;
                waitHandle.Set();
            };

            var sentMessage = new EventMessage { EventId = Guid.NewGuid(), SequenceNumber = 42 };
            provider.SendMessage(sentMessage);

            waitHandle.WaitOne(WaitTimeout);

            Assert.NotNull(receivedMessage);
            Assert.NotSame(sentMessage, receivedMessage);
            Assert.Equal(sentMessage.EventId, receivedMessage.EventId);
            Assert.Equal(sentMessage.SequenceNumber, receivedMessage.SequenceNumber);
        }
    }
}
