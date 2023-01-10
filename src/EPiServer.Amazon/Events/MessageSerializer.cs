using EPiServer.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Responsible for serializing and deserializing <see cref="EventMessage"/> objects to Amazon SNS and SQS.
    /// </summary>
    public class MessageSerializer
    {
        private static readonly JsonSerializerSettings DefaultSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new MessageContractResolver()
        };

        /// <summary>
        /// Serializes the specified <see cref="EventMessage"/> to JSON.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns></returns>
        public virtual string Serialize(EventMessage message)
        {
            return JsonConvert.SerializeObject(message, DefaultSettings);
        }

        /// <summary>
        /// Deserializes the specified JSON serialized <see cref="EventMessage"/> to an object.
        /// </summary>
        /// <param name="serializedMessage">The JSON serialized message.</param>
        /// <returns>An <see cref="EventMessage"/> instance.</returns>
        public virtual EventMessage Deserialize(string serializedMessage)
        {
            return JsonConvert.DeserializeObject<EventMessage>(serializedMessage, DefaultSettings);
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
