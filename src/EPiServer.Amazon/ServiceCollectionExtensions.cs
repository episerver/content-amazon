using System;
using EPiServer.Amazon.Blobs;
using EPiServer.Amazon.Events;
using EPiServer.Framework.Blobs;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Configures Amazon event provider and blob provider
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Configure amazon event provider
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="configureOptions">The options to configure</param>
        public static IServiceCollection AddAmazonEventProvider(this IServiceCollection services, Action<AmazonEventClientOptions> configureOptions = null)
        {
            services.AddEventProvider<AmazonEventProvider>();
            if (configureOptions is object)
            {
                services.Configure(configureOptions);
            }
            return services;
        }


        /// <summary>
        /// Configure azure blob provider
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="configureOptions">Optional action to configure blob provider</param>
        public static IServiceCollection AddAmazonBlobProvider(this IServiceCollection services, Action<AmazonBlobClientOptions> configureOptions = null)
        {
            services.Configure<BlobProvidersOptions>(options =>
            {
                options.DefaultProvider = "amazon";
                options.AddProvider<AmazonBlobProvider>("amazon");
            });

            if (configureOptions is object)
            {
                services.Configure(configureOptions);
            }

            return services;
        }
    }
}
