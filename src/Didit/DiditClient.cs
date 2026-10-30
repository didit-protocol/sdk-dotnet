using System;
using System.Net.Http;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// The Didit API client. Thread-safe and reusable: create one per process (or register it with
    /// <c>services.AddDidit()</c>) and share it.
    /// </summary>
    /// <example>
    /// <code>
    /// var client = new DiditClient(new DiditClientOptions { ApiKey = "..." });
    /// var session = await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "..." });
    /// </code>
    /// </example>
    public sealed partial class DiditClient : IDisposable
    {
        private readonly DiditClientOptions _options;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        /// <summary>Creates a client configured from the <c>DIDIT_*</c> environment variables.</summary>
        public DiditClient()
            : this(new DiditClientOptions())
        {
        }

        /// <summary>Creates a client with an API key.</summary>
        public DiditClient(string apiKey)
            : this(new DiditClientOptions { ApiKey = apiKey })
        {
        }

        /// <summary>Creates a client from options. Missing values fall back to the <c>DIDIT_*</c> environment variables.</summary>
        public DiditClient(DiditClientOptions options)
            : this(options, sharedHttpClient: null)
        {
        }

        private DiditClient(DiditClientOptions options, HttpClient? sharedHttpClient)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Clone();
            _options.ApiKey = FirstNonEmpty(_options.ApiKey, Environment.GetEnvironmentVariable("DIDIT_API_KEY"));
            _options.OrganizationId = FirstNonEmpty(_options.OrganizationId, Environment.GetEnvironmentVariable("DIDIT_ORGANIZATION_ID"));
            _options.ApplicationId = FirstNonEmpty(_options.ApplicationId, Environment.GetEnvironmentVariable("DIDIT_APPLICATION_ID"));
            Validate(_options);

            if (_options.HttpClient is not null)
            {
                _httpClient = _options.HttpClient;
            }
            else if (sharedHttpClient is not null)
            {
                _httpClient = sharedHttpClient;
            }
            else
            {
                _httpClient = CreateHttpClient();
                _ownsHttpClient = true;
            }

            Pipeline = new HttpPipeline(_options, _httpClient);
            InitializeResources();
        }

        /// <summary>The effective configuration (a copy; changing it does not affect the client).</summary>
        public DiditClientOptions Options => _options.Clone();

        internal HttpPipeline Pipeline { get; }

        /// <summary>
        /// Returns a new client that shares this client's connection pool, with the options changed by <paramref name="configure"/>.
        /// </summary>
        public DiditClient WithOptions(Action<DiditClientOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = _options.Clone();
            configure(options);
            return new DiditClient(options, _httpClient);
        }

        /// <summary>Disposes the <see cref="HttpClient"/> the client created. A client passed in the options is left alone.</summary>
        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        private static string? FirstNonEmpty(string? value, string? fallback) =>
            !string.IsNullOrEmpty(value) ? value : (!string.IsNullOrEmpty(fallback) ? fallback : null);

        private static void Validate(DiditClientOptions options)
        {
            if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri)
            {
                throw new ConfigurationException("DiditClientOptions.BaseUrl must be an absolute URL.");
            }

            if (options.AccountBaseUrl is null || !options.AccountBaseUrl.IsAbsoluteUri)
            {
                throw new ConfigurationException("DiditClientOptions.AccountBaseUrl must be an absolute URL.");
            }

            if (options.MaxRetries < 0)
            {
                throw new ConfigurationException("DiditClientOptions.MaxRetries must be zero or more.");
            }

            if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ConfigurationException("DiditClientOptions.Timeout must be positive.");
            }

            options.DefaultHeaders ??= new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static HttpClient CreateHttpClient()
        {
#if NET8_0_OR_GREATER
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
            };
#else
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            };
#endif
            return new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }
    }
}
