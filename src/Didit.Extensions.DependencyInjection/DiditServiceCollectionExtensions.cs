using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Didit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Registers <see cref="DiditClient"/> with dependency injection.</summary>
    public static class DiditServiceCollectionExtensions
    {
        /// <summary>The name of the <see cref="HttpClient"/> the client is created with (configure it through the returned builder).</summary>
        public const string HttpClientName = "Didit";

        /// <summary>
        /// Registers a singleton <see cref="DiditClient"/> whose <see cref="HttpClient"/> comes from <see cref="IHttpClientFactory"/>.
        /// Options not set by <paramref name="configure"/> fall back to the <c>DIDIT_*</c> environment variables. Debug lines go to
        /// <see cref="ILogger{DiditClient}"/> at Debug level unless <see cref="DiditClientOptions.Logger"/> is set.
        /// </summary>
        /// <returns>The builder of the named HttpClient, to add handlers or change its configuration.</returns>
        public static IHttpClientBuilder AddDidit(this IServiceCollection services, Action<DiditClientOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = services.AddOptions<DiditClientOptions>();
            if (configure is not null)
            {
                options.Configure(configure);
            }

            return Register(services);
        }

        /// <summary>
        /// Registers a singleton <see cref="DiditClient"/> configured from a configuration section, for example
        /// <c>builder.Configuration.GetSection("Didit")</c> with keys <c>ApiKey</c>, <c>OrganizationId</c>, <c>ApplicationId</c>,
        /// <c>BaseUrl</c>, <c>Timeout</c> and <c>MaxRetries</c>.
        /// </summary>
        /// <returns>The builder of the named HttpClient.</returns>
        public static IHttpClientBuilder AddDidit(this IServiceCollection services, IConfiguration configuration, Action<DiditClientOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var options = services.AddOptions<DiditClientOptions>().Bind(configuration);
            if (configure is not null)
            {
                options.Configure(configure);
            }

            return Register(services);
        }

        private static IHttpClientBuilder Register(IServiceCollection services)
        {
            var builder = services
                .AddHttpClient(HttpClientName)
                .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(CreateHandler);

            services.TryAddSingleton(provider =>
            {
                var configured = provider.GetRequiredService<IOptions<DiditClientOptions>>().Value;
                var httpClient = configured.HttpClient ?? provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
                var logger = provider.GetService<ILogger<DiditClient>>();
                var options = new DiditClientOptions
                {
                    ApiKey = configured.ApiKey,
                    BaseUrl = configured.BaseUrl,
                    AccountBaseUrl = configured.AccountBaseUrl,
                    OrganizationId = configured.OrganizationId,
                    ApplicationId = configured.ApplicationId,
                    AccountToken = configured.AccountToken,
                    Timeout = configured.Timeout,
                    MaxRetries = configured.MaxRetries,
                    DefaultHeaders = configured.DefaultHeaders,
                    HttpClient = httpClient,
                    Logger = configured.Logger ?? (logger is not null && logger.IsEnabled(LogLevel.Debug) ? line => logger.LogDebug("{DiditLine}", line) : null),
                };
                return new DiditClient(options);
            });
            return builder;
        }

        private static HttpMessageHandler CreateHandler()
        {
#if NET8_0_OR_GREATER
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
#else
            return new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
#endif
        }
    }
}
