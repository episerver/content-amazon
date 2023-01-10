using System;
using System.Text.RegularExpressions;
using EPiServer.Events;
using EPiServer.Events.Clients.Internal;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class MessageSerializerTest
    {
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
                        "\\\"$type\\\":\\\"EPiServer.Events.Clients.Internal.StateMessage, EPiServer.Events\\\"," +
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

        [Fact]
        public void Serialize_ShouldCreateJsonSerialization()
        {
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                RaiserId = Guid.NewGuid(),
                ServerName = "A"
            };

            var subject = new MessageSerializer();

            var result = subject.Serialize(message);

            Assert.Matches(PropertyPattern("EventId", message.EventId), result);
            Assert.Matches(PropertyPattern("RaiserId", message.RaiserId), result);
            Assert.Matches(PropertyPattern("ServerName", message.ServerName), result);
        }

        [Fact]
        public void Serialize_WithMessageParameter_ShouldCreateJsonSerialization()
        {
            var param = new StateMessage("A", "X", StateMessageType.Awesome, DateTime.UtcNow);
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = param
            };

            var subject = new MessageSerializer();

            var result = subject.Serialize(message);

            Assert.Matches(PropertyPattern("ServerName", param.ServerName), result);
        }

        [Fact]
        public void Serialize_WithMessageParameter_ShouldIncludeTypeInformation()
        {
            var param = new StateMessage("A", "X", StateMessageType.Awesome, DateTime.UtcNow);
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = param
            };

            var subject = new MessageSerializer();

            var result = subject.Serialize(message);

            var qualifiedTypeStringWithoutVersion = string.Format("{0}, {1}", typeof(StateMessage).FullName, typeof(StateMessage).Assembly.GetName().Name);

            Assert.Matches(PropertyPattern("$type", qualifiedTypeStringWithoutVersion), result);
        }

        [Fact]
        public void Serialize_WithGuidParameter_ShouldBeDeserializableToGuid()
        {
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = Guid.NewGuid()
            };
            var subject = new MessageSerializer();
            var result = subject.Serialize(message);
            message = subject.Deserialize(result);

            Assert.IsType<Guid>(message.Parameter);
        }


        [Fact]
        public void DeserializeNotification_WithExampleJson_ShouldCreateEventMessage()
        {
            var subject = new MessageSerializer();

            var result = subject.DeserializeNotification(exampleSqsJson);

            Assert.NotNull(result);
            Assert.Equal(new Guid("51da5053-6af8-4a10-9bd4-8417e48f38bd"), result.EventId);
            Assert.Equal(new Guid("b9f7ba84-c341-4cd2-9a7a-75dee7812c23"), result.RaiserId);
        }

        [Fact]
        public void DeserializeNotification_WithExampleJson_ShouldCreateStateMessageParameter()
        {
            var subject = new MessageSerializer();

            var result = subject.DeserializeNotification(exampleSqsJson);

            Assert.IsAssignableFrom<StateMessage>(result.Parameter);
            Assert.Equal("PULVER", ((StateMessage)result.Parameter).ServerName);
        }

        [Fact]
        public void Deserialize_WithMessageParameter_ShouldRecreateParameterAsOriginalType()
        {
            var param = new StateMessage("A", "X", StateMessageType.Awesome, DateTime.UtcNow);
            var message = new EventMessage
            {
                EventId = Guid.NewGuid(),
                Parameter = param
            };

            var subject = new MessageSerializer();

            var serialized = subject.Serialize(message);
            var result = subject.Deserialize(serialized);

            Assert.IsAssignableFrom<StateMessage>(result.Parameter);
            Assert.Equal(param.ServerName, ((StateMessage)result.Parameter).ServerName);
        }

        private static string PropertyPattern(string propertyName, object value)
        {
            return $@"[\""\']{Regex.Escape(propertyName)}[\""\']:\s?[\""\']{Regex.Escape(value.ToString())}[\""\']";
        }
    }
}
