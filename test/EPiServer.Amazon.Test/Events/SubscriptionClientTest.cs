using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Amazon.SQS;
using Amazon.SQS.Model;
using EPiServer.Events;
using Moq;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class SubscriptionClientTest
    {
        [Fact]
        public void Receive_WhenMessageCannotBeDeserialized_ShouldReturnOtherMessagesAndDeleteAll()
        {
            var serializer = new MessageSerializer();
            var validEvent = new EventMessage { EventId = Guid.NewGuid() };
            var valid = new Message { MessageId = "valid", ReceiptHandle = "r1", Body = Notification(serializer.Serialize(validEvent)) };
            var invalid = new Message { MessageId = "invalid", ReceiptHandle = "r2", Body = Notification("{\"Parameter\":{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\"}}") };

            DeleteMessageBatchRequest deleteRequest = null;
            var queueService = new Mock<IAmazonSQS>();
            queueService.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceiveMessageResponse { Messages = new List<Message> { invalid, valid } });
            queueService.Setup(s => s.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteMessageBatchRequest, CancellationToken>((r, _) => deleteRequest = r)
                .ReturnsAsync(new DeleteMessageBatchResponse());

            var subject = new SubscriptionClient(queueService.Object, "queue");

            var result = subject.Receive(TimeSpan.Zero, 10).ToList();

            Assert.Equal(validEvent.EventId, Assert.Single(result).EventId);
            Assert.NotNull(deleteRequest);
            Assert.Equal(new[] { "invalid", "valid" }, deleteRequest.Entries.Select(e => e.Id));
        }

        private static string Notification(string message)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { Type = "Notification", Message = message });
        }
    }
}
