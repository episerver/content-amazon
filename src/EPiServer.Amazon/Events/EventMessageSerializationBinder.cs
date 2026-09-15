using EPiServer.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Restricts <see cref="TypeNameHandling"/> deserialization of <see cref="EventMessage"/> payloads to a
    /// known safe set of types, to prevent arbitrary type instantiation (CWE-502) from untrusted SNS/SQS messages.
    /// </summary>
    internal class EventMessageSerializationBinder : DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            var type = base.BindToType(assemblyName, typeName);

            if (!IsAllowed(type))
            {
                throw new JsonSerializationException($"Deserialization of type '{type}' is not allowed.");
            }

            return type;
        }

        private static bool IsAllowed(Type type)
        {
            if (type == typeof(BoxedValue<Guid>))
            {
                return true;
            }

            // Only types owned by EPiServer/Optimizely assemblies are trusted for polymorphic deserialization.
            var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            return assemblyName.Equals("EPiServer.Events", StringComparison.Ordinal)
                || assemblyName.StartsWith("EPiServer.Events.", StringComparison.Ordinal);
        }
    }
}
