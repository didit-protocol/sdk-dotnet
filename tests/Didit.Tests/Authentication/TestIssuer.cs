using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Didit.Tests.Authentication
{
    /// <summary>
    /// A stubbed Didit issuer with a locally generated ES256 key: serves discovery and JWKS from memory, signs tokens, and lets a
    /// test answer the runtime endpoints. Nothing touches the network.
    /// </summary>
    public sealed class TestIssuer : IDisposable
    {
        public const string Url = "https://acme.auth.didit.me";
        public const string ClientId = "6c7e3a52-9b1f-4c1a-8f5e-0d6c2b9a1e47";
        public const string Kid = "key-1";

        private readonly HttpClient _httpClient;
        private readonly List<(string Kid, ECDsa Key)> _published = new();

        public TestIssuer()
        {
            Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _published.Add((Kid, Key));
            Handler = new StubHttpHandler(Respond);
            _httpClient = new HttpClient(Handler);
            Now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        }

        public ECDsa Key { get; }

        public StubHttpHandler Handler { get; }

        public DateTimeOffset Now { get; set; }

        public string? DiscoveryCacheControl { get; set; } = "public, max-age=300";

        public string? JwksCacheControl { get; set; } = "public, max-age=300";

        /// <summary>Answers any request that is not discovery or JWKS.</summary>
        public Func<RecordedRequest, HttpResponseMessage> Runtime { get; set; } =
            _ => StubHttpHandler.Json("{\"detail\":\"not found\"}", HttpStatusCode.NotFound);

        public string? TokenEndpoint { get; set; } = Url + "/token/";

        /// <summary>The discovery document, shaped like service-didit-auth's <c>issuer.discovery()</c>.</summary>
        public string DiscoveryJson
        {
            get
            {
                var document = new JsonObject
                {
                    ["issuer"] = Url,
                    ["authorization_endpoint"] = Url + "/sign-in/",
                    ["jwks_uri"] = Url + "/.well-known/jwks.json",
                    ["response_types_supported"] = new JsonArray("code"),
                    ["response_modes_supported"] = new JsonArray("query"),
                    ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
                    ["subject_types_supported"] = new JsonArray("public"),
                    ["id_token_signing_alg_values_supported"] = new JsonArray("ES256"),
                    ["token_endpoint_auth_methods_supported"] = new JsonArray("none"),
                    ["code_challenge_methods_supported"] = new JsonArray("S256"),
                    ["scopes_supported"] = new JsonArray("openid", "email", "phone", "profile", "offline_access"),
                    ["claims_supported"] = new JsonArray("iss", "aud", "sub", "sid", "vendor_data", "amr", "org", "verified", "act"),
                    ["x_future_field"] = "kept",
                };
                if (TokenEndpoint is not null)
                {
                    document["token_endpoint"] = TokenEndpoint;
                }

                return document.ToJsonString();
            }
        }

        public Didit.Authentication Create(TimeSpan? clockSkew = null)
        {
            var options = new AuthenticationOptions { HttpClient = _httpClient, UtcNow = () => Now };
            if (clockSkew is { } skew)
            {
                options.ClockSkew = skew;
            }

            return new Didit.Authentication(Url, options);
        }

        /// <summary>Publishes an extra key in the JWKS (key rotation).</summary>
        public void Publish(string kid, ECDsa key) => _published.Add((kid, key));

        /// <summary>Access token claims shaped like service-didit-auth's <c>issuer.session_tokens()</c>.</summary>
        public JsonObject AccessClaims() => new()
        {
            ["iss"] = Url,
            ["aud"] = ClientId,
            ["sub"] = "0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90",
            ["sid"] = "a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57",
            ["jti"] = "Jx8bU2nqVt0rWl5m",
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.ToUnixTimeSeconds() + 300,
            ["vendor_data"] = "customer-42",
            ["org"] = new JsonObject { ["id"] = "4d1e8f2a-6b7c-4c3d-9e0f-1a2b3c4d5e6f", ["role"] = "admin" },
            ["verified"] = new JsonObject { ["level"] = "full_id", ["over_18"] = true, ["given_name"] = "Ada", ["country"] = "GB" },
            ["amr"] = new JsonArray("passkey"),
            ["act"] = null,
            ["client_id"] = ClientId,
            ["x_future_claim"] = "kept",
        };

        /// <summary>ID token claims: the access token's, without <c>client_id</c>, plus the profile claims.</summary>
        public JsonObject IdClaims(string nonce) => new()
        {
            ["iss"] = Url,
            ["aud"] = ClientId,
            ["sub"] = "0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90",
            ["sid"] = "a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57",
            ["jti"] = "Qw3eR5tY7uI9oP1a",
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.ToUnixTimeSeconds() + 300,
            ["vendor_data"] = "customer-42",
            ["org"] = null,
            ["verified"] = new JsonObject(),
            ["amr"] = new JsonArray("password"),
            ["act"] = null,
            ["auth_time"] = Now.ToUnixTimeSeconds() - 30,
            ["email_verified"] = false,
            ["email"] = "ada@example.com",
            ["name"] = "Ada Lovelace",
            ["phone_number"] = "+34600000000",
            ["nonce"] = nonce,
        };

        public static JsonObject Header(string alg = "ES256", string? typ = "at+jwt", string? kid = Kid)
        {
            var header = new JsonObject { ["alg"] = alg };
            if (typ is not null)
            {
                header["typ"] = typ;
            }

            if (kid is not null)
            {
                header["kid"] = kid;
            }

            return header;
        }

        public string Sign(JsonObject claims, JsonObject? header = null, ECDsa? key = null) =>
            SignWith(header ?? Header(), claims, data => (key ?? Key).SignData(data, HashAlgorithmName.SHA256));

        public static string SignWith(JsonObject header, JsonObject claims, Func<byte[], byte[]> sign)
        {
            var signingInput = B64(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + B64(Encoding.UTF8.GetBytes(claims.ToJsonString()));
            return signingInput + "." + B64(sign(Encoding.ASCII.GetBytes(signingInput)));
        }

        public static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string Jwk(string kid, ECDsa key)
        {
            var parameters = key.ExportParameters(false);
            return new JsonObject
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["use"] = "sig",
                ["alg"] = "ES256",
                ["kid"] = kid,
                ["x"] = B64(parameters.Q.X!),
                ["y"] = B64(parameters.Q.Y!),
            }.ToJsonString();
        }

        public string JwksJson()
        {
            var keys = new List<string>();
            foreach (var (kid, key) in _published)
            {
                keys.Add(Jwk(kid, key));
            }

            return "{\"keys\":[" + string.Join(",", keys) + "]}";
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            foreach (var (_, key) in _published)
            {
                key.Dispose();
            }
        }

        private HttpResponseMessage Respond(RecordedRequest request)
        {
            switch (request.Uri.AbsolutePath)
            {
                case "/.well-known/openid-configuration":
                    return StubHttpHandler.Json(DiscoveryJson, cacheControl: DiscoveryCacheControl);
                case "/.well-known/jwks.json":
                    return StubHttpHandler.Json(JwksJson(), cacheControl: JwksCacheControl);
                default:
                    return Runtime(request);
            }
        }
    }

    internal static class JsonNodeExtensions
    {
        public static JsonObject With(this JsonObject node, string name, JsonNode? value)
        {
            node[name] = value;
            return node;
        }

        public static JsonObject Without(this JsonObject node, string name)
        {
            node.Remove(name);
            return node;
        }

        public static JsonElement ToElement(this JsonObject node) => JsonDocument.Parse(node.ToJsonString()).RootElement;
    }
}
