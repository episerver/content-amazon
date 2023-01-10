using System;
using System.Reflection;
using EPiServer.Events;
using Newtonsoft.Json.Serialization;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Contract resolver that deals with the property "Parameter" that is of type <see cref="object"/>
    /// </summary>
    internal class MessageContractResolver : DefaultContractResolver
    {
        protected override IValueProvider CreateMemberValueProvider(MemberInfo member)
        {
            if (member.DeclaringType == typeof(EventMessage) && member.Name == "Parameter")
            {
                return new ParameterValueProvider(member);
            }

            return base.CreateMemberValueProvider(member);
        }
    }

    /// <summary>
    /// A value provider that takes care of values that Json does not understand, currently only <see cref="Guid"/>
    /// </summary>
    internal class ParameterValueProvider : IValueProvider
    {
        private readonly IValueProvider _underlyingValueProvider;

        public ParameterValueProvider(MemberInfo memberInfo)
        {
            _underlyingValueProvider = new ReflectionValueProvider(memberInfo);
        }

        public void SetValue(object target, object value)
        {
            if (value != null && value.GetType() == typeof(BoxedValue<Guid>))
            {
                _underlyingValueProvider.SetValue(target, ((BoxedValue<Guid>)value).Value);
            }
            else
            {
                _underlyingValueProvider.SetValue(target, value);
            }
        }

        public object GetValue(object target)
        {
            var value = _underlyingValueProvider.GetValue(target);
            if (value != null && value.GetType() == typeof(Guid))
            {
                return new BoxedValue<Guid>() { Value = ((Guid)value) };
            }
            return value;
        }
    }

    /// <summary>
    /// Class that is used to box values such as <see cref="Guid"/>
    /// </summary>
    public class BoxedValue<T> where T: struct
    {
        public T Value { get; set; }
    }
}
