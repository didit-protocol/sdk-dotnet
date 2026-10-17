namespace Didit
{
    /// <summary>
    /// A token failed verification in <see cref="Authentication.VerifyAccessTokenAsync"/> or
    /// <see cref="Authentication.VerifyIdTokenAsync"/>. <see cref="Reason"/> says why, as one of
    /// <see cref="TokenVerificationReason"/>. Treat the request as unauthenticated (HTTP 401).
    /// </summary>
    public class TokenVerificationException : DiditException
    {
        /// <summary>Creates the exception.</summary>
        public TokenVerificationException(string reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        /// <summary>Machine-readable reason, one of the <see cref="TokenVerificationReason"/> constants.</summary>
        public string Reason { get; }
    }

    /// <summary>The machine-readable reasons of <see cref="TokenVerificationException"/>.</summary>
    public static class TokenVerificationReason
    {
        /// <summary>The token is not a well-formed compact JWS with JSON header and claims, or a required claim is missing or has the wrong type.</summary>
        public const string Malformed = "malformed";

        /// <summary>The header <c>alg</c> is not <c>ES256</c>.</summary>
        public const string UnsupportedAlgorithm = "unsupported_algorithm";

        /// <summary>The header <c>typ</c> is wrong for the token kind (an ID token presented as an access token, or the reverse).</summary>
        public const string InvalidType = "invalid_type";

        /// <summary>The <c>iss</c> claim is not this issuer.</summary>
        public const string InvalidIssuer = "invalid_issuer";

        /// <summary>The <c>aud</c> claim does not contain the expected audience.</summary>
        public const string InvalidAudience = "invalid_audience";

        /// <summary>The <c>exp</c> claim is in the past, beyond the clock skew.</summary>
        public const string Expired = "expired";

        /// <summary>The <c>nbf</c> claim is in the future, beyond the clock skew.</summary>
        public const string NotYetValid = "not_yet_valid";

        /// <summary>The <c>iat</c> claim is in the future, beyond the clock skew.</summary>
        public const string IssuedInFuture = "issued_in_future";

        /// <summary>No key in the issuer's JWKS matches the header <c>kid</c>, even after a refetch.</summary>
        public const string UnknownKey = "unknown_key";

        /// <summary>The ES256 signature does not verify with the issuer's key.</summary>
        public const string InvalidSignature = "invalid_signature";

        /// <summary>The ID token <c>nonce</c> claim is missing or differs from the nonce sent in the authorization request.</summary>
        public const string InvalidNonce = "invalid_nonce";
    }
}
