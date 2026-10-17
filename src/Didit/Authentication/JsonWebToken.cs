using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>A compact JWS split into its parts. Nothing about it is trusted until the signature is verified.</summary>
    internal sealed class JsonWebToken
    {
        /// <summary>Tokens longer than this are rejected before any parsing.</summary>
        public const int MaxLength = 64 * 1024;

        private JsonWebToken(byte[] signingInput, JsonElement header, JsonElement payload, byte[] signature)
        {
            SigningInput = signingInput;
            Header = header;
            Payload = payload;
            Signature = signature;
        }

        /// <summary>ASCII bytes of <c>base64url(header) "." base64url(payload)</c>, the data the signature covers.</summary>
        public byte[] SigningInput { get; }

        public JsonElement Header { get; }

        public JsonElement Payload { get; }

        public byte[] Signature { get; }

        public string? Algorithm => AuthJson.GetString(Header, "alg");

        public string? KeyId => AuthJson.GetString(Header, "kid");

        public static JsonWebToken Parse(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw Malformed("The token is empty.");
            }

            if (token!.Length > MaxLength)
            {
                throw Malformed("The token is too long.");
            }

            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                throw Malformed(parts.Length == 5 ? "Encrypted tokens (JWE) are not supported." : "The token is not a compact JWS with three parts.");
            }

            if (!Base64Url.TryDecode(parts[0], out var headerBytes) || !Base64Url.TryDecode(parts[1], out var payloadBytes) || !Base64Url.TryDecode(parts[2], out var signature))
            {
                throw Malformed("The token parts are not base64url.");
            }

            var header = ParseObject(headerBytes, "header");
            var payload = ParseObject(payloadBytes, "claims");
            var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            return new JsonWebToken(signingInput, header, payload, signature);
        }

        private static JsonElement ParseObject(byte[] utf8, string what)
        {
            try
            {
                using var document = JsonDocument.Parse(utf8);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw Malformed($"The token {what} is not a JSON object.");
                }

                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw Malformed($"The token {what} is not valid JSON.");
            }
        }

        private static TokenVerificationException Malformed(string message) =>
            new(TokenVerificationReason.Malformed, message);
    }

    /// <summary>An ES256 (P-256) verification key from the issuer's JWKS.</summary>
    internal sealed class SigningKey
    {
        private const int CoordinateLength = 32;

        private readonly byte[] _x;
        private readonly byte[] _y;

        private SigningKey(string? keyId, byte[] x, byte[] y)
        {
            KeyId = keyId;
            _x = x;
            _y = y;
        }

        public string? KeyId { get; }

        /// <summary>Verifies an IEEE P1363 (r || s) ES256 signature over <paramref name="data"/>.</summary>
        public bool Verify(byte[] data, byte[] signature)
        {
            if (signature.Length != 2 * CoordinateLength)
            {
                return false;
            }

            using var ecdsa = CreateEcdsa(_x, _y);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        /// <summary>
        /// Reads the usable keys of a JWKS: <c>kty</c> EC on <c>crv</c> P-256, <c>use</c> absent or <c>sig</c>, <c>alg</c> absent or
        /// ES256, and a point on the curve. Other keys are skipped, never an error.
        /// </summary>
        public static IReadOnlyList<SigningKey> ParseSet(JsonElement jwks)
        {
            var keys = new List<SigningKey>();
            if (jwks.ValueKind != JsonValueKind.Object || !jwks.TryGetProperty("keys", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return keys;
            }

            foreach (var jwk in array.EnumerateArray())
            {
                var key = TryParse(jwk);
                if (key is not null)
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        private static SigningKey? TryParse(JsonElement jwk)
        {
            if (jwk.ValueKind != JsonValueKind.Object
                || AuthJson.GetString(jwk, "kty") != "EC"
                || AuthJson.GetString(jwk, "crv") != "P-256"
                || AuthJson.GetString(jwk, "d") is not null)
            {
                return null;
            }

            var use = AuthJson.GetString(jwk, "use");
            var alg = AuthJson.GetString(jwk, "alg");
            if ((use is not null && use != "sig") || (alg is not null && alg != "ES256"))
            {
                return null;
            }

            if (!Base64Url.TryDecode(AuthJson.GetString(jwk, "x"), out var x) || x.Length != CoordinateLength
                || !Base64Url.TryDecode(AuthJson.GetString(jwk, "y"), out var y) || y.Length != CoordinateLength)
            {
                return null;
            }

            try
            {
                // Importing validates that the point is on the curve.
                using var check = CreateEcdsa(x, y);
            }
            catch (CryptographicException)
            {
                return null;
            }

            return new SigningKey(AuthJson.GetString(jwk, "kid"), x, y);
        }

        private static ECDsa CreateEcdsa(byte[] x, byte[] y) =>
            ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });
    }
}
