using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Didit.Tests.Authentication
{
    public sealed class AuthenticationHelperTests
    {
        private static readonly Regex UrlSafe = new("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);

        [Fact]
        public void PkceMatchesTheRfc7636Vector()
        {
            Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", PkcePair.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        }

        [Fact]
        public void CreatePkceReturnsAnS256Pair()
        {
            var pkce = Didit.Authentication.CreatePkce();

            Assert.Matches(UrlSafe, pkce.CodeVerifier);
            Assert.Matches(UrlSafe, pkce.CodeChallenge);
            Assert.Equal(PkcePair.ComputeChallenge(pkce.CodeVerifier), pkce.CodeChallenge);
            Assert.Equal("S256", pkce.CodeChallengeMethod);
            Assert.NotEqual(pkce.CodeVerifier, Didit.Authentication.CreatePkce().CodeVerifier);
        }

        [Fact]
        public void StateAndNonceAreRandomAndUrlSafe()
        {
            var values = new HashSet<string>();
            for (var i = 0; i < 200; i++)
            {
                var state = Didit.Authentication.CreateState();
                var nonce = Didit.Authentication.CreateNonce();
                Assert.Matches(UrlSafe, state);
                Assert.Matches(UrlSafe, nonce);
                Assert.True(values.Add(state));
                Assert.True(values.Add(nonce));
            }
        }

        [Fact]
        public void ParsesALiveKeyLikeTheBackendEncodesIt()
        {
            // base64.urlsafe_b64encode(b"https://acme.auth.didit.me").decode().rstrip("=")
            var key = PublishableKey.Parse("pk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU");

            Assert.Equal("https://acme.auth.didit.me", key.Issuer);
            Assert.True(key.IsLive);
            Assert.False(key.IsTest);
            Assert.Equal("pk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU", key.ToString());
        }

        [Fact]
        public void ParsesATestKeyWithALocalhostIssuer()
        {
            var key = Didit.Authentication.ParsePublishableKey("pk_test_aHR0cDovL2xvY2FsaG9zdDo4MDAw");

            Assert.Equal("http://localhost:8000", key.Issuer);
            Assert.True(key.IsTest);
        }

        [Fact]
        public void ToleratesPaddingAndSurroundingWhitespace()
        {
            var key = PublishableKey.Parse(" pk_test_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("https://acme.sandbox.auth.didit.me")).Replace('+', '-').Replace('/', '_') + "\n");

            Assert.Equal("https://acme.sandbox.auth.didit.me", key.Issuer);
        }

        [Theory]
        [InlineData("")]
        [InlineData("aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU")]
        [InlineData("pk_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU")]
        [InlineData("sk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU")]
        [InlineData("PK_LIVE_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU")]
        public void RejectsAnInvalidPrefix(string value)
        {
            Assert.Throws<FormatException>(() => PublishableKey.Parse(value));
            Assert.False(PublishableKey.TryParse(value, out _));
        }

        [Theory]
        [InlineData("pk_live_")]
        [InlineData("pk_live_***")]
        [InlineData("pk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU+")]
        [InlineData("pk_live_a")]
        public void RejectsInvalidBase64(string value)
        {
            Assert.Throws<FormatException>(() => PublishableKey.Parse(value));
        }

        [Theory]
        [InlineData("pk_live_bm90IGEgdXJs")]
        [InlineData("pk_live_aHR0cDovL2V2aWwuZXhhbXBsZQ")]
        [InlineData("pk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU_eD0x")]
        [InlineData("pk_live__w")]
        public void RejectsKeysWithoutASecureIssuer(string value)
        {
            Assert.Throws<FormatException>(() => PublishableKey.Parse(value));
            Assert.False(PublishableKey.TryParse(value, out var key));
            Assert.Null(key);
        }

        [Fact]
        public void NullKeyThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => PublishableKey.Parse(null!));
            Assert.False(PublishableKey.TryParse(null, out _));
        }

        [Fact]
        public void FromPublishableKeyUsesTheEncodedIssuer()
        {
            using var auth = Didit.Authentication.FromPublishableKey("pk_live_aHR0cHM6Ly9hY21lLmF1dGguZGlkaXQubWU");

            Assert.Equal("https://acme.auth.didit.me", auth.Issuer);
        }

        [Theory]
        [InlineData("")]
        [InlineData("acme.auth.didit.me")]
        [InlineData("http://acme.auth.didit.me")]
        [InlineData("ftp://acme.auth.didit.me")]
        [InlineData("https://acme.auth.didit.me/?x=1")]
        [InlineData("https://user:pass@acme.auth.didit.me")]
        public void ConstructorRejectsInvalidIssuers(string issuer)
        {
            Assert.Throws<ArgumentException>(() => new Didit.Authentication(issuer));
        }

        [Fact]
        public void ConstructorRejectsInvalidOptions()
        {
            Assert.Throws<ArgumentException>(() => new Didit.Authentication("https://acme.auth.didit.me", new AuthenticationOptions { Timeout = TimeSpan.Zero }));
            Assert.Throws<ArgumentException>(() => new Didit.Authentication("https://acme.auth.didit.me", new AuthenticationOptions { ClockSkew = TimeSpan.FromSeconds(-1) }));
            Assert.Throws<ArgumentException>(() => new Didit.Authentication("https://acme.auth.didit.me", new AuthenticationOptions { ClockSkew = TimeSpan.MaxValue }));
        }

        [Fact]
        public void DefaultsMatchTheDesign()
        {
            var options = new AuthenticationOptions();

            Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(60), options.ClockSkew);
            Assert.Null(options.HttpClient);
            Assert.Null(options.UtcNow);
        }

        [Fact]
        public void LoopbackHttpIssuerIsAllowed()
        {
            using var auth = new Didit.Authentication("http://127.0.0.1:8000");

            Assert.Equal("http://127.0.0.1:8000", auth.Issuer);
        }

        [Fact]
        public void ReasonsAreStable()
        {
            var reasons = new[]
            {
                TokenVerificationReason.Malformed, TokenVerificationReason.UnsupportedAlgorithm, TokenVerificationReason.InvalidType,
                TokenVerificationReason.InvalidIssuer, TokenVerificationReason.InvalidAudience, TokenVerificationReason.Expired,
                TokenVerificationReason.NotYetValid, TokenVerificationReason.IssuedInFuture, TokenVerificationReason.UnknownKey,
                TokenVerificationReason.InvalidSignature, TokenVerificationReason.InvalidNonce,
            };

            Assert.Equal(
                new[] { "malformed", "unsupported_algorithm", "invalid_type", "invalid_issuer", "invalid_audience", "expired", "not_yet_valid", "issued_in_future", "unknown_key", "invalid_signature", "invalid_nonce" },
                reasons);
            Assert.Equal(reasons.Length, reasons.Distinct().Count());
        }
    }
}
