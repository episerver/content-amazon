using EPiServer.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Restricts <see cref="TypeNameHandling"/> deserialization of <see cref="EventMessage"/> payloads to a
    /// known safe set of types, to prevent arbitrary type instantiation (CWE-502) from untrusted SNS/SQS messages.
    /// </summary>
    /// <remarks>
    /// A type is allowed when it is a simple value type, a type decorated with <see cref="EventsServiceKnownTypeAttribute"/>,
    /// an explicitly registered type, or an array, <see cref="List{T}"/> or <see cref="Dictionary{TKey, TValue}"/> of allowed types.
    /// </remarks>
    internal class EventMessageSerializationBinder : DefaultSerializationBinder
    {
        private static readonly HashSet<Type> SimpleTypes = new HashSet<Type>
        {
            typeof(object),
            typeof(string),
            typeof(decimal),
            typeof(Guid),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(BoxedValue<Guid>)
        };

        private static readonly HashSet<Type> AllowedGenericTypeDefinitions = new HashSet<Type>
        {
            typeof(List<>),
            typeof(Dictionary<,>)
        };

        private readonly HashSet<Type> _allowedTypes;

        public EventMessageSerializationBinder()
            : this(null)
        {
        }

        public EventMessageSerializationBinder(IEnumerable<Type> additionalAllowedTypes)
        {
            _allowedTypes = new HashSet<Type>(SimpleTypes);
            if (additionalAllowedTypes != null)
            {
                _allowedTypes.UnionWith(additionalAllowedTypes.Where(t => t != null));
            }
        }

        public override Type BindToType(string assemblyName, string typeName)
        {
            var type = base.BindToType(assemblyName, typeName);

            if (type == null || !IsAllowed(type))
            {
                throw new JsonSerializationException($"Deserialization of type '{typeName}, {assemblyName}' is not allowed.");
            }

            return type;
        }

        private bool IsAllowed(Type type)
        {
            if (_allowedTypes.Contains(type) || type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            if (type.IsArray)
            {
                return IsAllowed(type.GetElementType());
            }

            // Generic arguments are resolved by Json.NET without going through this binder and their
            // values are created without any type information, so every argument must be allowed as well.
            if (type.IsGenericType)
            {
                return AllowedGenericTypeDefinitions.Contains(type.GetGenericTypeDefinition())
                    && type.GetGenericArguments().All(IsAllowed);
            }

            return type.IsDefined(typeof(EventsServiceKnownTypeAttribute), false);
        }
    }
}
