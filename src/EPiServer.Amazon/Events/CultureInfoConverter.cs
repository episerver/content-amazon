using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Converter for <see cref="CultureInfo"/> values, which are represented by their name.
    /// </summary>
    /// <remarks>
    /// A culture that is not available on the receiving server is read as <c>null</c> rather than throwing,
    /// as a single unreadable value should not cost the event system the whole message.
    /// </remarks>
    internal class CultureInfoConverter : JsonConverter<CultureInfo>
    {
        public override CultureInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(reader.GetString());
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
