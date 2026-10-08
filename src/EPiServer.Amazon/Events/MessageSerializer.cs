using EPiServer.Events;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Responsible for serializing and deserializing <see cref="EventMessage"/> objects to Amazon SNS and SQS.
    /// </summary>
    /// <remarks>
    /// <see cref="EventMessage.Parameter"/> is of type <see cref="object"/>, so a concrete type is needed to
    /// deserialize it. That type is resolved from the event types registered in <see cref="EventsOptions"/>,
    /// keyed on <see cref="EventMessage.EventId"/>, rather than from a type name carried by the message. A
    /// received message can therefore only ever instantiate a type the application has itself registered.
    /// </remarks>
    public class MessageSerializer
    {
        /// <summary>
        /// Used to write messages, and to read everything but the event parameter. Deliberately without an
        /// <see cref="ObjectToInferredTypesConverter"/>: writing needs no help, as System.Text.Json writes a
        /// member of type <see cref="object"/> using its runtime type.
        /// </summary>
        /// <remarks>
        /// Enum values are written by name rather than by number, as a number is positional: inserting a member
        /// into an enum silently changes the meaning of every value after it for a server that has yet to be
        /// redeployed.
        /// </remarks>
        private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(),
                new CultureInfoConverter()
            }
        };

        private readonly Dictionary<Guid, JsonSerializerOptions> _optionsByEventId = new Dictionary<Guid, JsonSerializerOptions>();

        private readonly JsonSerializerOptions _unregisteredEventOptions = new JsonSerializerOptions(DefaultOptions)
        {
            Converters = { new ObjectToInferredTypesConverter(null, DefaultOptions) }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageSerializer"/> class.
        /// </summary>
        /// <param name="eventsOptions">The options holding the event types that may be published.</param>
        /// <exception cref="System.ArgumentNullException" />
        public MessageSerializer(IOptions<EventsOptions> eventsOptions)
        {
            if (eventsOptions == null)
                throw new ArgumentNullException("eventsOptions");

            foreach (var registration in eventsOptions.Value.Events)
            {
                _optionsByEventId[registration.Id] = new JsonSerializerOptions(DefaultOptions)
                {
                    Converters = { new ObjectToInferredTypesConverter(registration.DataType, DefaultOptions) }
                };
            }
        }

        /// <summary>
        /// Serializes the specified <see cref="EventMessage"/> to JSON.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns></returns>
        public virtual string Serialize(EventMessage message)
        {
            return JsonSerializer.Serialize(message, DefaultOptions);
        }

        /// <summary>
        /// Deserializes the specified JSON serialized <see cref="EventMessage"/> to an object.
        /// </summary>
        /// <remarks>
        /// The parameter of an event that this application has not registered is read as <c>null</c>, as there
        /// is no type to read it as. The message is still delivered, so that the event system can account for it.
        /// </remarks>
        /// <param name="serializedMessage">The JSON serialized message.</param>
        /// <returns>An <see cref="EventMessage"/> instance.</returns>
        /// <exception cref="System.ArgumentNullException" />
        public virtual EventMessage Deserialize(string serializedMessage)
        {
            if (serializedMessage == null)
                throw new ArgumentNullException("serializedMessage");

            // The event id decides the type of the parameter, so it has to be read before the message itself.
            using var document = JsonDocument.Parse(serializedMessage);
            return document.RootElement.Deserialize<EventMessage>(SelectOptions(document.RootElement));
        }

        /// <summary>
        /// Deserializes a JSON serialized <see cref="EventMessage"/> that is wrapped in a serialized SQS notification.
        /// </summary>
        /// <param name="notification">The serialized body of a SQS notification message.</param>
        /// <returns>An <see cref="EventMessage"/> instance.</returns>
        /// <exception cref="System.ArgumentNullException" />
        public virtual EventMessage DeserializeNotification(string notification)
        {
            if (notification == null)
                throw new ArgumentNullException("notification");

            using var document = JsonDocument.Parse(notification);
            return Deserialize(document.RootElement.GetProperty("Message").GetString());
        }

        private JsonSerializerOptions SelectOptions(JsonElement message)
        {
            if (message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("EventId", out var eventId)
                && eventId.ValueKind == JsonValueKind.String
                && eventId.TryGetGuid(out var id)
                && _optionsByEventId.TryGetValue(id, out var options))
            {
                return options;
            }

            return _unregisteredEventOptions;
        }
    }
}
