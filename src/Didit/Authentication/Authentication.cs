using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// Didit Authentication for your backend: verifies the access and ID tokens your application's issuer signs, runs the
    /// PKCE code exchange and refresh, checks and revokes sessions, and drives step-up verification. It talks only to the
    /// issuer (<c>https://&lt;slug&gt;.auth.didit.me</c> or your own domain) and never takes your Didit API key.
    /// </summary>
    /// <remarks>
    /// Thread-safe and reusable: create one instance per issuer and share it. The discovery document and the JWKS are fetched on
    /// first use and cached for the issuer's <c>Cache-Control: max-age</c>.
    /// </remarks>
    public sealed class Authentication : IDisposable
    {
        private const string DiscoveryPath = ".well-known/openid-configuration";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly AuthenticationTransport _transport;
        private readonly IssuerMetadataCache _metadata;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly TimeSpan _clockSkew;
        private readonly string _baseUrl;

        /// <summary>Creates an instance for an issuer URL, for example <c>https://acme.auth.didit.me</c>.</summary>
        /// <param name="issuer">The application's issuer, exactly as it appears in the tokens' <c>iss</c> claim.</param>
        /// <param name="options">Optional configuration.</param>
        /// <exception cref="ArgumentException"><paramref name="issuer"/> is not an absolute https URL.</exception>
        public Authentication(string issuer, AuthenticationOptions? options = null)
        {
            var valid = IssuerComparer.Validate(issuer, out var error);
            if (valid is null)
            {
                throw new ArgumentException(error, nameof(issuer));
            }

            options ??= new AuthenticationOptions();
            if (options.Timeout <= TimeSpan.Zero && options.Timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException("AuthenticationOptions.Timeout must be positive.", nameof(options));
            }

            if (options.ClockSkew < TimeSpan.Zero)
            {
                throw new ArgumentException("AuthenticationOptions.ClockSkew must not be negative.", nameof(options));
            }

            Issuer = valid;
            _baseUrl = IssuerComparer.Trim(valid) + "/";
            _clockSkew = options.ClockSkew;
            _utcNow = options.UtcNow ?? (() => DateTimeOffset.UtcNow);
            if (options.HttpClient is not null)
            {
                _httpClient = options.HttpClient;
            }
            else
            {
                _httpClient = CreateHttpClient();
                _ownsHttpClient = true;
            }

            _transport = new AuthenticationTransport(_httpClient, options.Timeout);
            _metadata = new IssuerMetadataCache(_transport, valid, new Uri(_baseUrl + DiscoveryPath), _utcNow);
        }

        /// <summary>The issuer URL this instance trusts.</summary>
        public string Issuer { get; }

        /// <summary>Creates an instance for the issuer a publishable key (<c>pk_live_...</c> or <c>pk_test_...</c>) carries.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="publishableKey"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException">The key is not a valid publishable key.</exception>
        public static Authentication FromPublishableKey(string publishableKey, AuthenticationOptions? options = null) =>
            new(PublishableKey.Parse(publishableKey).Issuer, options);

        /// <summary>Parses a publishable key; see <see cref="PublishableKey.Parse"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="publishableKey"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException">The key is not a valid publishable key.</exception>
        public static PublishableKey ParsePublishableKey(string publishableKey) => PublishableKey.Parse(publishableKey);

        /// <summary>Creates a PKCE verifier and its S256 challenge for an authorization request.</summary>
        /// <remarks>Send <see cref="PkcePair.CodeChallenge"/> with the authorization request and keep <see cref="PkcePair.CodeVerifier"/> for <see cref="ExchangeCodeAsync"/>.</remarks>
        public static PkcePair CreatePkce() => PkcePair.FromVerifier(RandomToken());

        /// <summary>Creates a random, URL-safe <c>state</c> value (256 bits) that binds the callback to the browser session.</summary>
        public static string CreateState() => RandomToken();

        /// <summary>Creates a random, URL-safe <c>nonce</c> (256 bits) to send with the authorization request and check with <see cref="VerifyIdTokenAsync"/>.</summary>
        public static string CreateNonce() => RandomToken();

        /// <summary>Returns the issuer's OpenID Connect discovery document (cached).</summary>
        /// <exception cref="ApiStatusException">The issuer answered with a non-2xx status.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public Task<OpenIdConfiguration> GetDiscoveryDocumentAsync(CancellationToken cancellationToken = default) =>
            _metadata.GetDiscoveryAsync(cancellationToken);

        /// <summary>
        /// Verifies an access token (from an <c>Authorization: Bearer</c> header) and returns its claims. Checks: ES256 signature
        /// with a key from the issuer's JWKS (every other algorithm is rejected), header <c>typ</c> <c>at+jwt</c>, <c>iss</c>
        /// equal to <see cref="Issuer"/>, <c>aud</c> when <paramref name="audience"/> is given, and <c>exp</c>, <c>nbf</c> and
        /// <c>iat</c> within <see cref="AuthenticationOptions.ClockSkew"/>.
        /// </summary>
        /// <remarks>
        /// Verification is local, so a session revoked in the last few minutes still has valid access tokens until they expire
        /// (5 minutes). Call <see cref="GetSessionAsync"/> before sensitive actions for an instant revocation check.
        /// </remarks>
        /// <param name="token">The compact JWT, without the <c>Bearer</c> prefix.</param>
        /// <param name="audience">When given, <c>aud</c> must contain it.</param>
        /// <param name="cancellationToken">Cancels a discovery or JWKS fetch.</param>
        /// <exception cref="TokenVerificationException">The token is not valid; <see cref="TokenVerificationException.Reason"/> says why.</exception>
        /// <exception cref="ApiStatusException">The discovery document or JWKS could not be fetched.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task<AccessTokenClaims> VerifyAccessTokenAsync(string token, string? audience = null, CancellationToken cancellationToken = default)
        {
            if (token is null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            var jwt = await VerifyAsync(token, idToken: false, string.IsNullOrEmpty(audience) ? null : audience, cancellationToken).ConfigureAwait(false);
            return AccessTokenClaims.FromPayload(jwt.Payload);
        }

        /// <summary>
        /// Verifies an ID token from the token response and returns its claims: the checks of <see cref="VerifyAccessTokenAsync"/>
        /// (with <c>typ</c> absent or <c>JWT</c>), <c>aud</c> containing <paramref name="audience"/>, and <c>nonce</c> equal to
        /// <paramref name="nonce"/>.
        /// </summary>
        /// <param name="token">The <c>id_token</c> from <see cref="AuthTokenResponse.IdToken"/>.</param>
        /// <param name="nonce">The nonce you sent with the authorization request (see <see cref="CreateNonce"/>).</param>
        /// <param name="audience">Your client id (the application id).</param>
        /// <param name="cancellationToken">Cancels a discovery or JWKS fetch.</param>
        /// <exception cref="TokenVerificationException">The token is not valid; <see cref="TokenVerificationException.Reason"/> says why.</exception>
        /// <exception cref="ApiStatusException">The discovery document or JWKS could not be fetched.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task<IdTokenClaims> VerifyIdTokenAsync(string token, string nonce, string audience, CancellationToken cancellationToken = default)
        {
            if (token is null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (string.IsNullOrEmpty(nonce))
            {
                throw new ArgumentException("The nonce sent with the authorization request is required.", nameof(nonce));
            }

            if (string.IsNullOrEmpty(audience))
            {
                throw new ArgumentException("The audience (your client id) is required.", nameof(audience));
            }

            var jwt = await VerifyAsync(token, idToken: true, audience, cancellationToken).ConfigureAwait(false);
            if (!FixedTimeEquals(AuthJson.GetString(jwt.Payload, "nonce"), nonce))
            {
                throw new TokenVerificationException(TokenVerificationReason.InvalidNonce, "The ID token nonce does not match the authorization request.");
            }

            return IdTokenClaims.FromPayload(jwt.Payload);
        }

        /// <summary>
        /// Exchanges an authorization code for tokens (<c>POST token/</c>, <c>grant_type=authorization_code</c>). Codes are single-use
        /// and expire 60 seconds after sign-in; check the callback's <c>state</c> before calling this.
        /// </summary>
        /// <param name="code">The <c>code</c> query parameter of the callback.</param>
        /// <param name="codeVerifier">The <see cref="PkcePair.CodeVerifier"/> created for this authorization request.</param>
        /// <param name="redirectUri">The exact <c>redirect_uri</c> of the authorization request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The tokens. Verify <see cref="AuthTokenResponse.IdToken"/> with <see cref="VerifyIdTokenAsync"/> before trusting it.</returns>
        /// <exception cref="BadRequestException">The code is invalid, used, expired, or does not match the verifier or redirect URI (<c>invalid_grant</c>).</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached; the request is not retried.</exception>
        public async Task<AuthTokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        {
            RequireText(code, nameof(code));
            RequireText(codeVerifier, nameof(codeVerifier));
            RequireText(redirectUri, nameof(redirectUri));
            var form = new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("code_verifier", codeVerifier),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
            };
            return await PostTokenAsync(form, code, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Rotates a refresh token for new access, ID and refresh tokens (<c>POST token/</c>, <c>grant_type=refresh_token</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Refresh tokens are single-use. <b>Reusing a consumed refresh token revokes the session</b> and everything issued from it,
        /// so serialise refreshes for one session (for example with a per-session lock) and store the new
        /// <see cref="AuthTokenResponse.RefreshToken"/> before using it. Two concurrent refreshes with the same token sign the person out.
        /// </para>
        /// <para>The request is never retried, for the same reason.</para>
        /// </remarks>
        /// <param name="refreshToken">The current refresh token.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <exception cref="BadRequestException">The refresh token is unknown, used or its session ended (<c>invalid_grant</c>).</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            RequireText(refreshToken, nameof(refreshToken));
            var form = new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
            };
            return await PostTokenAsync(form, refreshToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the session behind an access token (<c>GET session/</c>). Unlike <see cref="VerifyAccessTokenAsync"/> this asks the
        /// issuer, so it is the instant check: it fails as soon as the session is revoked, expired, idle or the token was replaced by a refresh.
        /// </summary>
        /// <param name="accessToken">A person's access token (agent tokens are refused).</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <exception cref="AuthenticationException">The token or its session is no longer valid (401 <c>invalid_token</c>).</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task<AuthSession> GetSessionAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            RequireText(accessToken, nameof(accessToken));
            var response = await _transport.SendAsync(HttpMethod.Get, RuntimeUrl("session/"), null, accessToken, accessToken, cancellationToken).ConfigureAwait(false);
            return AuthSession.FromJson(AuthJson.Parse(response.Body, "session response"));
        }

        /// <summary>Revokes the session behind an access token (<c>DELETE session/</c>): signs the person out everywhere that session is used.</summary>
        /// <param name="accessToken">An access token of the session to revoke.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <exception cref="AuthenticationException">The token or its session is already invalid (401 <c>invalid_token</c>).</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task RevokeSessionAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            RequireText(accessToken, nameof(accessToken));
            await _transport.SendAsync(HttpMethod.Delete, RuntimeUrl("session/"), null, accessToken, accessToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asks whether a sensitive action needs a step-up verification (<c>POST step-up/</c>). When
        /// <see cref="AuthStepUp.Required"/> is <c>true</c>, send the person to <see cref="AuthStepUp.VerificationUrl"/>; they come
        /// back to <paramref name="returnTo"/> with <c>step_up=&lt;id&gt;</c>. Then read <see cref="RetrieveStepUpAsync"/> and act only
        /// when its status is <see cref="AuthStepUpStatus.Approved"/>.
        /// </summary>
        /// <param name="accessToken">The person's access token.</param>
        /// <param name="action"><c>payout</c>, <c>bank</c> or <c>contact</c>.</param>
        /// <param name="returnTo">Where the person returns after verifying; must be one of the application's registered redirect URIs.</param>
        /// <param name="amount">The payout amount as a decimal string with at most two decimals, for example <c>"250.00"</c>; required for <c>payout</c>.</param>
        /// <param name="currency">ISO 4217 code of <paramref name="amount"/>, for example <c>EUR</c>; defaults to the payout rule's currency.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <exception cref="BadRequestException">Invalid fields, or <paramref name="returnTo"/> is not registered.</exception>
        /// <exception cref="AuthenticationException">The access token or its session is no longer valid.</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer, for example when verification is unavailable (step-ups never fail open).</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached; the request is not retried.</exception>
        public async Task<AuthStepUp> CreateStepUpAsync(
            string accessToken,
            string action,
            string returnTo,
            string? amount = null,
            string? currency = null,
            CancellationToken cancellationToken = default)
        {
            RequireText(accessToken, nameof(accessToken));
            RequireText(action, nameof(action));
            RequireText(returnTo, nameof(returnTo));
            var body = WriteJson(writer =>
            {
                writer.WriteString("action", action);
                if (amount is not null)
                {
                    writer.WriteString("amount", amount);
                }

                if (currency is not null)
                {
                    writer.WriteString("currency", currency);
                }

                writer.WriteString("return_to", returnTo);
            });
            var response = await _transport.SendAsync(HttpMethod.Post, RuntimeUrl("step-up/"), () => JsonContent(body), accessToken, accessToken, cancellationToken).ConfigureAwait(false);
            return AuthStepUp.FromJson(AuthJson.Parse(response.Body, "step-up response"));
        }

        /// <summary>
        /// Reads a step-up's current status (<c>GET step-up/{id}/</c>) with an access token of the session that created it.
        /// Proceed with the action only when <see cref="AuthStepUp.Status"/> is <see cref="AuthStepUpStatus.Approved"/>; the
        /// <c>status</c> query parameter on the return URL is only a hint and must never be trusted.
        /// </summary>
        /// <param name="accessToken">An access token of the session that created the step-up.</param>
        /// <param name="stepUpId">The <c>step_up</c> query parameter of the return URL, or <see cref="AuthStepUp.Id"/>.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <exception cref="NotFoundException">No such step-up for this session.</exception>
        /// <exception cref="AuthenticationException">The access token or its session is no longer valid.</exception>
        /// <exception cref="ApiStatusException">Another non-2xx answer.</exception>
        /// <exception cref="ApiConnectionException">The issuer could not be reached.</exception>
        public async Task<AuthStepUp> RetrieveStepUpAsync(string accessToken, string stepUpId, CancellationToken cancellationToken = default)
        {
            RequireText(accessToken, nameof(accessToken));
            RequireText(stepUpId, nameof(stepUpId));
            var url = RuntimeUrl("step-up/" + Uri.EscapeDataString(stepUpId) + "/");
            var response = await _transport.SendAsync(HttpMethod.Get, url, null, accessToken, accessToken, cancellationToken).ConfigureAwait(false);
            return AuthStepUp.FromJson(AuthJson.Parse(response.Body, "step-up response"));
        }

        /// <summary>Disposes the <see cref="HttpClient"/> this instance created. A client passed in the options is left alone.</summary>
        public void Dispose()
        {
            _metadata.Dispose();
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        internal static string RandomToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Base64Url.Encode(bytes);
        }

        private static bool FixedTimeEquals(string? left, string right)
        {
            if (left is null)
            {
                return false;
            }

            var a = Encoding.UTF8.GetBytes(left);
            var b = Encoding.UTF8.GetBytes(right);
            var difference = a.Length ^ b.Length;
            for (var i = 0; i < a.Length && i < b.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }

            return difference == 0;
        }

        private static TokenVerificationException Fail(string reason, string message) => new(reason, message);

        private static void RequireText(string? value, string parameterName)
        {
            if (value is null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (value.Length == 0)
            {
                throw new ArgumentException("The value must not be empty.", parameterName);
            }
        }

        private static byte[] WriteJson(Action<Utf8JsonWriter> write)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                write(writer);
                writer.WriteEndObject();
            }

            return stream.ToArray();
        }

        private static ByteArrayContent JsonContent(byte[] body)
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            return content;
        }

        private Uri RuntimeUrl(string path) => new(_baseUrl + path);

        private async Task<AuthTokenResponse> PostTokenAsync(KeyValuePair<string, string>[] form, string secret, CancellationToken cancellationToken)
        {
            var discovery = await _metadata.GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
            var endpoint = discovery.TokenEndpoint ?? RuntimeUrl("token/");
            var response = await _transport.SendAsync(HttpMethod.Post, endpoint, () => new FormUrlEncodedContent(form), null, secret, cancellationToken).ConfigureAwait(false);
            var tokens = AuthTokenResponse.FromJson(AuthJson.Parse(response.Body, "token response"));
            if (tokens.AccessToken.Length == 0)
            {
                throw new DiditException("The token response has no access_token.");
            }

            return tokens;
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
            return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private async Task<JsonWebToken> VerifyAsync(string token, bool idToken, string? audience, CancellationToken cancellationToken)
        {
            var jwt = JsonWebToken.Parse(token);
            var header = jwt.Header;

            // The algorithm is pinned: the header never chooses how the token is checked.
            if (!header.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String || alg.GetString() != "ES256")
            {
                var shown = alg.ValueKind == JsonValueKind.String ? alg.GetString() : "missing";
                throw Fail(TokenVerificationReason.UnsupportedAlgorithm, $"The token algorithm '{shown}' is not allowed; Didit tokens use ES256.");
            }

            if (header.TryGetProperty("crit", out _))
            {
                throw Fail(TokenVerificationReason.Malformed, "The token header has critical extensions this SDK does not understand.");
            }

            if (header.TryGetProperty("kid", out var kidElement) && kidElement.ValueKind != JsonValueKind.String)
            {
                throw Fail(TokenVerificationReason.Malformed, "The token header kid is not a string.");
            }

            CheckType(header, idToken);

            var keys = await _metadata.GetKeysAsync(jwt.KeyId, cancellationToken).ConfigureAwait(false);
            if (keys.Count == 0)
            {
                throw Fail(TokenVerificationReason.UnknownKey, jwt.KeyId is null
                    ? "The issuer's JWKS has no ES256 key."
                    : $"The issuer's JWKS has no key '{jwt.KeyId}'.");
            }

            var verified = false;
            foreach (var key in keys)
            {
                if (key.Verify(jwt.SigningInput, jwt.Signature))
                {
                    verified = true;
                    break;
                }
            }

            if (!verified)
            {
                throw Fail(TokenVerificationReason.InvalidSignature, "The token signature is not valid.");
            }

            CheckClaims(jwt.Payload, idToken, audience);
            return jwt;
        }

        private static void CheckType(JsonElement header, bool idToken)
        {
            string? typ = null;
            if (header.TryGetProperty("typ", out var typElement))
            {
                if (typElement.ValueKind != JsonValueKind.String)
                {
                    throw Fail(TokenVerificationReason.Malformed, "The token header typ is not a string.");
                }

                typ = typElement.GetString();
            }

            if (idToken)
            {
                if (typ is not null && !IsType(typ, "JWT"))
                {
                    throw Fail(TokenVerificationReason.InvalidType, $"The token type '{typ}' is not an ID token.");
                }
            }
            else if (typ is null || !IsType(typ, "at+jwt"))
            {
                throw Fail(TokenVerificationReason.InvalidType, $"The token type '{typ ?? "missing"}' is not an access token (at+jwt).");
            }
        }

        // RFC 7515 4.1.9: the media type is compared case-insensitively and may omit the "application/" prefix.
        private static bool IsType(string typ, string expected) =>
            string.Equals(typ, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(typ, "application/" + expected, StringComparison.OrdinalIgnoreCase);

        private void CheckClaims(JsonElement payload, bool idToken, string? audience)
        {
            var issuer = AuthJson.GetString(payload, "iss");
            if (!IssuerComparer.Matches(issuer, Issuer))
            {
                throw Fail(TokenVerificationReason.InvalidIssuer, $"The token issuer '{issuer ?? "missing"}' is not '{Issuer}'.");
            }

            if (audience is not null)
            {
                var audiences = AuthJson.GetStringList(payload, "aud");
                if (!Contains(audiences, audience))
                {
                    throw Fail(TokenVerificationReason.InvalidAudience, $"The token audience does not include '{audience}'.");
                }

                // OpenID Connect Core 3.1.3.7: with several audiences, azp names the client the token was issued to.
                var authorizedParty = AuthJson.GetString(payload, "azp");
                if (idToken && authorizedParty is not null && authorizedParty != audience)
                {
                    throw Fail(TokenVerificationReason.InvalidAudience, $"The ID token was issued to '{authorizedParty}', not '{audience}'.");
                }
            }

            var now = _utcNow();
            var expiresAt = ReadTime(payload, "exp", required: true)!.Value;
            if (now - _clockSkew >= expiresAt)
            {
                throw Fail(TokenVerificationReason.Expired, $"The token expired at {expiresAt:O}.");
            }

            if (ReadTime(payload, "nbf", required: false) is { } notBefore && now + _clockSkew < notBefore)
            {
                throw Fail(TokenVerificationReason.NotYetValid, $"The token is not valid before {notBefore:O}.");
            }

            var issuedAt = ReadTime(payload, "iat", required: true)!.Value;
            if (issuedAt > now + _clockSkew)
            {
                throw Fail(TokenVerificationReason.IssuedInFuture, $"The token was issued in the future ({issuedAt:O}).");
            }

            if (AuthJson.GetString(payload, "sub") is null)
            {
                throw Fail(TokenVerificationReason.Malformed, "The token has no sub claim.");
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            foreach (var item in values)
            {
                if (string.Equals(item, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static DateTimeOffset? ReadTime(JsonElement payload, string name, bool required)
        {
            if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (required)
                {
                    throw Fail(TokenVerificationReason.Malformed, $"The token has no {name} claim.");
                }

                return null;
            }

            var seconds = TokenClaims.ToTime(value);
            if (seconds is null)
            {
                throw Fail(TokenVerificationReason.Malformed, $"The token {name} claim is not a NumericDate.");
            }

            return seconds;
        }
    }
}
