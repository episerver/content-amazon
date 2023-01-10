using System;
using System.Collections.Specialized;
using System.Globalization;

namespace EPiServer.Amazon
{
    internal static class NameValueCollectionExtensions
    {
        /// <summary>
        /// Gets the value and if found removes it from a collection.
        /// </summary>
        /// <param name="collection">The configuration.</param>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public static string GetAndRemove(this NameValueCollection collection, string key)
        {
            var val = collection[key];
            collection.Remove(key);
            return val != null ? val.Trim() : null;
        }

        public static void ThrowIfNotEmpty(this NameValueCollection config, Type owner, string name)
        {
            if (config.Count > 0)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "The configuration of the {0} '{1}' contained the following unrecognized keys: '{2}'.", owner.Name, name, string.Join("', '", config.AllKeys)));
            }
        }

    }
}
