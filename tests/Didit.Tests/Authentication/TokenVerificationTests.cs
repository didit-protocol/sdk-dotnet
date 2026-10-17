using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Didit.Tests.Authentication
{
    public sealed class TokenVerificationTests : IDisposable
    {
        private readonly TestIssuer _issuer = new();

        public void Dispose() => _issuer.Dispose();

        [Fact]
        public async Task ValidAccessTokenReturnsTypedClaims()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims());

            var claims = await auth.VerifyAccessTokenAsync(token, TestIssuer.ClientId);

            Assert.Equal(TestIssuer.Url, claims.Issuer);
            Assert.Equal(new[] { TestIssuer.ClientId }, claims.Audience);
            Assert.Equal(TestIssuer.ClientId, claims.ClientId);
            Assert.Equal("0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90", claims.Subject);
            Assert.Equal("a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57", claims.SessionId);
            Assert.Equal("Jx8bU2nqVt0rWl5m", claims.JwtId);
            Assert.Equal(_issuer.Now, claims.IssuedAt);
            Assert.Equal(_issuer.Now.AddSeconds(300), claims.ExpiresAt);
            Assert.Equal("customer-42", claims.VendorData);
            Assert.Equal(new[] { "passkey" }, claims.Amr);
            Assert.NotNull(claims.Organization);
            Assert.Equal("4d1e8f2a-6b7c-4c3d-9e0f-1a2b3c4d5e6f", claims.Organization!.Id);
            Assert.Equal("admin", claims.Organization.Role);
            Assert.Equal("full_id", claims.Verified.Level);
            Assert.True(claims.Verified.Over18);
            Assert.Equal("Ada", claims.Verified.GivenName);
            Assert.Equal("GB", claims.Verified.Country);
            Assert.Null(claims.Verified.Over21);
            Assert.Null(claims.Actor);
            Assert.Equal("kept", claims.Raw.GetProperty("x_future_claim").GetString());
        }

        [Fact]
        public async Task AudienceIsOptionalAndMayBeAnArray()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("aud", new JsonArray("other", TestIssuer.ClientId)));

            var withoutAudience = await auth.VerifyAccessTokenAsync(token);
            var withAudience = await auth.VerifyAccessTokenAsync(token, TestIssuer.ClientId);

            Assert.Equal(new[] { "other", TestIssuer.ClientId }, withoutAudience.Audience);
            Assert.Equal(withoutAudience.Subject, withAudience.Subject);
        }

        [Fact]
        public async Task AgentTokenExposesTheActor()
        {
            using var auth = _issuer.Create();
            var claims = _issuer.AccessClaims()
                .With("amr", new JsonArray())
                .With("verified", new JsonObject())
                .With("org", null)
                .With("act", new JsonObject { ["sub"] = "agent-7", ["agent"] = "Expense bot", ["scopes"] = new JsonArray("read", "act") });

            var result = await auth.VerifyAccessTokenAsync(_issuer.Sign(claims), TestIssuer.ClientId);

            Assert.NotNull(result.Actor);
            Assert.Equal("agent-7", result.Actor!.Subject);
            Assert.Equal("Expense bot", result.Actor.Agent);
            Assert.Equal(new[] { "read", "act" }, result.Actor.Scopes);
            Assert.Empty(result.Amr);
            Assert.Null(result.Organization);
            Assert.Null(result.Verified.Level);
        }

        [Theory]
        [InlineData("at+jwt")]
        [InlineData("AT+JWT")]
        [InlineData("application/at+jwt")]
        public async Task AccessTokenTypeIsCaseInsensitive(string typ)
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header(typ: typ));

            var claims = await auth.VerifyAccessTokenAsync(token);

            Assert.Equal(TestIssuer.Url, claims.Issuer);
        }

        [Fact]
        public async Task RejectsAlgNone()
        {
            using var auth = _issuer.Create();
            var token = TestIssuer.SignWith(TestIssuer.Header(alg: "none"), _issuer.AccessClaims(), _ => Array.Empty<byte>());

            await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
            Assert.Empty(_issuer.Handler.Requests);
        }

        [Fact]
        public async Task RejectsHs256SignedWithThePublicKey()
        {
            using var auth = _issuer.Create();
            var point = _issuer.Key.ExportParameters(false).Q;
            var jwkBytes = Encoding.UTF8.GetBytes(TestIssuer.Jwk(TestIssuer.Kid, _issuer.Key));
            var pointBytes = new byte[] { 0x04 }.Concat(point.X!).Concat(point.Y!).ToArray();

            foreach (var secret in new[] { jwkBytes, pointBytes })
            {
                using var hmac = new HMACSHA256(secret);
                var token = TestIssuer.SignWith(TestIssuer.Header(alg: "HS256"), _issuer.AccessClaims(), hmac.ComputeHash);
                await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
            }
        }

        [Fact]
        public async Task RejectsRs256()
        {
            using var auth = _issuer.Create();
            using var rsa = RSA.Create(2048);
            var token = TestIssuer.SignWith(TestIssuer.Header(alg: "RS256"), _issuer.AccessClaims(), data => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
        }

        [Fact]
        public async Task RejectsEs384()
        {
            using var auth = _issuer.Create();
            using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var token = TestIssuer.SignWith(TestIssuer.Header(alg: "ES384"), _issuer.AccessClaims(), data => p384.SignData(data, HashAlgorithmName.SHA384));

            await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
        }

        [Theory]
        [InlineData("es256")]
        [InlineData("PS256")]
        [InlineData("EdDSA")]
        [InlineData("")]
        public async Task RejectsEveryOtherAlgorithmName(string alg)
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header(alg: alg));

            await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
        }

        [Fact]
        public async Task RejectsAMissingAlgorithm()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header().Without("alg"));

            await AssertRejected(auth, token, TokenVerificationReason.UnsupportedAlgorithm);
        }

        [Theory]
        [InlineData("JWT")]
        [InlineData("application/jwt")]
        [InlineData("at+jwt+extra")]
        [InlineData(null)]
        public async Task RejectsTheWrongAccessTokenType(string? typ)
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header(typ: typ));

            await AssertRejected(auth, token, TokenVerificationReason.InvalidType);
        }

        [Fact]
        public async Task RejectsAnIdTokenPresentedAsAnAccessToken()
        {
            using var auth = _issuer.Create();
            var idToken = _issuer.Sign(_issuer.IdClaims("n-1"), TestIssuer.Header(typ: "JWT"));

            await AssertRejected(auth, idToken, TokenVerificationReason.InvalidType);
        }

        [Theory]
        [InlineData("https://evil.auth.didit.me")]
        [InlineData("https://acme.auth.didit.me.evil.example")]
        [InlineData("http://acme.auth.didit.me")]
        public async Task RejectsTheWrongIssuer(string iss)
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("iss", iss));

            await AssertRejected(auth, token, TokenVerificationReason.InvalidIssuer);
        }

        [Fact]
        public async Task RejectsAMissingIssuer()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().Without("iss"));

            await AssertRejected(auth, token, TokenVerificationReason.InvalidIssuer);
        }

        [Fact]
        public async Task RejectsTheWrongAudience()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims());

            await AssertRejected(auth, token, TokenVerificationReason.InvalidAudience, audience: "another-application");
            var noAudience = _issuer.Sign(_issuer.AccessClaims().Without("aud"));
            await AssertRejected(auth, noAudience, TokenVerificationReason.InvalidAudience, audience: TestIssuer.ClientId);
        }

        [Fact]
        public async Task RejectsAnExpiredTokenBeyondTheClockSkew()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("iat", _issuer.Now.AddMinutes(-10).ToUnixTimeSeconds()).With("exp", _issuer.Now.AddSeconds(-61).ToUnixTimeSeconds()));

            await AssertRejected(auth, token, TokenVerificationReason.Expired);
        }

        [Fact]
        public async Task AcceptsAnExpiredTokenWithinTheClockSkew()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.AddSeconds(-59).ToUnixTimeSeconds()));

            var claims = await auth.VerifyAccessTokenAsync(token);

            Assert.Equal(_issuer.Now.AddSeconds(-59), claims.ExpiresAt);
        }

        [Fact]
        public async Task ClockSkewIsConfigurable()
        {
            using var strict = _issuer.Create(clockSkew: TimeSpan.Zero);
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", _issuer.Now.ToUnixTimeSeconds()));

            await AssertRejected(strict, token, TokenVerificationReason.Expired);
        }

        [Fact]
        public async Task RejectsATokenNotYetValid()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("nbf", _issuer.Now.AddSeconds(61).ToUnixTimeSeconds()));
            var withinSkew = _issuer.Sign(_issuer.AccessClaims().With("nbf", _issuer.Now.AddSeconds(59).ToUnixTimeSeconds()));

            await AssertRejected(auth, token, TokenVerificationReason.NotYetValid);
            await auth.VerifyAccessTokenAsync(withinSkew);
        }

        [Fact]
        public async Task RejectsATokenIssuedInTheFuture()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("iat", _issuer.Now.AddSeconds(61).ToUnixTimeSeconds()));
            var withinSkew = _issuer.Sign(_issuer.AccessClaims().With("iat", _issuer.Now.AddSeconds(59).ToUnixTimeSeconds()));

            await AssertRejected(auth, token, TokenVerificationReason.IssuedInFuture);
            await auth.VerifyAccessTokenAsync(withinSkew);
        }

        [Theory]
        [InlineData("exp")]
        [InlineData("iat")]
        [InlineData("sub")]
        public async Task RejectsATokenWithoutARequiredClaim(string claim)
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().Without(claim));

            await AssertRejected(auth, token, TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task RejectsANonNumericDate()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims().With("exp", "tomorrow"));

            await AssertRejected(auth, token, TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task RejectsATamperedPayload()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims());
            var parts = token.Split('.');
            var forged = TestIssuer.B64(Encoding.UTF8.GetBytes(_issuer.AccessClaims().With("sub", "someone-else").ToJsonString()));

            await AssertRejected(auth, parts[0] + "." + forged + "." + parts[2], TokenVerificationReason.InvalidSignature);
        }

        [Fact]
        public async Task RejectsATokenSignedByAnotherKeyWithTheSameKid()
        {
            using var auth = _issuer.Create();
            using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            await AssertRejected(auth, _issuer.Sign(_issuer.AccessClaims(), key: attacker), TokenVerificationReason.InvalidSignature);
        }

        [Fact]
        public async Task RejectsADerEncodedSignature()
        {
            using var auth = _issuer.Create();
            var token = TestIssuer.SignWith(TestIssuer.Header(), _issuer.AccessClaims(), data => ToDer(_issuer.Key.SignData(data, HashAlgorithmName.SHA256)));

            await AssertRejected(auth, token, TokenVerificationReason.InvalidSignature);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-jwt")]
        [InlineData("a.b")]
        [InlineData("a.b.c.d")]
        [InlineData("a.b.c.d.e")]
        [InlineData("!!!.???.***")]
        [InlineData("eyJhbGciOiJFUzI1NiJ9=.e30.AA")]
        public async Task RejectsMalformedTokens(string token)
        {
            using var auth = _issuer.Create();

            await AssertRejected(auth, token, TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task RejectsNonJsonOrNonObjectParts()
        {
            using var auth = _issuer.Create();
            var header = TestIssuer.B64(Encoding.UTF8.GetBytes(TestIssuer.Header().ToJsonString()));
            var notJson = TestIssuer.B64(Encoding.UTF8.GetBytes("{not json"));
            var array = TestIssuer.B64(Encoding.UTF8.GetBytes("[1,2]"));

            await AssertRejected(auth, notJson + "." + array + ".AA", TokenVerificationReason.Malformed);
            await AssertRejected(auth, header + "." + array + ".AA", TokenVerificationReason.Malformed);
            await AssertRejected(auth, header + "." + notJson + ".AA", TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task RejectsCriticalHeaderExtensions()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.AccessClaims(), TestIssuer.Header().With("crit", new JsonArray("exp")));

            await AssertRejected(auth, token, TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task RejectsAnOversizedToken()
        {
            using var auth = _issuer.Create();

            await AssertRejected(auth, new string('a', 70_000), TokenVerificationReason.Malformed);
        }

        [Fact]
        public async Task NullTokenThrowsArgumentNullException()
        {
            using var auth = _issuer.Create();

            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.VerifyAccessTokenAsync(null!));
        }

        [Fact]
        public async Task ValidIdTokenReturnsTypedClaims()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.IdClaims("nonce-123"), TestIssuer.Header(typ: "JWT"));

            var claims = await auth.VerifyIdTokenAsync(token, "nonce-123", TestIssuer.ClientId);

            Assert.Equal("0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90", claims.Subject);
            Assert.Equal("nonce-123", claims.Nonce);
            Assert.Equal("ada@example.com", claims.Email);
            Assert.False(claims.EmailVerified);
            Assert.Equal("Ada Lovelace", claims.Name);
            Assert.Equal("+34600000000", claims.PhoneNumber);
            Assert.Equal(_issuer.Now.AddSeconds(-30), claims.AuthTime);
            Assert.Equal(new[] { "password" }, claims.Amr);
            Assert.Null(claims.Organization);
            Assert.Null(claims.Verified.Level);
        }

        [Fact]
        public async Task IdTokenTypeMayBeAbsent()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.IdClaims("n"), TestIssuer.Header(typ: null));

            var claims = await auth.VerifyIdTokenAsync(token, "n", TestIssuer.ClientId);

            Assert.Equal("n", claims.Nonce);
        }

        [Fact]
        public async Task RejectsAnIdTokenWithTheWrongNonce()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.IdClaims("nonce-123"), TestIssuer.Header(typ: "JWT"));
            var noNonce = _issuer.Sign(_issuer.IdClaims("x").Without("nonce"), TestIssuer.Header(typ: "JWT"));

            var mismatch = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyIdTokenAsync(token, "nonce-456", TestIssuer.ClientId));
            var missing = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyIdTokenAsync(noNonce, "nonce-123", TestIssuer.ClientId));

            Assert.Equal(TokenVerificationReason.InvalidNonce, mismatch.Reason);
            Assert.Equal(TokenVerificationReason.InvalidNonce, missing.Reason);
        }

        [Fact]
        public async Task RejectsAnAccessTokenPresentedAsAnIdToken()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.IdClaims("n"), TestIssuer.Header(typ: "at+jwt"));

            var error = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyIdTokenAsync(token, "n", TestIssuer.ClientId));

            Assert.Equal(TokenVerificationReason.InvalidType, error.Reason);
        }

        [Fact]
        public async Task RejectsAnIdTokenForAnotherClient()
        {
            using var auth = _issuer.Create();
            var wrongAudience = _issuer.Sign(_issuer.IdClaims("n"), TestIssuer.Header(typ: "JWT"));
            var wrongParty = _issuer.Sign(_issuer.IdClaims("n").With("aud", new JsonArray(TestIssuer.ClientId, "other")).With("azp", "other"), TestIssuer.Header(typ: "JWT"));

            var audience = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyIdTokenAsync(wrongAudience, "n", "other"));
            var party = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyIdTokenAsync(wrongParty, "n", TestIssuer.ClientId));

            Assert.Equal(TokenVerificationReason.InvalidAudience, audience.Reason);
            Assert.Equal(TokenVerificationReason.InvalidAudience, party.Reason);
        }

        [Fact]
        public async Task IdTokenRequiresNonceAndAudience()
        {
            using var auth = _issuer.Create();
            var token = _issuer.Sign(_issuer.IdClaims("n"), TestIssuer.Header(typ: "JWT"));

            await Assert.ThrowsAsync<ArgumentException>(() => auth.VerifyIdTokenAsync(token, string.Empty, TestIssuer.ClientId));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.VerifyIdTokenAsync(token, "n", string.Empty));
        }

        [Fact]
        public void TokenVerificationExceptionIsADiditException()
        {
            var error = new TokenVerificationException(TokenVerificationReason.Expired, "expired");

            Assert.IsAssignableFrom<DiditException>(error);
            Assert.Equal("expired", error.Reason);
        }

        // The same signature as an ASN.1 DER SEQUENCE of two INTEGERs, which JWS does not use.
        private static byte[] ToDer(byte[] p1363)
        {
            static byte[] Integer(byte[] value)
            {
                var trimmed = value.SkipWhile((b, i) => b == 0 && i < value.Length - 1).ToArray();
                var body = (trimmed[0] & 0x80) != 0 ? new byte[] { 0 }.Concat(trimmed).ToArray() : trimmed;
                return new byte[] { 0x02, (byte)body.Length }.Concat(body).ToArray();
            }

            var r = Integer(p1363.Take(32).ToArray());
            var s = Integer(p1363.Skip(32).ToArray());
            return new byte[] { 0x30, (byte)(r.Length + s.Length) }.Concat(r).Concat(s).ToArray();
        }

        private static async Task AssertRejected(Didit.Authentication auth, string token, string reason, string? audience = null)
        {
            var error = await Assert.ThrowsAsync<TokenVerificationException>(() => auth.VerifyAccessTokenAsync(token, audience));
            Assert.Equal(reason, error.Reason);
        }
    }
}
