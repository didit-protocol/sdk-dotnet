using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Didit
{
    /// <summary>Configuration of a <see cref="DiditClient"/>.</summary>
    public sealed class DiditClientOptions
    {
        /// <summary>The default verification API origin.</summary>
        public static readonly Uri DefaultBaseUrl = new("https://verification.didit.me");

        /// <summary>The default account management API base.</summary>
        public static readonly Uri DefaultAccountBaseUrl = new("https://apx.didit.me/auth/v2");

        /// <summary>
        /// Your application's API key (header <c>x-api-key</c>). Falls back to the <c>DIDIT_API_KEY</c> environment
        /// variable. Operations that need it throw <see cref="ConfigurationException"/> when it is missing.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>Verification API origin. Defaults to <c>https://verification.didit.me</c>. The API key is only ever sent here.</summary>
        public Uri BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>Account management API base. Defaults to <c>https://apx.didit.me/auth/v2</c>.</summary>
        public Uri AccountBaseUrl { get; set; } = DefaultAccountBaseUrl;

        /// <summary>
        /// Organization id used by application-scoped operations (paths under
        /// <c>/v3/organization/{organization_id}/application/{application_id}/</c>). Falls back to <c>DIDIT_ORGANIZATION_ID</c>.
        /// </summary>
        public string? OrganizationId { get; set; }

        /// <summary>Application id used by application-scoped operations. Falls back to <c>DIDIT_APPLICATION_ID</c>.</summary>
        public string? ApplicationId { get; set; }

        /// <summary>
        /// Bearer token for the account management operations that need one, as returned by
        /// <c>client.Account.LoginAsync</c> or <c>client.Account.VerifyEmailAsync</c>.
        /// </summary>
        public string? AccountToken { get; set; }

        /// <summary>Timeout of each attempt. Defaults to 60 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>How many times a failed request is retried (see the README for which failures are retried). Defaults to 2.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Headers added to every request.</summary>
        public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The <see cref="System.Net.Http.HttpClient"/> to send requests with. When <c>null</c>, the client creates and owns one.
        /// A supplied client is never disposed by the SDK, and its own <see cref="HttpClient.Timeout"/> also applies.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// Debug log sink. Receives one line per attempt with method, path, status, duration and request id; never bodies or
        /// headers. When <c>null</c> and the <c>DIDIT_LOG</c> environment variable is <c>debug</c>, lines go to standard error.
        /// </summary>
        public Action<string>? Logger { get; set; }

        internal DiditClientOptions Clone() => new()
        {
            ApiKey = ApiKey,
            BaseUrl = BaseUrl,
            AccountBaseUrl = AccountBaseUrl,
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            AccountToken = AccountToken,
            Timeout = Timeout,
            MaxRetries = MaxRetries,
            DefaultHeaders = new Dictionary<string, string>(DefaultHeaders, StringComparer.OrdinalIgnoreCase),
            HttpClient = HttpClient,
            Logger = Logger,
        };
    }

    /// <summary>Overrides of the client options for a single request. Every property left <c>null</c> keeps the client's value.</summary>
    public sealed class RequestOptions
    {
        /// <summary>API key for this request.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Verification API origin for this request.</summary>
        public Uri? BaseUrl { get; set; }

        /// <summary>Account management API base for this request.</summary>
        public Uri? AccountBaseUrl { get; set; }

        /// <summary>Organization id for an application-scoped operation.</summary>
        public string? OrganizationId { get; set; }

        /// <summary>Application id for an application-scoped operation.</summary>
        public string? ApplicationId { get; set; }

        /// <summary>Account management bearer token.</summary>
        public string? AccountToken { get; set; }

        /// <summary>Timeout of each attempt.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Maximum number of retries.</summary>
        public int? MaxRetries { get; set; }

        /// <summary>
        /// Value of the <c>Idempotency-Key</c> header. Operations that accept it return the stored response when the same key is
        /// replayed, so a <c>POST</c> can be retried safely by your own code.
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>Extra headers for this request.</summary>
        public IDictionary<string, string>? Headers { get; set; }
    }
}
