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

        public string DiscoveryJson => new JsonObject
        {
            ["issuer"] = Url,
            ["authorization_endpoint"] = Url + "/authorize/",
            ["token_endpoint"] = Url + "/token/",
            ["jwks_uri"] = Url + "/.well-known/jwks.json",
            ["userinfo_endpoint"] = Url + "/userinfo/",
            ["end_session_endpoint"] = Url + "/logout/",
            ["response_types_supported"] = new JsonArray("code"),
            ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
            ["code_challenge_methods_supported"] = new JsonArray("S256"),
            ["id_token_signing_alg_values_supported"] = new JsonArray("ES256"),
            ["scopes_supported"] = new JsonArray("openid", "email", "profile", "offline_access"),
            ["x_future_field"] = "kept",
        }.ToJsonString();

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

        public JsonObject AccessClaims() => new()
        {
            ["iss"] = Url,
            ["sub"] = "user_8f2b6c",
            ["aud"] = ClientId,
            ["client_id"] = ClientId,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["nbf"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.ToUnixTimeSeconds() + 300,
            ["jti"] = "at_01J9Z6",
            ["sid"] = "sess_7d1e",
            ["scope"] = "openid email offline_access",
            ["vendor_data"] = "customer-42",
            ["amr"] = new JsonArray("pwd", "otp"),
            ["org"] = new JsonObject { ["id"] = "org_3a9c", ["role"] = "admin" },
            ["verified"] = true,
        };

        public JsonObject IdClaims(string nonce) => new()
        {
            ["iss"] = Url,
            ["sub"] = "user_8f2b6c",
            ["aud"] = ClientId,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.ToUnixTimeSeconds() + 300,
            ["auth_time"] = Now.ToUnixTimeSeconds() - 30,
            ["nonce"] = nonce,
            ["sid"] = "sess_7d1e",
            ["email"] = "ada@example.com",
            ["email_verified"] = true,
            ["name"] = "Ada Lovelace",
            ["phone_number"] = "+34600000000",
            ["amr"] = new JsonArray("pwd"),
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
