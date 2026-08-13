using System;
using System.Text.RegularExpressions;
using EPiServer.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class MessageSerializerTest
    {
        // The event id that the parameter type is registered for, and that the example notification below carries.
        private static readonly Guid TestStateMessageEventId = new Guid("51da5053-6af8-4a10-9bd4-8417e48f38bd");

        private const string exampleSqsJson = "{\n  \"Type\" : \"Notification\",\n " +
            " \"MessageId\" : \"99cf755c-893a-5c0c-9ec2-3fcc1d3482ae\",\n " +
            " \"TopicArn\" : \"arn:aws:sns:ap-southeast-2:565859746803:EPiServer-Events\",\n " +
            " \"Message\" : \"{" +
                    "\\\"RaiserId\\\":\\\"b9f7ba84-c341-4cd2-9a7a-75dee7812c23\\\"," +
                    "\\\"SiteId\\\":\\\"979b9954cbd9409c905ebb09ef30b2f7\\\"," +
                    "\\\"SequenceNumber\\\":6," +
                    "\\\"ServerName\\\":\\\"PULVER\\\"," +
                    "\\\"ApplicationName\\\":\\\"/LM/W3SVC/4/ROOT\\\"," +
                    "\\\"Sent\\\":\\\"2013-08-22T22:25:55.6334138Z\\\"," +
                    "\\\"VerificationData\\\":\\\"GL18gfME9pDG/gVdurQnHUtVM8LR3lCunw4dBGnUIvM=\\\"," +
                    "\\\"EventId\\\":\\\"51da5053-6af8-4a10-9bd4-8417e48f38bd\\\"," +
                    "\\\"Parameter\\\":{" +
                        "\\\"ServerName\\\":\\\"PULVER\\\"," +
                        "\\\"ApplicationName\\\":\\\"/LM/W3SVC/4/ROOT\\\"," +
                        "\\\"Sent\\\":\\\"2013-08-22T22:25:55.6294049Z\\\"," +
                        "\\\"Type\\\":1" +
                "}" +
            "}\",\n " +
            " \"Timestamp\" : \"2013-08-22T22:33:50.750Z\",\n " +
            " \"SignatureVersion\" : \"1\",\n " +
            " \"Signature\" : \"aHC9irQqC6d4vBGeztk496VvzEdwitySOEvZzYxggvmymSSh+ERP6rH9e1a6NYie5o5e/lAJeZqcKD4oEU7Ib8Q1bZ3MumNIBrql70X/80culpl3oXCIIhrnQEv4AofRbmA9PurMOeFegjrk3WJuYuaLsc1PCo4gmw9wRq9V2m4=\",\n " +
            " \"SigningCertURL\" : \"https://sns.ap-southeast-2.amazonaws.com/SimpleNotificationService-f3ecfb7224c7233fe7bb5f59f96de52f.pem\",\n " +
            " \"UnsubscribeURL\" : \"https://sns.ap-southeast-2.amazonaws.com/?Action=Unsubscribe&SubscriptionArn=arn:aws:sns:ap-southeast-2:565859746803:EPiServer-Events:a59faa8c-b985-42f1-ae57-e4ea58a75d4c\"\n}";

        private static MessageSerializer CreateSerializer()
        {
            var eventsOptions = new EventsOptions();
            eventsOptions.AddEventType<TestStateMessage>(TestStateMessageEventId);
            return new MessageSerializer(Options.Create(eventsOptions));
        }

        [Fact]
        public void Serialize_ShouldCreateJsonSerialization()
        {
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                RaiserId = Guid.NewGuid(),
                ServerName = "A"
            };

            var subject = CreateSerializer();

            var result = subject.Serialize(message);

            Assert.Matches(PropertyPattern("EventId", message.EventId), result);
            Assert.Matches(PropertyPattern("RaiserId", message.RaiserId), result);
            Assert.Matches(PropertyPattern("ServerName", message.ServerName), result);
        }

        [Fact]
        public void Serialize_WithMessageParameter_ShouldCreateJsonSerialization()
        {
            var param = new TestStateMessage("A", "X", TestStateMessageType.Awesome, DateTime.UtcNow);
            var message = new EventMessage
            {
                EventId = TestStateMessageEventId,
                Parameter = param
            };

            var subject = CreateSerializer();

            var result = subject.Serialize(message);

            Assert.Matches(PropertyPattern("ServerName", param.ServerName), result);
        }

        [Fact]
        public void Serialize_WithMessageParameter_ShouldNotIncludeTypeInformation()
        {
            var message = new EventMessage
            {
                EventId = TestStateMessageEventId,
                Parameter = new TestStateMessage("A", "X", TestStateMessageType.Awesome, DateTime.UtcNow)
            };

            var subject = CreateSerializer();

            var result = subject.Serialize(message);

            // The parameter type is resolved from the registered event type, not from the message.
            Assert.DoesNotContain("$type", result, StringComparison.Ordinal);
            Assert.DoesNotContain(typeof(TestStateMessage).FullName, result, StringComparison.Ordinal);
        }

        [Fact]
        public void Serialize_WithEnumInParameter_ShouldWriteItsName()
        {
            var message = new EventMessage
            {
                EventId = TestStateMessageEventId,
                Parameter = new TestStateMessage("A", "X", TestStateMessageType.Awesome, DateTime.UtcNow)
            };

            var subject = CreateSerializer();

            var result = subject.Serialize(message);

            Assert.Contains("\"Type\":\"Awesome\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void Serialize_WithGuidParameter_ShouldBeDeserializableToGuid()
        {
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = Guid.NewGuid()
            };
            var subject = CreateSerializer();
            var result = subject.Serialize(message);
            message = subject.Deserialize(result);

            Assert.IsType<Guid>(message.Parameter);
        }

        [Fact]
        public void Serialize_WithoutOptionalProperties_ShouldOmitThem()
        {
            var subject = CreateSerializer();

            var result = subject.Serialize(new EventMessage { EventId = Guid.NewGuid() });

            Assert.DoesNotContain("ApplicationName", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Sent", result, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("text")]
        [InlineData(true)]
        public void Deserialize_WithScalarParameter_ShouldInferItsType(object parameter)
        {
            var subject = CreateSerializer();

            var result = subject.Deserialize(subject.Serialize(new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = parameter
            }));

            Assert.Equal(parameter, result.Parameter);
        }

        [Fact]
        public void Deserialize_WithIntegerParameter_ShouldInferItAsInt64()
        {
            var subject = CreateSerializer();

            var result = subject.Deserialize(subject.Serialize(new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = 42
            }));

            Assert.Equal(42L, result.Parameter);
        }

        [Fact]
        public void Deserialize_WithMessageParameter_ShouldRecreateParameterAsRegisteredType()
        {
            var param = new TestStateMessage("A", "X", TestStateMessageType.Awesome, DateTime.UtcNow);
            var message = new EventMessage
            {
                EventId = TestStateMessageEventId,
                Parameter = param
            };

            var subject = CreateSerializer();

            var serialized = subject.Serialize(message);
            var result = subject.Deserialize(serialized);

            var parameter = Assert.IsType<TestStateMessage>(result.Parameter);
            Assert.Equal(param.ServerName, parameter.ServerName);
            Assert.Equal(param.Type, parameter.Type);
        }

        [Fact]
        public void Deserialize_WithUnregisteredEvent_ShouldReadTheMessageWithoutItsParameter()
        {
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                SequenceNumber = 3,
                ServerName = "A",
                Parameter = new TestStateMessage("A", "X", TestStateMessageType.Awesome, DateTime.UtcNow)
            };

            var subject = CreateSerializer();

            var result = subject.Deserialize(subject.Serialize(message));

            Assert.Null(result.Parameter);
            Assert.Equal(3, result.SequenceNumber);
            Assert.Equal("A", result.ServerName);
        }

        [Fact]
        public void DeserializeNotification_WithExampleJson_ShouldCreateEventMessage()
        {
            var subject = CreateSerializer();

            var result = subject.DeserializeNotification(exampleSqsJson);

            Assert.NotNull(result);
            Assert.Equal(new Guid("51da5053-6af8-4a10-9bd4-8417e48f38bd"), result.EventId);
            Assert.Equal(new Guid("b9f7ba84-c341-4cd2-9a7a-75dee7812c23"), result.RaiserId);
        }

        [Fact]
        public void DeserializeNotification_WithExampleJson_ShouldCreateTypedParameter()
        {
            var subject = CreateSerializer();

            var result = subject.DeserializeNotification(exampleSqsJson);

            Assert.IsAssignableFrom<TestStateMessage>(result.Parameter);
            Assert.Equal("PULVER", ((TestStateMessage)result.Parameter).ServerName);
        }

        private static string PropertyPattern(string propertyName, object value)
        {
            return $@"[\""\']{Regex.Escape(propertyName)}[\""\']:\s?[\""\']{Regex.Escape(value.ToString())}[\""\']";
        }
    }
}
