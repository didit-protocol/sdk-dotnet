using System;
using System.Collections.Generic;
using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>The claims Didit access and ID tokens share. Every claim, known or not, is also in <see cref="Raw"/>.</summary>
    public abstract class TokenClaims
    {
        private protected TokenClaims(JsonElement payload)
        {
            Raw = payload;
            Issuer = AuthJson.GetString(payload, "iss") ?? string.Empty;
            Audience = AuthJson.GetStringList(payload, "aud");
            Subject = AuthJson.GetString(payload, "sub") ?? string.Empty;
            SessionId = AuthJson.GetText(payload, "sid");
            JwtId = AuthJson.GetText(payload, "jti");
            IssuedAt = ReadTime(payload, "iat") ?? default;
            ExpiresAt = ReadTime(payload, "exp") ?? default;
            VendorData = AuthJson.GetText(payload, "vendor_data");
            Amr = AuthJson.GetStringList(payload, "amr");
            Organization = AuthJson.TryGet(payload, "org", out var org) && org.ValueKind == JsonValueKind.Object ? new AuthOrganizationClaim(org.Clone()) : null;
            Verified = new AuthVerifiedClaim(AuthJson.TryGet(payload, "verified", out var verified) && verified.ValueKind == JsonValueKind.Object ? verified.Clone() : EmptyObject());
            Actor = AuthJson.TryGet(payload, "act", out var act) && act.ValueKind == JsonValueKind.Object ? new AuthActorClaim(act.Clone()) : null;
        }

        /// <summary><c>iss</c>: the application's issuer.</summary>
        public string Issuer { get; }

        /// <summary><c>aud</c>: the audiences (the application id). A single string claim reads as a one-element list.</summary>
        public IReadOnlyList<string> Audience { get; }

        /// <summary><c>sub</c>: the identity id, stable for this person within the application.</summary>
        public string Subject { get; }

        /// <summary><c>sid</c>: the session id (the grant id for an agent token).</summary>
        public string? SessionId { get; }

        /// <summary><c>jti</c>: the token id.</summary>
        public string? JwtId { get; }

        /// <summary><c>iat</c>: when the token was issued.</summary>
        public DateTimeOffset IssuedAt { get; }

        /// <summary><c>exp</c>: when the token expires.</summary>
        public DateTimeOffset ExpiresAt { get; }

        /// <summary>
        /// <c>vendor_data</c>: the person's reference across Didit products: your <c>external_id</c> when the identity was
        /// imported with one, otherwise the identity id. Use it to join the person to their verification sessions and transactions.
        /// </summary>
        public string? VendorData { get; }

        /// <summary><c>amr</c>: the sign-in methods, for example <c>passkey</c>; empty for an agent token.</summary>
        public IReadOnlyList<string> Amr { get; }

        /// <summary><c>org</c>: the person's earliest organization membership when customer organizations are on, otherwise <c>null</c>.</summary>
        public AuthOrganizationClaim? Organization { get; }

        /// <summary><c>verified</c>: the person's verified identity when the token was issued. Empty (no level) when there is none, and always for an agent token.</summary>
        public AuthVerifiedClaim Verified { get; }

        /// <summary><c>act</c>: the agent acting for the person, or <c>null</c> when the person is acting.</summary>
        public AuthActorClaim? Actor { get; }

        /// <summary>Every claim of the token, including those this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static DateTimeOffset? ToTime(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            const long MaxSeconds = 253_402_300_799; // 9999-12-31T23:59:59Z
            const long MinSeconds = -62_135_596_800; // 0001-01-01T00:00:00Z
            if (value.TryGetInt64(out var seconds))
            {
                return seconds >= MinSeconds && seconds <= MaxSeconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
            }

            if (value.TryGetDouble(out var real) && !double.IsNaN(real) && real >= MinSeconds && real <= MaxSeconds)
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(real));
            }

            return null;
        }

        private protected static DateTimeOffset? ReadTime(JsonElement payload, string name) =>
            AuthJson.TryGet(payload, name, out var value) ? ToTime(value) : null;

        private static JsonElement EmptyObject()
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }

    /// <summary>The verified claims of a Didit access token. Returned by <see cref="Authentication.VerifyAccessTokenAsync"/>.</summary>
    public sealed class AccessTokenClaims : TokenClaims
    {
        private AccessTokenClaims(JsonElement payload)
            : base(payload)
        {
            ClientId = AuthJson.GetText(payload, "client_id");
        }

        /// <summary><c>client_id</c>: the application id the token was issued to.</summary>
        public string? ClientId { get; }

        internal static AccessTokenClaims FromPayload(JsonElement payload) => new(payload);
    }

    /// <summary>The verified claims of a Didit ID token. Returned by <see cref="Authentication.VerifyIdTokenAsync"/>.</summary>
    public sealed class IdTokenClaims : TokenClaims
    {
        private IdTokenClaims(JsonElement payload)
            : base(payload)
        {
            AuthTime = ReadTime(payload, "auth_time");
            Nonce = AuthJson.GetString(payload, "nonce");
            Email = AuthJson.GetString(payload, "email");
            EmailVerified = AuthJson.GetBoolean(payload, "email_verified");
            Name = AuthJson.GetString(payload, "name");
            PhoneNumber = AuthJson.GetString(payload, "phone_number");
        }

        /// <summary><c>auth_time</c>: when the person authenticated.</summary>
        public DateTimeOffset? AuthTime { get; }

        /// <summary><c>nonce</c>: the nonce of the authorization request (already checked).</summary>
        public string? Nonce { get; }

        /// <summary><c>email</c>, when the identity has one.</summary>
        public string? Email { get; }

        /// <summary><c>email_verified</c>; <c>false</c> is meaningful, <c>null</c> means the claim is absent.</summary>
        public bool? EmailVerified { get; }

        /// <summary><c>name</c>, when set.</summary>
        public string? Name { get; }

        /// <summary><c>phone_number</c>, when set.</summary>
        public string? PhoneNumber { get; }

        internal static IdTokenClaims FromPayload(JsonElement payload) => new(payload);
    }

    /// <summary>The <c>org</c> claim: the person's organization membership.</summary>
    public sealed class AuthOrganizationClaim
    {
        internal AuthOrganizationClaim(JsonElement raw)
        {
            Raw = raw;
            Id = AuthJson.GetText(raw, "id") ?? string.Empty;
            Role = AuthJson.GetString(raw, "role");
        }

        /// <summary>The organization id.</summary>
        public string Id { get; }

        /// <summary>The person's built-in role in the organization.</summary>
        public string? Role { get; }

        /// <summary>The claim as sent, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }
    }

    /// <summary>The <c>act</c> claim of an agent token: the agent acting for the person (RFC 8693).</summary>
    public sealed class AuthActorClaim
    {
        internal AuthActorClaim(JsonElement raw)
        {
            Raw = raw;
            Subject = AuthJson.GetText(raw, "sub");
            Agent = AuthJson.GetString(raw, "agent");
            Scopes = AuthJson.GetSpaceSeparated(raw, "scopes");
        }

        /// <summary><c>sub</c>: the agent id.</summary>
        public string? Subject { get; }

        /// <summary><c>agent</c>: the agent's name.</summary>
        public string? Agent { get; }

        /// <summary><c>scopes</c>: what the person allowed the agent to do (<c>read</c>, <c>act</c>). Your application enforces them.</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>The claim as sent, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }
    }
}
