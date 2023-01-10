using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Moq;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class QueueCleanerTest
    {
        Mock<IAmazonSQS> _queueService;
        Mock<IAmazonSimpleNotificationService> _notificationClient;
        const string postFixSubArn = "subArn";
 

        [Fact]
        public void ListQueues_WhenRequestedQueues_ShouldBeCalledtWithTopicName()
        {
            var subject = Setup(new List<string>() { "myownQueue"});
            
            var res = subject.ListQueues();
            
            _queueService.Verify((s) => s.ListQueuesAsync(It.Is<ListQueuesRequest>(r => r.QueueNamePrefix == "mytopic"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void ListQueues_WhenThereNoOtherThanOwnedQueue_ShouldReturnEmpty()
        {
            var subject = Setup(new List<string>() { "myownQueue" });

            var res = subject.ListQueues();

            Assert.Empty(res);
            _queueService.Verify((s) => s.ListQueuesAsync(It.Is<ListQueuesRequest>(r => r.QueueNamePrefix == "mytopic"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void ListQueues_WhenThereIsOtherQueue_ShouldReturnTheQueue()
        {
            var subject = Setup(new List<string>() { "myownQueue", "otherQueue" });

            var res = subject.ListQueues();

            Assert.Single(res);
            Assert.Equal("otherQueue", res.First().QueueUrl);
            _queueService.Verify((s) => s.ListQueuesAsync(It.Is<ListQueuesRequest>(r => r.QueueNamePrefix == "mytopic"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Cleanup_WhenThereIsUsedQueue_ShouldKeepTheQueue()
        {
            var subject = Setup(new List<string>() { "myownQueue", "otherQueue" });

            var sentTime = CreateEpochTimeStamp(DateTime.UtcNow);
            var m = new Message() { };
            m.Attributes.Add("SentTimestamp", sentTime);
            _queueService.Setup(s => s.ReceiveMessageAsync(It.Is<ReceiveMessageRequest>(r => r.QueueUrl == "otherQueue"), It.IsAny<CancellationToken>())).ReturnsAsync(new ReceiveMessageResponse() { Messages = new List<Message>() { m} });
            
            var res = subject.Cleanup();

            Assert.Empty(res);
        }

        [Fact]
        public void Clean_WhenThereIsUnusedQueue_ShouldCleanupTheQueue()
        {
            var subject =  Setup(new List<string>() { "myownQueue", "otherQueue" });

            var sentTime = CreateEpochTimeStamp(DateTime.UtcNow.AddHours(-2));
            var m = new Message() { };
            m.Attributes.Add("SentTimestamp", sentTime);
            _queueService.Setup(s => s.ReceiveMessageAsync(It.Is<ReceiveMessageRequest>(r => r.QueueUrl == "otherQueue"), It.IsAny<CancellationToken>())).ReturnsAsync(new ReceiveMessageResponse() { Messages = new List<Message>() { m } });

            var res = subject.Cleanup();
            
            Assert.Single(res);
            Assert.Equal("otherQueue", res.First().QueueUrl);
            _queueService.Verify((s) => s.DeleteQueueAsync(It.Is<DeleteQueueRequest>(r => r.QueueUrl == "otherQueue"), It.IsAny<CancellationToken>()), Times.Once);
            _notificationClient.Verify((s) => s.UnsubscribeAsync(It.Is<UnsubscribeRequest>(req => req.SubscriptionArn == "otherQueue" + postFixSubArn), It.IsAny<CancellationToken>()), Times.Once);
        }

        private QueueCleaner Setup(List<string> queueUrls)
        {
            _queueService = new Mock<IAmazonSQS>();
            _queueService.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ListQueuesResponse() {  QueueUrls = queueUrls });

            _notificationClient = new Mock<IAmazonSimpleNotificationService>();
            var subscriptions = new List<Subscription>(); 
            foreach (var q in queueUrls)
            {
                var subscriptionArn = q + postFixSubArn;
                subscriptions.Add(new Subscription() { SubscriptionArn = subscriptionArn, Endpoint = q });

                _notificationClient.Setup(n => n.UnsubscribeAsync(subscriptionArn, It.IsAny<CancellationToken>())).ReturnsAsync(new UnsubscribeResponse());
                
                var getQueryReq = new GetQueueAttributesResponse();
                getQueryReq.Attributes.Add("QueueArn", q);
                _queueService.Setup(s => s.GetQueueAttributesAsync(It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == q), It.IsAny<CancellationToken>())).ReturnsAsync(getQueryReq);
            }
            _notificationClient.Setup(n => n.ListSubscriptionsAsync(It.IsAny<ListSubscriptionsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ListSubscriptionsResponse() { Subscriptions = subscriptions });

            var subject = new QueueCleaner(_queueService.Object, _notificationClient.Object, "mytopic", "myownQueue", TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(2));
            return subject;
        }

        private static string CreateEpochTimeStamp(DateTime dateTime)
        {
            var diff = dateTime - EpochDateTimeConverter.Epoch;

            return diff.TotalMilliseconds.ToString();

        }
    }
}
