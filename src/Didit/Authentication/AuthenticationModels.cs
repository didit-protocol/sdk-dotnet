using System;
using System.Collections.Generic;
using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>The issuer's OpenID Connect discovery document (<c>/.well-known/openid-configuration</c>).</summary>
    public sealed class OpenIdConfiguration
    {
        private OpenIdConfiguration(JsonElement raw)
        {
            Raw = raw;
            Issuer = AuthJson.GetString(raw, "issuer") ?? string.Empty;
            JwksUri = ReadUri(raw, "jwks_uri");
            TokenEndpoint = ReadUri(raw, "token_endpoint");
            AuthorizationEndpoint = ReadUri(raw, "authorization_endpoint");
            ResponseTypesSupported = AuthJson.GetStringList(raw, "response_types_supported");
            GrantTypesSupported = AuthJson.GetStringList(raw, "grant_types_supported");
            CodeChallengeMethodsSupported = AuthJson.GetStringList(raw, "code_challenge_methods_supported");
            IdTokenSigningAlgValuesSupported = AuthJson.GetStringList(raw, "id_token_signing_alg_values_supported");
            ScopesSupported = AuthJson.GetStringList(raw, "scopes_supported");
            ClaimsSupported = AuthJson.GetStringList(raw, "claims_supported");
        }

        /// <summary><c>issuer</c>: the issuer URL, equal to the <c>iss</c> claim of every token.</summary>
        public string Issuer { get; }

        /// <summary><c>jwks_uri</c>: where the issuer publishes its signing keys.</summary>
        public Uri? JwksUri { get; }

        /// <summary><c>token_endpoint</c>: the code exchange and refresh endpoint.</summary>
        public Uri? TokenEndpoint { get; }

        /// <summary><c>authorization_endpoint</c>: the hosted sign-in page to send the browser to.</summary>
        public Uri? AuthorizationEndpoint { get; }

        /// <summary><c>response_types_supported</c>.</summary>
        public IReadOnlyList<string> ResponseTypesSupported { get; }

        /// <summary><c>grant_types_supported</c>.</summary>
        public IReadOnlyList<string> GrantTypesSupported { get; }

        /// <summary><c>code_challenge_methods_supported</c>.</summary>
        public IReadOnlyList<string> CodeChallengeMethodsSupported { get; }

        /// <summary><c>id_token_signing_alg_values_supported</c>.</summary>
        public IReadOnlyList<string> IdTokenSigningAlgValuesSupported { get; }

        /// <summary><c>scopes_supported</c>.</summary>
        public IReadOnlyList<string> ScopesSupported { get; }

        /// <summary><c>claims_supported</c>.</summary>
        public IReadOnlyList<string> ClaimsSupported { get; }

        /// <summary>The whole document, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static OpenIdConfiguration FromJson(JsonElement raw) => new(raw);

        private static Uri? ReadUri(JsonElement raw, string name)
        {
            var text = AuthJson.GetString(raw, name);
            if (text is null || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var https = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            var loopback = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
            return https || loopback ? uri : null;
        }
    }

    /// <summary>The token endpoint's answer to <see cref="Authentication.ExchangeCodeAsync"/> and <see cref="Authentication.RefreshAsync"/>.</summary>
    public sealed class AuthTokenResponse
    {
        private AuthTokenResponse(JsonElement raw)
        {
            Raw = raw;
            AccessToken = AuthJson.GetString(raw, "access_token") ?? string.Empty;
            IdToken = AuthJson.GetString(raw, "id_token");
            RefreshToken = AuthJson.GetString(raw, "refresh_token");
            TokenType = AuthJson.GetString(raw, "token_type");
            ExpiresIn = AuthJson.GetInt64(raw, "expires_in");
            SessionId = AuthJson.GetText(raw, "session_id");
        }

        /// <summary><c>access_token</c>: a JWT (<c>typ</c> at+jwt) valid for up to 5 minutes. Verify it with <see cref="Authentication.VerifyAccessTokenAsync"/>.</summary>
        public string AccessToken { get; }

        /// <summary><c>id_token</c>: a JWT describing the person. Verify it with <see cref="Authentication.VerifyIdTokenAsync"/> before trusting it.</summary>
        public string? IdToken { get; }

        /// <summary>
        /// <c>refresh_token</c>: opaque and single-use; every refresh returns a new one. Store it server-side and serialise its use:
        /// presenting a consumed refresh token revokes the session.
        /// </summary>
        public string? RefreshToken { get; }

        /// <summary><c>token_type</c>: <c>Bearer</c>.</summary>
        public string? TokenType { get; }

        /// <summary><c>expires_in</c>: seconds until the access token expires.</summary>
        public long? ExpiresIn { get; }

        /// <summary><c>session_id</c>: the session the tokens belong to (the tokens' <c>sid</c>).</summary>
        public string? SessionId { get; }

        /// <summary>The whole response, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static AuthTokenResponse FromJson(JsonElement raw) => new(raw);
    }

    /// <summary>A live session, as <see cref="Authentication.GetSessionAsync"/> returns it (<c>GET session/</c>).</summary>
    public sealed class AuthSession
    {
        private AuthSession(JsonElement raw)
        {
            Raw = raw;
            Subject = AuthJson.GetText(raw, "sub") ?? string.Empty;
            SessionId = AuthJson.GetText(raw, "sid") ?? string.Empty;
            ApplicationId = AuthJson.GetText(raw, "application_id");
            VendorData = AuthJson.GetText(raw, "vendor_data");
            Amr = AuthJson.GetStringList(raw, "amr");
            MfaVerified = AuthJson.GetBoolean(raw, "mfa_verified");
            Email = AuthJson.GetString(raw, "email");
            EmailVerified = AuthJson.GetBoolean(raw, "email_verified");
            Phone = AuthJson.GetString(raw, "phone");
            Verified = AuthVerifiedClaim.FromJson(AuthJson.GetElement(raw, "verified"));
            ExpiresAt = AuthJson.GetDateTime(raw, "expires_at");
            PostLogoutRedirectUri = AuthJson.GetString(raw, "post_logout_redirect_uri");
            var organizations = new List<AuthSessionOrganization>();
            if (AuthJson.TryGet(raw, "organizations", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        organizations.Add(new AuthSessionOrganization(item.Clone()));
                    }
                }
            }

            Organizations = organizations;
        }

        /// <summary><c>sub</c>: the identity id.</summary>
        public string Subject { get; }

        /// <summary><c>sid</c>: the session id.</summary>
        public string SessionId { get; }

        /// <summary><c>application_id</c>: the application (and OAuth client) id.</summary>
        public string? ApplicationId { get; }

        /// <summary><c>vendor_data</c>: the person's reference across Didit products.</summary>
        public string? VendorData { get; }

        /// <summary><c>amr</c>: how the person signed in, for example <c>passkey</c>.</summary>
        public IReadOnlyList<string> Amr { get; }

        /// <summary><c>mfa_verified</c>: whether the session passed a second factor (a passkey counts).</summary>
        public bool? MfaVerified { get; }

        /// <summary><c>email</c>.</summary>
        public string? Email { get; }

        /// <summary><c>email_verified</c>.</summary>
        public bool? EmailVerified { get; }

        /// <summary><c>phone</c>.</summary>
        public string? Phone { get; }

        /// <summary><c>verified</c>: the person's verified identity, read now (tokens carry the value from when they were issued).</summary>
        public AuthVerifiedClaim Verified { get; }

        /// <summary><c>expires_at</c>: when the session ends at the latest (idle timeout can end it earlier).</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary><c>post_logout_redirect_uri</c>: where to send the browser after signing out.</summary>
        public string? PostLogoutRedirectUri { get; }

        /// <summary><c>organizations</c>: every organization the person belongs to; empty when customer organizations are off.</summary>
        public IReadOnlyList<AuthSessionOrganization> Organizations { get; }

        /// <summary>The whole response, including fields this SDK version does not know (for example <c>verified_identity</c>).</summary>
        public JsonElement Raw { get; }

        internal static AuthSession FromJson(JsonElement raw) => new(raw);
    }

    /// <summary>One organization membership in <see cref="AuthSession.Organizations"/>.</summary>
    public sealed class AuthSessionOrganization
    {
        internal AuthSessionOrganization(JsonElement raw)
        {
            Raw = raw;
            OrganizationId = AuthJson.GetText(raw, "organization_id") ?? string.Empty;
            OrganizationName = AuthJson.GetString(raw, "organization__name");
            Role = AuthJson.GetString(raw, "role");
            if (AuthJson.TryGet(raw, "custom_role", out var custom) && custom.ValueKind == JsonValueKind.Object)
            {
                CustomRoleKey = AuthJson.GetString(custom, "key");
                CustomRolePermissions = AuthJson.GetStringList(custom, "permissions");
            }
            else
            {
                CustomRolePermissions = Array.Empty<string>();
            }
        }

        /// <summary><c>organization_id</c>.</summary>
        public string OrganizationId { get; }

        /// <summary>The organization's name (sent as <c>organization__name</c>).</summary>
        public string? OrganizationName { get; }

        /// <summary><c>role</c>: the built-in role.</summary>
        public string? Role { get; }

        /// <summary><c>custom_role.key</c>, when the member has a custom role.</summary>
        public string? CustomRoleKey { get; }

        /// <summary><c>custom_role.permissions</c>; empty without a custom role.</summary>
        public IReadOnlyList<string> CustomRolePermissions { get; }

        /// <summary>The membership as sent.</summary>
        public JsonElement Raw { get; }
    }

    /// <summary>
    /// The <c>verified</c> object of tokens and sessions: the highest approved assurance level and the verified claims the
    /// application selected. Empty (every property <c>null</c>) when the person has none.
    /// </summary>
    public sealed class AuthVerifiedClaim
    {
        private AuthVerifiedClaim(JsonElement raw)
        {
            Raw = raw;
            Level = AuthJson.GetString(raw, "level");
            Over18 = AuthJson.GetBoolean(raw, "over_18");
            Over21 = AuthJson.GetBoolean(raw, "over_21");
            GivenName = AuthJson.GetString(raw, "given_name");
            FamilyName = AuthJson.GetString(raw, "family_name");
            Birthdate = AuthJson.GetString(raw, "birthdate");
            Nationality = AuthJson.GetString(raw, "nationality");
            Country = AuthJson.GetString(raw, "country");
            DocumentVerified = AuthJson.GetBoolean(raw, "document_verified");
            Liveness = AuthJson.GetBoolean(raw, "liveness");
        }

        /// <summary><c>level</c>: <c>liveness</c>, <c>liveness_face_match</c> or <c>full_id</c> (weakest first), or <c>null</c>.</summary>
        public string? Level { get; }

        /// <summary><c>over_18</c>.</summary>
        public bool? Over18 { get; }

        /// <summary><c>over_21</c>.</summary>
        public bool? Over21 { get; }

        /// <summary><c>given_name</c> from the verified document.</summary>
        public string? GivenName { get; }

        /// <summary><c>family_name</c> from the verified document.</summary>
        public string? FamilyName { get; }

        /// <summary><c>birthdate</c> as an ISO 8601 date (<c>YYYY-MM-DD</c>).</summary>
        public string? Birthdate { get; }

        /// <summary><c>nationality</c>: ISO 3166-1 alpha-2.</summary>
        public string? Nationality { get; }

        /// <summary><c>country</c>: ISO 3166-1 alpha-2.</summary>
        public string? Country { get; }

        /// <summary><c>document_verified</c>.</summary>
        public bool? DocumentVerified { get; }

        /// <summary><c>liveness</c>.</summary>
        public bool? Liveness { get; }

        /// <summary>The object as sent, including claims this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static AuthVerifiedClaim FromJson(JsonElement? raw) =>
            new(raw is { ValueKind: JsonValueKind.Object } value ? value : AuthJson.EmptyObject);
    }

    /// <summary>
    /// A step-up verification, from <see cref="Authentication.CreateStepUpAsync"/> or <see cref="Authentication.RetrieveStepUpAsync"/>.
    /// Act on the sensitive action only when <see cref="Status"/> is <see cref="AuthStepUpStatus.Approved"/>, read from
    /// <see cref="Authentication.RetrieveStepUpAsync"/> on your backend; never trust the <c>status</c> query parameter of the return URL.
    /// </summary>
    public sealed class AuthStepUp
    {
        private AuthStepUp(JsonElement raw)
        {
            Raw = raw;
            Required = AuthJson.GetBoolean(raw, "required") ?? AuthJson.GetText(raw, "id") is not null;
            Id = AuthJson.GetText(raw, "id");
            Rule = AuthJson.GetString(raw, "rule");
            Method = AuthJson.GetString(raw, "method");
            Status = AuthJson.GetString(raw, "status");
            Level = AuthJson.GetString(raw, "level");
            Amount = AuthJson.GetText(raw, "amount");
            Currency = AuthJson.GetString(raw, "currency");
            VerificationUrl = AuthJson.GetString(raw, "verification_url");
            ExpiresIn = AuthJson.GetInt64(raw, "expires_in");
            CreatedAt = AuthJson.GetDateTime(raw, "created_at");
            CompletedAt = AuthJson.GetDateTime(raw, "completed_at");
            ExpiresAt = AuthJson.GetDateTime(raw, "expires_at");
        }

        /// <summary>
        /// <c>required</c>: <c>false</c> when no rule applies and the action may proceed (every other property is then <c>null</c>);
        /// <c>true</c> when the person must verify at <see cref="VerificationUrl"/> first.
        /// </summary>
        public bool Required { get; }

        /// <summary><c>id</c>: the step-up id; pass it to <see cref="Authentication.RetrieveStepUpAsync"/>.</summary>
        public string? Id { get; }

        /// <summary><c>rule</c> that applied: <c>payout</c>, <c>device</c>, <c>bank</c> or <c>contact</c>.</summary>
        public string? Rule { get; }

        /// <summary><c>method</c> the person verifies with: <c>liveness</c>, <c>selfie</c> or <c>full_id</c>.</summary>
        public string? Method { get; }

        /// <summary><c>status</c>, one of <see cref="AuthStepUpStatus"/> (new values may appear).</summary>
        public string? Status { get; }

        /// <summary><c>level</c> the verification reached, once approved.</summary>
        public string? Level { get; }

        /// <summary><c>amount</c> of a payout, as a decimal string.</summary>
        public string? Amount { get; }

        /// <summary><c>currency</c> of a payout (ISO 4217).</summary>
        public string? Currency { get; }

        /// <summary><c>verification_url</c>: send the person here (only in the creation response).</summary>
        public string? VerificationUrl { get; }

        /// <summary><c>expires_in</c>: seconds the person has to verify (only in the creation response).</summary>
        public long? ExpiresIn { get; }

        /// <summary><c>created_at</c>.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary><c>completed_at</c>.</summary>
        public DateTimeOffset? CompletedAt { get; }

        /// <summary><c>expires_at</c>.</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary><c>true</c> when <see cref="Status"/> is <see cref="AuthStepUpStatus.Approved"/>: the only status to act on.</summary>
        public bool IsApproved => Status == AuthStepUpStatus.Approved;

        /// <summary>The whole response, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static AuthStepUp FromJson(JsonElement raw) => new(raw);
    }

    /// <summary>The known values of <see cref="AuthStepUp.Status"/>. The set is open: treat any other value as not approved.</summary>
    public static class AuthStepUpStatus
    {
        /// <summary>Waiting for the person to verify.</summary>
        public const string Pending = "pending";

        /// <summary>The verification is in manual review.</summary>
        public const string Review = "review";

        /// <summary>Verified: the only status on which the action may proceed.</summary>
        public const string Approved = "approved";

        /// <summary>The verification was declined.</summary>
        public const string Declined = "declined";

        /// <summary>The person did not verify in time.</summary>
        public const string Expired = "expired";

        /// <summary>A newer check replaced this one.</summary>
        public const string Superseded = "superseded";
    }
}
