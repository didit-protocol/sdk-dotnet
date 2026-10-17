using System;
using System.Net.Http;

namespace Didit
{
    /// <summary>Configuration of an <see cref="Authentication"/> instance.</summary>
    public sealed class AuthenticationOptions
    {
        /// <summary>
        /// The <see cref="System.Net.Http.HttpClient"/> to send requests with. When <c>null</c>, the instance creates and owns one.
        /// A supplied client is never disposed by the SDK, and its own <see cref="HttpClient.Timeout"/> also applies.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>Timeout of each request to the issuer. Defaults to 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Tolerance applied to <c>exp</c>, <c>nbf</c> and <c>iat</c> when verifying tokens. Defaults to 60 seconds.</summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// The OAuth <c>client_id</c> sent to the token endpoint. Defaults to the application id, which the issuer derives from
        /// its own discovery document; set it only when your issuer differs.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>Clock used for token lifetimes and cache expiry. Defaults to <see cref="DateTimeOffset.UtcNow"/>; replace it in tests.</summary>
        public Func<DateTimeOffset>? UtcNow { get; set; }
    }
}
