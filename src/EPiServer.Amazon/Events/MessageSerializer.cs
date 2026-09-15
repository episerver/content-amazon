using EPiServer.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Responsible for serializing and deserializing <see cref="EventMessage"/> objects to Amazon SNS and SQS.
    /// </summary>
    public class MessageSerializer
    {
        private static readonly JsonSerializerSettings DefaultSettings = CreateSettings(null);

        private readonly JsonSerializerSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageSerializer"/> class that only allows
        /// the default set of parameter types to be deserialized.
        /// </summary>
        public MessageSerializer()
        {
            _settings = DefaultSettings;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageSerializer"/> class that, in addition to the
        /// default set, allows the specified types to be deserialized as <see cref="EventMessage.Parameter"/>.
        /// </summary>
        /// <param name="allowedParameterTypes">Additional types that are allowed to be deserialized.</param>
        public MessageSerializer(IEnumerable<Type> allowedParameterTypes)
        {
            _settings = CreateSettings(allowedParameterTypes);
        }

        private static JsonSerializerSettings CreateSettings(IEnumerable<Type> allowedParameterTypes)
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new MessageContractResolver(),
                SerializationBinder = new EventMessageSerializationBinder(allowedParameterTypes)
            };
        }

        /// <summary>
        /// Serializes the specified <see cref="EventMessage"/> to JSON.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns></returns>
        public virtual string Serialize(EventMessage message)
        {
            return JsonConvert.SerializeObject(message, _settings);
        }

        /// <summary>
        /// Deserializes the specified JSON serialized <see cref="EventMessage"/> to an object.
        /// </summary>
        /// <param name="serializedMessage">The JSON serialized message.</param>
        /// <returns>An <see cref="EventMessage"/> instance.</returns>
        public virtual EventMessage Deserialize(string serializedMessage)
        {
            return JsonConvert.DeserializeObject<EventMessage>(serializedMessage, _settings);
        }

        /// <summary>
        /// Deserializes a JSON serialized <see cref="EventMessage"/> that is wrapped in a serialized SQS notification.
        /// </summary>
        /// <param name="notification">The serialized body of a SQS notification message.</param>
        /// <returns>An <see cref="EventMessage"/> instance.</returns>
        public virtual EventMessage DeserializeNotification(string notification)
        {
            if (notification == null)
                throw new ArgumentNullException("notification");

            dynamic n = JObject.Parse(notification);
            string message = n.Message;
            return Deserialize(message);
        }

    }
}
