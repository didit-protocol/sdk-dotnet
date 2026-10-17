using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Didit.Internal;
using Xunit;

namespace Didit.Tests.Authentication
{
    public sealed class IssuerMetadataTests : IDisposable
    {
        private const string DiscoveryPath = "/.well-known/openid-configuration";
        private const string JwksPath = "/.well-known/jwks.json";

        private readonly TestIssuer _issuer = new();

        public void Dispose() => _issuer.Dispose();

        [Fact]
        public async Task DiscoveryIsTypedAndKeepsUnknownFields()
        {
            using var auth = _issuer.Create();

            var discovery = await auth.GetDiscoveryDocumentAsync();

            Assert.Equal(TestIssuer.Url, discovery.Issuer);
            Assert.Equal(new Uri(TestIssuer.Url + JwksPath), discovery.JwksUri);
            Assert.Equal(new Uri(TestIssuer.Url + "/token/"), discovery.TokenEndpoint);
            Assert.Equal(new Uri(TestIssuer.Url + "/sign-in/"), discovery.AuthorizationEndpoint);
            Assert.Equal(new[] { "S256" }, discovery.CodeChallengeMethodsSupported);
            Assert.Equal(new[] { "ES256" }, discovery.IdTokenSigningAlgValuesSupported);
            Assert.Contains("offline_access", discovery.ScopesSupported);
            Assert.Contains("refresh_token", discovery.GrantTypesSupported);
            Assert.Contains("verified", discovery.ClaimsSupported);
            Assert.Equal("kept", discovery.Raw.GetProperty("x_future_field").GetString());
            Assert.Equal(new Uri(TestIssuer.Url + DiscoveryPath), _issuer.Handler.Requests.Single().Uri);
        }

        [Fact]
        public async Task NothingIsFetchedUntilFirstUse()
        {
            using var auth = _issuer.Create();

            Assert.Empty(_issuer.Handler.Requests);
            await auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims()));

            Assert.Equal(1, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(1, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public async Task DiscoveryAndJwksAreCachedForMaxAge()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddHours(1).ToUnixTimeSeconds()));

            await auth.VerifyAccessTokenAsync(token);
            _issuer.Now = _issuer.Now.AddSeconds(299);
            await auth.VerifyAccessTokenAsync(token);
            await auth.GetDiscoveryDocumentAsync();

            Assert.Equal(1, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(1, _issuer.Handler.Count(JwksPath));

            _issuer.Now = _issuer.Now.AddSeconds(2);
            await auth.VerifyAccessTokenAsync(token);

            Assert.Equal(2, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public async Task ShorterMaxAgeIsHonoured()
        {
            _issuer.JwksCacheControl = "public, max-age=60";
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddHours(1).ToUnixTimeSeconds()));

            await auth.VerifyAccessTokenAsync(token);
            _issuer.Now = _issuer.Now.AddSeconds(61);
            await auth.VerifyAccessTokenAsync(token);

            Assert.Equal(1, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public async Task MissingCacheControlDefaultsToFiveMinutes()
        {
            _issuer.DiscoveryCacheControl = null;
            _issuer.JwksCacheControl = null;
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddHours(1).ToUnixTimeSeconds()));

            await auth.VerifyAccessTokenAsync(token);
            _issuer.Now = _issuer.Now.AddMinutes(4);
            await auth.VerifyAccessTokenAsync(token);
            Assert.Equal(1, _issuer.Handler.Count(JwksPath));

            _issuer.Now = _issuer.Now.AddMinutes(2);
            await auth.VerifyAccessTokenAsync(token);
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));
        }

        [Theory]
        [InlineData("no-store")]
        [InlineData("no-cache")]
        [InlineData("max-age=0")]
        public async Task UncacheableResponsesAreKeptForOneMinute(string cacheControl)
        {
            // Otherwise every verification, including a flood of tokens with unknown key ids, would fetch the JWKS.
            _issuer.JwksCacheControl = cacheControl;
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddHours(1).ToUnixTimeSeconds()));
            var unknownKid = _issuer.Sign(_issuer.AccessClaims(), keyId: "rotated-away");

            await auth.VerifyAccessTokenAsync(token);
            await auth.VerifyAccessTokenAsync(token);
            for (var i = 0; i < 5; i++)
            {
                await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyAccessTokenAsync(unknownKid));
            }

            Assert.Equal(1, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));

            _issuer.Now = _issuer.Now.AddSeconds(61);
            await auth.VerifyAccessTokenAsync(token);
            Assert.Equal(3, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public void LifetimeFollowsCacheControl()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), IssuerMetadataCache.Lifetime(null));
            Assert.Equal(TimeSpan.FromSeconds(120), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("public, max-age=120")));
            Assert.Equal(TimeSpan.FromMinutes(1), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("no-cache")));
            Assert.Equal(TimeSpan.FromMinutes(1), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("no-store")));
            Assert.Equal(TimeSpan.FromMinutes(1), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("max-age=10")));
            Assert.Equal(TimeSpan.FromMinutes(5), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("public")));
            Assert.Equal(TimeSpan.FromDays(1), IssuerMetadataCache.Lifetime(CacheControlHeaderValue.Parse("max-age=99999999")));
        }

        [Fact]
        public async Task UnknownKidRefetchesTheJwksAtMostOncePerMinute()
        {
            using var auth = _issuer.Create();
            using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddHours(1).ToUnixTimeSeconds()), TestIssuer.Header(kid: "k_next"), next);

            // Warm the cache, then present a token signed by a key the issuer has not published yet.
            await auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims()));
            Assert.Equal(1, _issuer.Handler.Count(JwksPath));

            var first = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyAccessTokenAsync(token));
            Assert.Equal(TokenVerificationReason.UnknownKey, first.Reason);
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));

            // A flood of unknown kids within the minute costs no further requests.
            for (var i = 0; i < 5; i++)
            {
                var again = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyAccessTokenAsync(token));
                Assert.Equal(TokenVerificationReason.UnknownKey, again.Reason);
            }

            Assert.Equal(2, _issuer.Handler.Count(JwksPath));

            // The issuer rotates; still inside the minute, the cached set is used.
            _issuer.Publish("k_next", ECDsa.Create(next.ExportParameters(true)));
            _issuer.Now = _issuer.Now.AddSeconds(30);
            await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyAccessTokenAsync(token));
            Assert.Equal(2, _issuer.Handler.Count(JwksPath));

            // After the minute, one refetch finds the new key.
            _issuer.Now = _issuer.Now.AddSeconds(31);
            var claims = await auth.VerifyAccessTokenAsync(token);
            Assert.Equal(TestIssuer.Url, claims.Issuer);
            Assert.Equal(3, _issuer.Handler.Count(JwksPath));

            await auth.VerifyAccessTokenAsync(token);
            Assert.Equal(3, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public async Task TokenWithoutKidIsCheckedAgainstEveryKey()
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _issuer.Publish("k_other", ECDsa.Create(other.ExportParameters(true)));
            using var auth = _issuer.Create();

            var claims = await auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header(kid: null), other));

            Assert.Equal(TestIssuer.Url, claims.Issuer);
        }

        [Fact]
        public async Task UnusableJwksEntriesAreSkipped()
        {
            using var auth = _issuer.Create();
            var jwks = "{\"keys\":[" +
                "{\"kty\":\"RSA\",\"kid\":\"key-1\",\"n\":\"AQAB\",\"e\":\"AQAB\"}," +
                "{\"kty\":\"EC\",\"crv\":\"P-384\",\"kid\":\"key-1\",\"x\":\"AA\",\"y\":\"AA\"}," +
                "{\"kty\":\"EC\",\"crv\":\"P-256\",\"kid\":\"key-1\",\"x\":\"AAAA\",\"y\":\"AAAA\"}," +
                "{\"kty\":\"EC\",\"crv\":\"P-256\",\"kid\":\"key-1\",\"use\":\"enc\",\"x\":\"AAAA\",\"y\":\"AAAA\"}" +
                "]}";
            using var unusable = System.Text.Json.JsonDocument.Parse(jwks);
            using var published = System.Text.Json.JsonDocument.Parse(_issuer.JwksJson());

            Assert.Empty(SigningKey.ParseSet(unusable.RootElement));
            Assert.Single(SigningKey.ParseSet(published.RootElement));
            await auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims()));
        }

        [Fact]
        public async Task ConcurrentVerificationsFetchOnce()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims());

            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => auth.VerifyAccessTokenAsync(token))));

            Assert.Equal(1, _issuer.Handler.Count(DiscoveryPath));
            Assert.Equal(1, _issuer.Handler.Count(JwksPath));
        }

        [Fact]
        public async Task DiscoveryForAnotherIssuerIsRefused()
        {
            using var handler = new StubHttpHandler(_ => StubHttpHandler.Json(_issuer.DiscoveryJson));
            using var client = new System.Net.Http.HttpClient(handler);
            using var auth = new Didit.Authentication("https://other.auth.didit.me", new AuthenticationOptions { HttpClient = client });

            var error = await Assert.ThrowsAsync<DiditException>(() => auth.GetDiscoveryDocumentAsync());

            Assert.Contains("https://acme.auth.didit.me", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task IssuerWithTrailingSlashMatches()
        {
            using var client = new System.Net.Http.HttpClient(_issuer.Handler, disposeHandler: false);
            using var auth = new Didit.Authentication(TestIssuer.Url + "/", new AuthenticationOptions { HttpClient = client, UtcNow = () => _issuer.Now });

            var claims = await auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims()));

            Assert.Equal(TestIssuer.Url, claims.Issuer);
            Assert.Equal(new Uri(TestIssuer.Url + DiscoveryPath), _issuer.Handler.Requests[0].Uri);
        }

        [Fact]
        public async Task DiscoveryFailureMapsToApiStatusException()
        {
            using var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{\"detail\":\"down\"}", HttpStatusCode.ServiceUnavailable));
            using var client = new System.Net.Http.HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            var error = await Assert.ThrowsAsync<InternalServerException>(() => auth.VerifyAccessTokenAsync(_issuer.Sign(_issuer.AccessClaims())));

            Assert.Equal(503, error.StatusCode);
        }

        [Fact]
        public async Task DiscoveryWithoutJwksUriIsRefused()
        {
            using var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{\"issuer\":\"https://acme.auth.didit.me\",\"jwks_uri\":\"http://acme.auth.didit.me/keys\"}"));
            using var client = new System.Net.Http.HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            await Assert.ThrowsAsync<DiditException>(() => auth.GetDiscoveryDocumentAsync());
        }
    }
}
