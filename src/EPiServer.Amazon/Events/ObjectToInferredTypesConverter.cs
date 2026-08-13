using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Converter for the property "Parameter" of <see cref="EPiServer.Events.EventMessage"/>, which is of
    /// type <see cref="object"/> and may hold a payload of any registered event data type.
    /// </summary>
    /// <remarks>
    /// Values that are represented by a single JSON token are read as their inferred type. Objects are read
    /// as the type registered for the event, or skipped when the event is unknown to this application.
    /// </remarks>
    internal class ObjectToInferredTypesConverter : JsonConverter<object>
    {
        private readonly Type _registeredType;
        private readonly JsonSerializerOptions _registeredTypeOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectToInferredTypesConverter"/> class.
        /// </summary>
        /// <param name="registeredType">The type registered for the event, or <c>null</c> when the event is not registered.</param>
        /// <param name="registeredTypeOptions">
        /// The options used to read <paramref name="registeredType"/>. These must not include this converter,
        /// so that a nested property of type <see cref="object"/> is not read as the event parameter type.
        /// </param>
        public ObjectToInferredTypesConverter(Type registeredType, JsonSerializerOptions registeredTypeOptions)
        {
            _registeredType = registeredType;
            _registeredTypeOptions = registeredTypeOptions;
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    // Separate returns, so that a whole number is not widened to a double by the compiler.
                    if (reader.TryGetInt64(out var integer))
                    {
                        return integer;
                    }
                    return reader.GetDouble();
                case JsonTokenType.String:
                    // A Guid is written as a string, and is the one struct that events pass without registering it.
                    if (reader.TryGetGuid(out var guid))
                    {
                        return guid;
                    }
                    return reader.GetString();
                case JsonTokenType.StartObject:
                    return ReadRegisteredType(ref reader);
                default:
                    throw new JsonException(string.Format(CultureInfo.InvariantCulture, "Unsupported token '{0}' for an event message parameter.", reader.TokenType));
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }

        private object ReadRegisteredType(ref Utf8JsonReader reader)
        {
            if (_registeredType == null)
            {
                // The event was not registered by this application, so there is no type to read the
                // parameter as. The rest of the message is still of interest to the event system.
                reader.Skip();
                return null;
            }

            return JsonSerializer.Deserialize(ref reader, _registeredType, _registeredTypeOptions);
        }
    }
}
