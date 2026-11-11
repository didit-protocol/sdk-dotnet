using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Didit.Tests.Authentication
{
    public sealed class AuthenticationEndpointTests : IDisposable
    {
        private const string AccessToken = "eyJhbGciOiJFUzI1NiJ9.access-token-under-test.sig";
        private const string TokenResponse =
            "{\"access_token\":\"at.jwt.value\",\"id_token\":\"id.jwt.value\",\"refresh_token\":\"rt_new\",\"token_type\":\"Bearer\",\"expires_in\":299," +
            "\"session_id\":\"a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57\",\"x_future\":1}";

        private readonly TestIssuer _issuer = new();

        public void Dispose() => _issuer.Dispose();

        [Fact]
        public async Task ExchangeCodePostsTheFormToTheTokenEndpoint()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(TokenResponse);
            using var auth = _issuer.Create();

            var tokens = await auth.ExchangeCodeAsync("code-abc", "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", "https://app.example.com/callback");

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/token/"), request.Uri);
            Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = "code-abc",
                    ["code_verifier"] = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
                    ["redirect_uri"] = "https://app.example.com/callback",
                },
                Form(request.Body!));
            Assert.False(request.Headers.ContainsKey("Authorization"));
            AssertSdkHeaders(request);

            Assert.Equal("at.jwt.value", tokens.AccessToken);
            Assert.Equal("id.jwt.value", tokens.IdToken);
            Assert.Equal("rt_new", tokens.RefreshToken);
            Assert.Equal("Bearer", tokens.TokenType);
            Assert.Equal(299, tokens.ExpiresIn);
            Assert.Equal("a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57", tokens.SessionId);
            Assert.Equal(1, tokens.Raw.GetProperty("x_future").GetInt32());
        }

        [Fact]
        public async Task RefreshPostsTheRefreshGrant()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(TokenResponse);
            using var auth = _issuer.Create();

            var tokens = await auth.RefreshAsync("rt_old");

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/token/"), request.Uri);
            Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
            Assert.Equal(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "rt_old" }, Form(request.Body!));
            Assert.Equal("rt_new", tokens.RefreshToken);
        }

        [Fact]
        public async Task TokenEndpointComesFromDiscovery()
        {
            _issuer.TokenEndpoint = TestIssuer.Url + "/oauth/token/";
            _issuer.Runtime = _ => StubHttpHandler.Json(TokenResponse);
            using var auth = _issuer.Create();

            await auth.RefreshAsync("rt_old");

            Assert.Equal(new Uri(TestIssuer.Url + "/oauth/token/"), Runtime().Single().Uri);
        }

        [Fact]
        public async Task TokenEndpointFallsBackToTheIssuer()
        {
            _issuer.TokenEndpoint = null;
            _issuer.Runtime = _ => StubHttpHandler.Json(TokenResponse);
            using var auth = _issuer.Create();

            await auth.RefreshAsync("rt_old");

            Assert.Equal(new Uri(TestIssuer.Url + "/token/"), Runtime().Single().Uri);
        }

        [Fact]
        public async Task InvalidGrantIsABadRequestWithTheTokenRedacted()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"error\":\"invalid_grant\",\"echo\":\"rt_consumed\"}", HttpStatusCode.BadRequest);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<BadRequestException>(() => auth.RefreshAsync("rt_consumed"));

            Assert.Equal(400, error.StatusCode);
            Assert.Contains("invalid_grant", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("rt_consumed", error.RawBody, StringComparison.Ordinal);
            Assert.Equal("invalid_grant", error.Body!.Value.GetProperty("error").GetString());
            Assert.Equal("[redacted]", error.Body!.Value.GetProperty("echo").GetString());
            Assert.Single(Runtime());
        }

        [Fact]
        public async Task CodeExchangeErrorsRedactTheCodeAndTheVerifier()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"error\":\"invalid_grant\",\"code\":\"code-abc\",\"detail\":\"verifier verifier-xyz does not match\"}", HttpStatusCode.BadRequest);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<BadRequestException>(() => auth.ExchangeCodeAsync("code-abc", "verifier-xyz", "https://app.example.com/callback"));

            foreach (var text in new[] { error.Message, error.RawBody!, error.Body!.Value.GetRawText() })
            {
                Assert.DoesNotContain("code-abc", text, StringComparison.Ordinal);
                Assert.DoesNotContain("verifier-xyz", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task TokenResponseWithoutAccessTokenIsRefused()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"token_type\":\"Bearer\"}");
            using var auth = _issuer.Create();

            await Assert.ThrowsAsync<DiditException>(() => auth.RefreshAsync("rt_old"));
        }

        [Fact]
        public async Task GetSessionSendsTheBearerTokenAndParsesTheSession()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(
                "{\"sub\":\"0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90\",\"sid\":\"a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57\"," +
                "\"application_id\":\"6c7e3a52-9b1f-4c1a-8f5e-0d6c2b9a1e47\",\"vendor_data\":\"customer-42\",\"amr\":[\"passkey\"]," +
                "\"mfa_verified\":true,\"email\":\"ada@example.com\",\"email_verified\":true,\"phone\":null,\"verified_identity\":null," +
                "\"verified\":{\"level\":\"liveness\",\"liveness\":true,\"over_21\":false},\"expires_at\":\"2026-10-24T12:00:00.123456Z\"," +
                "\"post_logout_redirect_uri\":\"https://app.example.com/bye\"," +
                "\"organizations\":[{\"organization_id\":\"4d1e8f2a-6b7c-4c3d-9e0f-1a2b3c4d5e6f\",\"organization__name\":\"Acme\",\"role\":\"member\"," +
                "\"custom_role\":{\"key\":\"finance\",\"permissions\":[\"payouts.approve\"]}}]}");
            using var auth = _issuer.Create();

            var session = await auth.GetSessionAsync(AccessToken);

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/session/"), request.Uri);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            AssertSdkHeaders(request);

            Assert.Equal("0b5c1f7e-3d2a-4e8b-9a61-5f4c2d7e8a90", session.Subject);
            Assert.Equal("a7e2c9d4-1b3f-4a5e-8c6d-2f9b0e1a3c57", session.SessionId);
            Assert.Equal(TestIssuer.ClientId, session.ApplicationId);
            Assert.Equal("customer-42", session.VendorData);
            Assert.Equal(new[] { "passkey" }, session.Amr);
            Assert.True(session.MfaVerified);
            Assert.Equal("ada@example.com", session.Email);
            Assert.True(session.EmailVerified);
            Assert.Null(session.Phone);
            Assert.Equal("liveness", session.Verified.Level);
            Assert.True(session.Verified.Liveness);
            Assert.False(session.Verified.Over21);
            Assert.Equal(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), session.ExpiresAt);
            Assert.Equal("https://app.example.com/bye", session.PostLogoutRedirectUri);
            var organization = Assert.Single(session.Organizations);
            Assert.Equal("4d1e8f2a-6b7c-4c3d-9e0f-1a2b3c4d5e6f", organization.OrganizationId);
            Assert.Equal("Acme", organization.OrganizationName);
            Assert.Equal("member", organization.Role);
            Assert.Equal("finance", organization.CustomRoleKey);
            Assert.Equal(new[] { "payouts.approve" }, organization.CustomRolePermissions);
            Assert.Equal(JsonValueKind.Null, session.Raw.GetProperty("verified_identity").ValueKind);
        }

        [Fact]
        public async Task InvalidTokenIsAnAuthenticationExceptionWithoutTheToken()
        {
            _issuer.Runtime = _ =>
            {
                var response = StubHttpHandler.Json("{\"detail\":\"invalid_token\"}", HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");
                response.Headers.TryAddWithoutValidation("X-Request-Id", "req_123");
                return response;
            };
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<AuthenticationException>(() => auth.GetSessionAsync(AccessToken));

            Assert.Equal(401, error.StatusCode);
            Assert.Contains("invalid_token", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(AccessToken, error.Message, StringComparison.Ordinal);
            Assert.Equal("req_123", error.RequestId);
            Assert.Equal("Bearer", error.Headers["www-authenticate"].Single());
        }

        [Fact]
        public async Task RevokeSessionSendsDelete()
        {
            _issuer.Runtime = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
            using var auth = _issuer.Create();

            await auth.RevokeSessionAsync(AccessToken);

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/session/"), request.Uri);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            Assert.Null(request.Body);
        }

        [Fact]
        public async Task CreateStepUpPostsJson()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(
                "{\"id\":\"00000000-0000-4000-8000-000000000001\",\"rule\":\"payout\",\"method\":\"selfie\",\"status\":\"pending\",\"level\":null," +
                "\"amount\":\"250.00\",\"currency\":\"EUR\",\"created_at\":\"2026-10-17T12:00:00Z\",\"completed_at\":null," +
                "\"expires_at\":\"2026-10-17T12:30:00Z\",\"required\":true,\"verification_url\":\"https://acme.auth.didit.me/verify/?verification=abc\"," +
                "\"expires_in\":1800}",
                HttpStatusCode.Created);
            using var auth = _issuer.Create();

            var stepUp = await auth.CreateStepUpAsync(AccessToken, "payout", "250.00", "EUR", "https://app.example.com/payouts");

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/step-up/"), request.Uri);
            Assert.Equal("application/json", request.ContentType);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            Assert.Equal("{\"action\":\"payout\",\"amount\":\"250.00\",\"currency\":\"EUR\",\"return_to\":\"https://app.example.com/payouts\"}", request.Body);

            Assert.True(stepUp.Required);
            Assert.Equal("00000000-0000-4000-8000-000000000001", stepUp.Id);
            Assert.Equal("payout", stepUp.Rule);
            Assert.Equal("selfie", stepUp.Method);
            Assert.Equal(AuthStepUpStatus.Pending, stepUp.Status);
            Assert.False(stepUp.IsApproved);
            Assert.Equal("250.00", stepUp.Amount);
            Assert.Equal("EUR", stepUp.Currency);
            Assert.Equal("https://acme.auth.didit.me/verify/?verification=abc", stepUp.VerificationUrl);
            Assert.Equal(1800, stepUp.ExpiresIn);
            Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 30, 0, TimeSpan.Zero), stepUp.ExpiresAt);
            Assert.Null(stepUp.CompletedAt);
        }

        [Fact]
        public async Task CreateStepUpOmitsOptionalFields()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"required\":false}");
            using var auth = _issuer.Create();

            var stepUp = await auth.CreateStepUpAsync(AccessToken, "bank", returnTo: "https://app.example.com/bank");

            Assert.Equal("{\"action\":\"bank\",\"return_to\":\"https://app.example.com/bank\"}", Runtime().Single().Body);
            Assert.False(stepUp.Required);
            Assert.Null(stepUp.Id);
            Assert.Null(stepUp.Status);
            Assert.False(stepUp.IsApproved);
        }

        [Fact]
        public async Task RetrieveStepUpReadsTheStatus()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(
                "{\"id\":\"00000000-0000-4000-8000-000000000001\",\"rule\":\"payout\",\"method\":\"selfie\",\"status\":\"approved\"," +
                "\"level\":\"liveness_face_match\",\"amount\":\"250.00\",\"currency\":\"EUR\",\"created_at\":\"2026-10-17T12:00:00Z\"," +
                "\"completed_at\":\"2026-10-17T12:04:00Z\",\"expires_at\":\"2026-10-17T12:30:00Z\"}");
            using var auth = _issuer.Create();

            var stepUp = await auth.RetrieveStepUpAsync(AccessToken, "00000000-0000-4000-8000-000000000001");

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/step-up/00000000-0000-4000-8000-000000000001/"), request.Uri);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            Assert.True(stepUp.IsApproved);
            Assert.True(stepUp.Required);
            Assert.Equal("liveness_face_match", stepUp.Level);
            Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 4, 0, TimeSpan.Zero), stepUp.CompletedAt);
        }

        [Fact]
        public async Task RetrieveStepUpEscapesTheId()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"detail\":\"Not found.\"}", HttpStatusCode.NotFound);
            using var auth = _issuer.Create();

            await Assert.ThrowsAsync<NotFoundException>(() => auth.RetrieveStepUpAsync(AccessToken, "../session"));

            Assert.Equal(TestIssuer.Url + "/step-up/..%2Fsession/", Runtime().Single().Uri.AbsoluteUri);
        }

        [Theory]
        [InlineData(".")]
        [InlineData("..")]
        public async Task RetrieveStepUpRefusesDotSegmentIds(string id)
        {
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<ArgumentException>(() => auth.RetrieveStepUpAsync(AccessToken, id));

            Assert.Equal("stepUpId", error.ParamName);
            Assert.Empty(_issuer.Handler.Requests);
        }

        private static AuthAgentPasskeyAssertion Assertion() => new()
        {
            Id = "Y3JlZC1pZA",
            RawId = "Y3JlZC1pZA",
            ClientDataJson = "client-data-json-b64",
            AuthenticatorData = "auth-data-b64",
            Signature = "signature-b64-padpad0123456",
        };

        [Fact]
        public async Task SubmitAgentConsentApprovePostsJson()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"code\":\"agent-code\",\"redirect_uri\":\"https://agent.example/callback\",\"state\":\"s1\",\"x_future\":1}");
            using var auth = _issuer.Create();

            var result = await auth.SubmitAgentConsentAsync(
                AccessToken,
                "agent-request-secret",
                AuthAgentConsentDecision.Approve,
                Assertion(),
                scopes: new[] { "read" },
                organizationIds: new[] { "00000000-0000-4000-8000-000000000002" },
                lifetime: 86400);

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/agents/consent/"), request.Uri);
            Assert.Equal("application/json", request.ContentType);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            Assert.Equal(
                "{\"challenge\":\"agent-request-secret\",\"decision\":\"approve\",\"assertion\":{\"id\":\"Y3JlZC1pZA\",\"rawId\":\"Y3JlZC1pZA\",\"type\":\"public-key\"," +
                "\"response\":{\"clientDataJSON\":\"client-data-json-b64\",\"authenticatorData\":\"auth-data-b64\",\"signature\":\"signature-b64-padpad0123456\"}}," +
                "\"scopes\":[\"read\"],\"organization_ids\":[\"00000000-0000-4000-8000-000000000002\"],\"lifetime\":86400}",
                request.Body);
            Assert.True(result.IsApproved);
            Assert.False(result.IsDenied);
            Assert.Equal("agent-code", result.Code);
            Assert.Equal("https://agent.example/callback", result.RedirectUri);
            Assert.Equal("s1", result.State);
            Assert.Null(result.Status);
            Assert.Equal(1, result.Raw.GetProperty("x_future").GetInt32());
        }

        [Fact]
        public async Task SubmitAgentConsentDenyOmitsOptionalFields()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"status\":\"denied\"}");
            using var auth = _issuer.Create();

            var result = await auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Deny);

            Assert.Equal("{\"challenge\":\"agent-request-secret\",\"decision\":\"deny\"}", Runtime().Single().Body);
            Assert.True(result.IsDenied);
            Assert.False(result.IsApproved);
            Assert.Equal("denied", result.Status);
            Assert.Null(result.Code);
        }

        [Fact]
        public async Task CreateAgentApprovalOptionsReturnsTheWebAuthnOptions()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(
                "{\"options\":{\"challenge\":\"c3ludGhldGljLWNoYWxsZW5nZQ\",\"rpId\":\"acme.auth.didit.me\",\"timeout\":60000,\"userVerification\":\"required\"," +
                "\"allowCredentials\":[{\"id\":\"Y3JlZA\",\"type\":\"public-key\",\"transports\":[\"internal\"]}]}}");
            using var auth = _issuer.Create();

            var result = await auth.CreateAgentApprovalOptionsAsync(AccessToken, "agent-request-secret");

            var request = Runtime().Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(TestIssuer.Url + "/agents/consent/passkey/"), request.Uri);
            Assert.Equal("Bearer " + AccessToken, request.Headers["Authorization"]);
            Assert.Equal("{\"challenge\":\"agent-request-secret\"}", request.Body);
            Assert.Equal("c3ludGhldGljLWNoYWxsZW5nZQ", result.Options.Challenge);
            Assert.Equal("acme.auth.didit.me", result.Options.RpId);
            Assert.Equal(60000, result.Options.Timeout);
            Assert.Equal("required", result.Options.UserVerification);
            var credential = Assert.Single(result.Options.AllowCredentials);
            Assert.Equal("Y3JlZA", credential.Id);
            Assert.Equal("public-key", credential.Type);
            Assert.Equal(new[] { "internal" }, credential.Transports);
        }

        [Fact]
        public async Task AgentConsentInvalidGrantIsABadRequestWithTheChallengeAndAssertionRedacted()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(
                "{\"error\":\"invalid_grant\",\"echo\":\"agent-request-secret signature-b64-padpad0123456\"}",
                HttpStatusCode.BadRequest);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<BadRequestException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve, Assertion()));

            Assert.Equal(400, error.StatusCode);
            // The whole string that contains a secret is replaced, not just the secret.
            Assert.Equal("[redacted]", error.Body!.Value.GetProperty("echo").GetString());
            Assert.DoesNotContain("agent-request-secret", error.Message, StringComparison.Ordinal);
        }

        private static readonly string[] ConsentMarkers =
        {
            "mk-id\"q/1+-padpad0123456", "mk-rawid\\b/2+-padpad0123456", "mk-cdj\"c/3+\u00e9-padpad0123456", "mk-authdata/4+-padpad0123456", "mk-sig\"/5+-padpad0123456", "mk-handle/6+-padpad0123456",
            "mk-ext-top/7+-padpad0123456", "mk-ext-deep\"/8+-padpad0123456", "mk-ext-array/9+-padpad0123456", "mk-challenge\"/10+-padpad0123456",
        };

        private static AuthAgentPasskeyAssertion MarkedAssertion() => new()
        {
            Id = ConsentMarkers[0],
            RawId = ConsentMarkers[1],
            ClientDataJson = ConsentMarkers[2],
            AuthenticatorData = ConsentMarkers[3],
            Signature = ConsentMarkers[4],
            UserHandle = ConsentMarkers[5],
            ClientExtensionResults = JsonDocument.Parse(
                "{\"mk-key-a-padpad0123456\":\"" + ConsentMarkers[6].Replace("/", "\\/") + "\",\"mk-key-b-padpad0123456\":{\"mk-key-c-padpad0123456\":[{\"mk-key-d-padpad0123456\":\"" + ConsentMarkers[7].Replace("\"", "\\\"") + "\"},\"" + ConsentMarkers[8] + "\"]}}").RootElement.Clone(),
        };

        private static string EscapedEcho()
        {
            // The same strings in three forms: decoded values, System.Text.Json escaping and the "\/" escaping other issuers use.
            var all = string.Join(" ", ConsentMarkers);
            var echo = new System.Text.Json.Nodes.JsonObject
            {
                ["detail"] = "rejected " + all,
                ["error"] = "invalid_grant",
                ["fields"] = new System.Text.Json.Nodes.JsonArray(ConsentMarkers.Select(m => (System.Text.Json.Nodes.JsonNode?)m).ToArray()),
                [ConsentMarkers[2]] = new System.Text.Json.Nodes.JsonObject { ["nested"] = ConsentMarkers[4] },
            };
            var text = echo.ToJsonString();
            var slashEscaped = "\"slash\":\"" + string.Join(" ", ConsentMarkers).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("/", "\\/") + "\"";
            return text.Substring(0, text.Length - 1) + "," + slashEscaped + "}";
        }

        private static void AssertNoConsentSecrets(Exception error, params string[] extra)
        {
            var visible = new List<string> { error.Message, error.ToString() };
            for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            {
                visible.Add(inner.Message);
            }

            if (error is ApiStatusException status)
            {
                visible.Add(status.RawBody ?? string.Empty);
                visible.Add(status.Body?.GetRawText() ?? string.Empty);
                visible.AddRange(status.Headers.SelectMany(h => h.Value.Append(h.Key)));
                if (status.Body is { } body)
                {
                    visible.AddRange(Strings(body));
                }
            }

            foreach (var text in visible)
            {
                foreach (var marker in ConsentMarkers.Concat(extra))
                {
                    Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
                    // The JSON-escaped forms of the marker.
                    Assert.DoesNotContain(JsonSerializer.Serialize(marker).Trim('"'), text, StringComparison.Ordinal);
                    Assert.DoesNotContain(JsonSerializer.Serialize(marker).Trim('"').Replace("/", "\\/"), text, StringComparison.Ordinal);
                    Assert.DoesNotContain(marker.Replace("\"", "\\\"").Replace("/", "\\/"), text, StringComparison.Ordinal);
                }
            }
        }

        private static IEnumerable<string> Strings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    yield return element.GetString()!;
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        foreach (var text in Strings(item))
                        {
                            yield return text;
                        }
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        yield return property.Name;
                        foreach (var text in Strings(property.Value))
                        {
                            yield return text;
                        }
                    }

                    break;
            }
        }

        [Fact]
        public async Task AgentConsentErrorNeverCarriesAnyAssertionStringTheChallengeOrTheToken()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json(EscapedEcho(), HttpStatusCode.BadRequest);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<BadRequestException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, ConsentMarkers[9], AuthAgentConsentDecision.Approve, MarkedAssertion()));

            Assert.Equal(400, error.StatusCode);
            AssertNoConsentSecrets(error, AccessToken);
        }

        private static HttpResponseMessage Raw(string text, HttpStatusCode status, string contentType) =>
            new(status) { Content = new StringContent(text, System.Text.Encoding.UTF8, contentType) };

        private async Task<BadRequestException> ConsentFailureAsync(AuthAgentPasskeyAssertion assertion, HttpResponseMessage reply)
        {
            _issuer.Runtime = _ => reply;
            using var auth = _issuer.Create();
            return await Assert.ThrowsAsync<BadRequestException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve, assertion));
        }

        private static AuthAgentPasskeyAssertion AssertionWith(string id, string signature) => new()
        {
            Id = id,
            RawId = "raw-x",
            ClientDataJson = "cdj-x",
            AuthenticatorData = "ad-x",
            Signature = signature,
        };

        private static string Shown(ApiStatusException error) => error.Message + "\n" + error.RawBody + "\n" + error.Body?.GetRawText();

        [Fact]
        public async Task ConsentRedactionLeavesNoFragmentOfAPrefixedSecret()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("abcCREDENTIALID0123", "abcCREDENTIALID0123456"),
                StubHttpHandler.Json("{\"detail\":\"bad abcCREDENTIALID0123456 and abcCREDENTIALID0123\",\"other\":\"abcCREDENTIALID0123456\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"detail\":\"[redacted]\",\"other\":\"[redacted]\"}", error.RawBody);
            Assert.DoesNotContain("abcCRED", Shown(error), StringComparison.Ordinal);
            Assert.DoesNotContain("456", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionReplacesTheWholeStringThatContainsASecret()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("SECRET-0123456789abcdef", "s"),
                StubHttpHandler.Json("{\"detail\":\"bad assertion xyzSECRET-0123456789abcdefxyz\",\"keep\":\"fine\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"detail\":\"[redacted]\",\"keep\":\"fine\"}", error.RawBody);
            Assert.Contains("[redacted]", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-0123456789abcdef", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionHandlesASecretUsedAsAnObjectKey()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("SECRET-0123456789abcdef", "s"),
                StubHttpHandler.Json("{\"detail\":\"no\",\"echo-SECRET-0123456789abcdef-1\":\"a\",\"echo-SECRET-0123456789abcdef-2\":\"b\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"detail\":\"no\",\"[redacted]\":\"a\",\"[redacted]2\":\"b\"}", error.RawBody);
            Assert.DoesNotContain("SECRET-0123456789abcdef", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionIgnoresTheSpellingOfJsonEscapes()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("a/b+\u00e9-padding0123456", "s"),
                Raw("{\"detail\":\"x\",\"echo\":\"a\\/b+\\u00E9-padding0123456\"}", HttpStatusCode.BadRequest, "application/json"));

            Assert.Equal("{\"detail\":\"x\",\"echo\":\"[redacted]\"}", error.RawBody);
            Assert.DoesNotContain("a/b", Shown(error), StringComparison.Ordinal);
            Assert.DoesNotContain("00E9", Shown(error), StringComparison.Ordinal);
            Assert.DoesNotContain("padding0123456", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionKeepsErrorCodesAndShortAssertionStringsReadable()
        {
            const string credential = "Y3JlZGVudGlhbC1pZC0xMjM0NTY3ODkw";
            var error = await ConsentFailureAsync(
                AssertionWith(credential, "s"),
                StubHttpHandler.Json("{\"error\":\"invalid_grant\",\"detail\":\"id public-key " + credential + "\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"error\":\"invalid_grant\",\"detail\":\"[redacted]\"}", error.RawBody);
            Assert.Contains("invalid_grant", Shown(error), StringComparison.Ordinal);
            Assert.DoesNotContain(credential, Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionCoversAStringInsideANestedExtensionObject()
        {
            const string nested = "nested-credential-string-0123456789";
            var assertion = AssertionWith("id", "s");
            assertion.ClientExtensionResults = JsonDocument.Parse("{\"credProps\":{\"deep\":[{\"value\":\"" + nested + "\"}]}}").RootElement.Clone();
            var error = await ConsentFailureAsync(
                assertion,
                StubHttpHandler.Json("{\"error\":\"invalid_grant\",\"echo\":\"" + nested + "\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"error\":\"invalid_grant\",\"echo\":\"[redacted]\"}", error.RawBody);
            Assert.DoesNotContain(nested, Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionTreatsTrailingContentAsNotJson()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("x", "s"),
                Raw("{\"detail\":\"safe\"}]", HttpStatusCode.BadRequest, "application/json"));

            Assert.Equal("[redacted]", error.RawBody);
            Assert.Null(error.Body);
        }

        [Fact]
        public async Task ConsentRedactionTurnsAnEmptyBodyIntoRedacted()
        {
            var error = await ConsentFailureAsync(AssertionWith("x", "s"), Raw(string.Empty, HttpStatusCode.BadRequest, "text/plain"));

            Assert.Equal("[redacted]", error.RawBody);
        }

        [Fact]
        public async Task ConsentRedactionKeepsASafeKeyNamedRedactedAfterASecretKey()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("SECRET-0123456789abcdef", "s"),
                StubHttpHandler.Json("{\"a-SECRET-0123456789abcdef\":\"x\",\"[redacted]\":\"safe\"}", HttpStatusCode.BadRequest));

            Assert.Equal("{\"[redacted]2\":\"x\",\"[redacted]\":\"safe\"}", error.RawBody);
        }

        [Fact]
        public async Task ConsentRedactionCoversTheReasonPhraseAndTheRequestId()
        {
            const string credential = "credential-string-0123456789";
            var reply = StubHttpHandler.Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
            reply.ReasonPhrase = "bad " + credential;
            reply.Headers.TryAddWithoutValidation("X-Request-ID", "req-" + credential);
            var error = await ConsentFailureAsync(AssertionWith(credential, "s"), reply);

            Assert.DoesNotContain(credential, error.Message, StringComparison.Ordinal);
            Assert.Equal("[redacted]", error.RequestId);
            Assert.DoesNotContain(credential, string.Join("|", error.Headers.SelectMany(h => h.Value.Append(h.Key))), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentTransportFailureKeepsNoUnderlyingException()
        {
            const string credential = "cred\"ential\\string-0123456789";
            _issuer.Runtime = _ => throw new HttpRequestException("write failed: {\"id\":\"cred\\\"ential\\\\string-0123456789\"}");
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<ApiConnectionException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve, AssertionWith(credential, "s")));

            var text = error.ToString();
            Assert.DoesNotContain("ential", text, StringComparison.Ordinal);
            Assert.DoesNotContain("write failed", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionDropsAHeaderNamedAfterASecret()
        {
            const string credential = "credential-string-0123456789";
            var reply = StubHttpHandler.Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
            reply.Headers.TryAddWithoutValidation("X-" + credential, credential);
            reply.Headers.TryAddWithoutValidation("X-Keep", "fine");
            var error = await ConsentFailureAsync(AssertionWith(credential, "s"), reply);

            Assert.Contains(error.Headers, h => h.Key == "X-Keep");
            Assert.DoesNotContain(credential, string.Join("|", error.Headers.SelectMany(h => h.Value.Append(h.Key))), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentMalformed2xxKeepsNothingOfTheSecret()
        {
            const string credential = "credential-string-0123456789";
            var reply = Raw("not json " + credential, HttpStatusCode.OK, "text/html");
            reply.Headers.TryAddWithoutValidation("X-Request-Id", "req-" + credential);
            _issuer.Runtime = _ => reply;
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAnyAsync<Exception>(
                () => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve, AssertionWith(credential, "s")));

            Assert.DoesNotContain(credential, error.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionTreatsAVeryDeeplyNestedBodyAsNotJson()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("x", "s"),
                Raw(new string('[', 10000) + new string(']', 10000), HttpStatusCode.BadRequest, "application/json"));

            Assert.Equal("[redacted]", error.RawBody);
        }

        [Fact]
        public async Task ConsentRedactionReplacesANonJsonBody()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("x", "s"),
                Raw("<html>Bad gateway agent-request-secret</html>", HttpStatusCode.BadRequest, "text/html"));

            Assert.Equal("[redacted]", error.RawBody);
            Assert.Null(error.Body);
            Assert.DoesNotContain("agent-request-secret", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConsentRedactionCoversASecretPastTheMessageCap()
        {
            var error = await ConsentFailureAsync(
                AssertionWith("x", "s"),
                Raw(new string('x', 600) + "agent-request-secret", HttpStatusCode.BadRequest, "text/plain"));

            Assert.Equal("[redacted]", error.RawBody);
            Assert.DoesNotContain("agent-request-secret", Shown(error), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AssertionSecretsAreReadFromTheBytesTheRequestSent()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"code\":\"c\",\"redirect_uri\":\"https://a.example/cb\",\"state\":\"s\"}");
            using var auth = _issuer.Create();
            await auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve, MarkedAssertion());

            var secrets = AuthAgentPasskeyAssertion.SecretsOf(System.Text.Encoding.UTF8.GetBytes(Runtime().Single().Body!));

            Assert.All(secrets, secret => Assert.True(secret!.Length >= 16));
            Assert.Contains(ConsentMarkers[4], secrets);
            Assert.DoesNotContain("id", secrets);
        }

        [Fact]
        public async Task AgentConsentWithAnUndecodableExtensionStringFailsLocallyBeforeAnythingIsSent()
        {
            var assertion = MarkedAssertion();
            assertion.ClientExtensionResults = JsonDocument.Parse("{\"mk-key-x-padpad0123456\":[\"\\ud800\"]}").RootElement.Clone();
            var sent = 0;
            _issuer.Runtime = _ =>
            {
                sent++;
                return StubHttpHandler.Json("upstream said: \\ud800 end", HttpStatusCode.BadGateway);
            };
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, ConsentMarkers[9], AuthAgentConsentDecision.Approve, assertion));

            Assert.Equal(0, sent);
            Assert.DoesNotContain("ud800", error.Message);
        }

        [Fact]
        public async Task AgentConsentNonJsonErrorBodyIsRedactedToo()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("upstream said: " + string.Join(" ", ConsentMarkers), HttpStatusCode.BadGateway);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<InternalServerException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, ConsentMarkers[9], AuthAgentConsentDecision.Approve, MarkedAssertion()));

            AssertNoConsentSecrets(error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AgentConsentMalformedSuccessNeverCarriesAnySecret(bool wellFormedButWrongShape)
        {
            var echo = string.Join(" ", ConsentMarkers);
            _issuer.Runtime = _ => StubHttpHandler.Json(wellFormedButWrongShape ? "[\"" + echo.Replace("\"", "\\\"") + "\"]" : "{\"detail\":\"" + echo.Replace("\"", "\\\"") + "\"");
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<DiditException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, ConsentMarkers[9], AuthAgentConsentDecision.Approve, MarkedAssertion()));

            AssertNoConsentSecrets(error, AccessToken);
        }

        [Fact]
        public async Task AgentConsentPasskeyRequiredIsPermissionDenied()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"error\":\"passkey_required\"}", HttpStatusCode.Forbidden);
            using var auth = _issuer.Create();

            var error = await Assert.ThrowsAsync<PermissionDeniedException>(
                () => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Approve));

            Assert.Equal(403, error.StatusCode);
            Assert.Equal("passkey_required", error.Body!.Value.GetProperty("error").GetString());
        }

        [Fact]
        public async Task AgentApprovalOptionsErrorsMapByStatus()
        {
            _issuer.Runtime = _ => StubHttpHandler.Json("{\"detail\":\"Invalid token.\"}", HttpStatusCode.Unauthorized);
            using var auth = _issuer.Create();

            await Assert.ThrowsAsync<AuthenticationException>(() => auth.CreateAgentApprovalOptionsAsync(AccessToken, "agent-request-secret"));
        }

        [Fact]
        public async Task AgentPostsAreNotRetriedOnConnectionErrors()
        {
            var calls = 0;
            using var handler = new StubHttpHandler(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new HttpRequestException("connection reset agent-request-secret");
            });
            using var client = new HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            var error = await Assert.ThrowsAsync<ApiConnectionException>(() => auth.CreateAgentApprovalOptionsAsync(AccessToken, "agent-request-secret"));
            await Assert.ThrowsAsync<ApiConnectionException>(() => auth.SubmitAgentConsentAsync(AccessToken, "agent-request-secret", AuthAgentConsentDecision.Deny));

            Assert.Equal(2, calls);
            Assert.DoesNotContain("agent-request-secret", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PostIsNotRetriedOnConnectionErrors()
        {
            var calls = 0;
            using var handler = new StubHttpHandler(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new HttpRequestException("connection reset rt_secret");
            });
            using var client = new HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            var error = await Assert.ThrowsAsync<ApiConnectionException>(() => auth.CreateStepUpAsync("rt_secret", "bank", returnTo: "https://app.example.com/bank"));

            Assert.IsNotType<ApiTimeoutException>(error);
            Assert.Equal(1, calls);
            Assert.DoesNotContain("rt_secret", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetIsRetriedOnceOnConnectionErrors()
        {
            var calls = 0;
            using var handler = new StubHttpHandler(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new HttpRequestException("connection refused");
            });
            using var client = new HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            await Assert.ThrowsAsync<ApiConnectionException>(() => auth.GetSessionAsync(AccessToken));

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task SlowIssuerTimesOut()
        {
            using var handler = new DelayHandler();
            using var client = new HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client, Timeout = TimeSpan.FromMilliseconds(50) });

            await Assert.ThrowsAsync<ApiTimeoutException>(() => auth.GetDiscoveryDocumentAsync());
        }

        [Fact]
        public async Task CallerCancellationIsNotATimeout()
        {
            using var handler = new DelayHandler();
            using var client = new HttpClient(handler);
            using var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => auth.GetSessionAsync(AccessToken, cancel.Token));
        }

        [Fact]
        public async Task ArgumentsAreValidated()
        {
            using var auth = _issuer.Create();

            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.ExchangeCodeAsync(null!, "v", "r"));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.ExchangeCodeAsync("c", string.Empty, "r"));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.RefreshAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.GetSessionAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.RevokeSessionAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.CreateStepUpAsync(AccessToken, null!));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.RetrieveStepUpAsync(AccessToken, string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => auth.SubmitAgentConsentAsync(AccessToken, string.Empty, "approve"));
            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.CreateAgentApprovalOptionsAsync(AccessToken, null!));
            Assert.Empty(_issuer.Handler.Requests);
        }

        [Fact]
        public void DisposeLeavesASuppliedHttpClientAlone()
        {
            using var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}"));
            using var client = new HttpClient(handler);
            var auth = new Didit.Authentication(TestIssuer.Url, new AuthenticationOptions { HttpClient = client });

            auth.Dispose();

            Assert.Equal(TimeSpan.FromSeconds(100), client.Timeout);
            client.Timeout = TimeSpan.FromSeconds(5);
        }

        private static void AssertSdkHeaders(RecordedRequest request)
        {
            Assert.StartsWith("didit-dotnet/", request.Headers["User-Agent"], StringComparison.Ordinal);
            Assert.StartsWith("dotnet/", request.Headers["X-Didit-Client"], StringComparison.Ordinal);
            Assert.Equal("application/json", request.Headers["Accept"]);
            Assert.False(request.Headers.ContainsKey("x-api-key"));
        }

        private static Dictionary<string, string> Form(string body) =>
            body.Split('&').Select(pair => pair.Split('=')).ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));

        private List<RecordedRequest> Runtime() =>
            _issuer.Handler.Requests.Where(r => !r.Uri.AbsolutePath.StartsWith("/.well-known/", StringComparison.Ordinal)).ToList();

        private sealed class DelayHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }
    }
}
