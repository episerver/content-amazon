using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Amazon.SQS;
using Amazon.SQS.Model;
using EPiServer.Events;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class SubscriptionClientTest
    {
        private const string QueueUrl = "https://sqs.eu-west-1.amazonaws.com/000000000000/topic-server";

        private static readonly MessageSerializer Serializer = new MessageSerializer(Options.Create(new EventsOptions()));

        private Mock<IAmazonSQS> _queueService;

        [Fact]
        public void Receive_WhenAMessageCannotBeRead_ShouldReturnTheRestOfTheBatch()
        {
            var subject = CreateClient(CreateNotification(1), "Not a JSON document at all.", CreateNotification(3));

            var result = subject.Receive(TimeSpan.Zero, 10).ToArray();

            Assert.Equal(new[] { 1, 3 }, result.Select(m => m.SequenceNumber));
        }

        [Fact]
        public void Receive_WhenAMessageCannotBeRead_ShouldDeleteItWithTheRestOfTheBatch()
        {
            var subject = CreateClient(CreateNotification(1), "Not a JSON document at all.", CreateNotification(3));

            subject.Receive(TimeSpan.Zero, 10).ToArray();

            // The unreadable message has to be deleted as well. Left on the queue it would be delivered
            // again, fail again, and hold up every event behind it for as long as the application runs.
            _queueService.Verify(s => s.DeleteMessageBatchAsync(
                It.Is<DeleteMessageBatchRequest>(r => r.QueueUrl == QueueUrl && r.Entries.Count == 3),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("Not a JSON document at all.")]
        [InlineData("{\"Type\":\"Notification\"}")]           // No Message property, as with raw message delivery.
        [InlineData("{\"Message\":42}")]                      // The envelope holds something other than a string.
        [InlineData("{\"Message\":\"{ not a message }\"}")]   // The envelope holds something other than a message.
        public void Receive_WhenAMessageCannotBeRead_ShouldNotThrow(string body)
        {
            var subject = CreateClient(body);

            Assert.Empty(subject.Receive(TimeSpan.Zero, 10));
        }

        [Fact]
        public void Receive_WhenNoMessagesAreOnTheQueue_ShouldNotDeleteAnything()
        {
            var subject = CreateClient();

            Assert.Empty(subject.Receive(TimeSpan.Zero, 10));

            _queueService.Verify(s => s.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Creates the body of a SNS notification holding an event message with the given sequence number.
        /// </summary>
        private static string CreateNotification(int sequenceNumber)
        {
            var message = Serializer.Serialize(new EventMessage
            {
                EventId = Guid.NewGuid(),
                RaiserId = Guid.NewGuid(),
                SequenceNumber = sequenceNumber
            });

            return JsonSerializer.Serialize(new { Message = message });
        }

        private SubscriptionClient CreateClient(params string[] bodies)
        {
            var messages = bodies.Select((body, index) => new Message
            {
                MessageId = "message-" + index,
                ReceiptHandle = "receipt-" + index,
                Body = body
            }).ToList();

            _queueService = new Mock<IAmazonSQS>();
            _queueService
                .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });
            _queueService
                .Setup(s => s.DeleteMessageBatchAsync(It.IsAny<DeleteMessageBatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMessageBatchResponse { Failed = new List<BatchResultErrorEntry>() });

            return new SubscriptionClient(_queueService.Object, QueueUrl, Serializer);
        }
    }
}
