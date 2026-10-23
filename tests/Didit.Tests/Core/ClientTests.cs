using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Didit.Tests.Support;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class ClientTests
    {
        private const string SessionJson = "{\"session_id\":\"s-1\",\"session_number\":1,\"session_token\":\"t\",\"url\":\"https://verify.didit.me/session/t\"}";

        [Fact]
        public async Task Sends_the_standard_headers()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf-1" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal(TestClients.ApiKey, request.Header("x-api-key"));
            Assert.Equal("application/json", request.Header("Accept"));
            Assert.Matches(new Regex(@"^dotnet/\d+\.\d+\.\d+"), request.Header("X-Didit-Client"));
            Assert.Matches(new Regex(@"^didit-dotnet/\d+\.\d+\.\d+\S* \(.+; .+\)$"), request.Header("User-Agent"));
            Assert.Equal("https://verification.didit.me/v3/session/", request.Uri.ToString());
        }

        [Fact]
        public async Task Default_and_request_headers_are_sent_and_request_headers_win()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler, o =>
            {
                o.DefaultHeaders["X-Team"] = "kyc";
                o.DefaultHeaders["X-Override"] = "default";
            });

            await client.Sessions.CreateAsync(
                new SessionsCreateParams { WorkflowId = "wf-1" },
                new RequestOptions { Headers = new Dictionary<string, string> { ["X-Override"] = "request" }, IdempotencyKey = "idem-1" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal("kyc", request.Header("X-Team"));
            Assert.Equal("request", request.Header("X-Override"));
            Assert.Equal("idem-1", request.Header("Idempotency-Key"));
        }

        [Fact]
        public async Task Missing_api_key_fails_before_sending()
        {
            var handler = new MockHttpHandler();
            using var client = TestClients.Create(handler, o => o.ApiKey = null);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIDIT_API_KEY")))
            {
                return;
            }

            var error = await Assert.ThrowsAsync<ConfigurationException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Contains("DIDIT_API_KEY", error.Message);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Application_scoped_operation_needs_both_ids()
        {
            var handler = new MockHttpHandler();
            using var client = TestClients.Create(handler, o => o.ApplicationId = null);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIDIT_APPLICATION_ID")))
            {
                return;
            }

            await Assert.ThrowsAsync<ConfigurationException>(() => client.Cases.ListAsync());
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Application_scope_is_filled_from_options_and_request_options()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}");
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}");
            using var client = TestClients.Create(handler);

            await client.Cases.ListAsync();
            await client.Cases.ListAsync(requestOptions: new RequestOptions { OrganizationId = "org 2", ApplicationId = "app/2" });

            Assert.Equal("/v3/organization/org-test/application/app-test/cases/", handler.Requests[0].Path);
            Assert.Equal("/v3/organization/org%202/application/app%2F2/cases/", handler.Requests[1].Uri.AbsolutePath);
        }

        [Fact]
        public async Task Account_operations_use_the_account_base_url_and_never_the_api_key()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"access_token\":\"tok\"}");
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}");
            using var client = TestClients.Create(handler);

            await client.Account.LoginAsync(new LoginRequest { Email = "a@example.com", Password = "pw" });
            await client.Account.Organizations.ListAsync();

            Assert.Equal("https://apx.didit.me/auth/v2/programmatic/login/", handler.Requests[0].Uri.ToString());
            Assert.Null(handler.Requests[0].Header("x-api-key"));
            Assert.Null(handler.Requests[0].Header("Authorization"));
            Assert.Equal("Bearer " + TestClients.AccountToken, handler.Requests[1].Header("Authorization"));
            Assert.Null(handler.Requests[1].Header("x-api-key"));
        }

        [Fact]
        public async Task An_api_key_in_default_headers_never_reaches_the_account_api()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(400, "{\"detail\":\"bad token " + TestClients.AccountToken + "\"}");
            using var client = TestClients.Create(handler, o => o.DefaultHeaders["x-api-key"] = "leaked_key");

            var error = await Assert.ThrowsAsync<BadRequestException>(() => client.Account.Organizations.ListAsync());

            Assert.Null(handler.Requests[0].Header("x-api-key"));
            Assert.DoesNotContain(TestClients.AccountToken, error.Message);
            Assert.DoesNotContain(TestClients.AccountToken, error.RawBody);
        }

        [Fact]
        public async Task Account_token_operation_without_token_fails_before_sending()
        {
            var handler = new MockHttpHandler();
            using var client = TestClients.Create(handler, o => o.AccountToken = null);

            var error = await Assert.ThrowsAsync<ConfigurationException>(() => client.Account.Organizations.ListAsync());
            Assert.Contains("AccountToken", error.Message);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Base_url_and_api_key_can_be_overridden_per_request()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(
                new SessionsCreateParams { WorkflowId = "wf" },
                new RequestOptions { BaseUrl = new Uri("http://localhost:8080/"), ApiKey = "other_key" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal("http://localhost:8080/v3/session/", request.Uri.ToString());
            Assert.Equal("other_key", request.Header("x-api-key"));
        }

        [Fact]
        public async Task WithOptions_returns_an_independent_client_sharing_the_connection_pool()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);
            var other = client.WithOptions(o => o.ApiKey = "second_key");
            other.Pipeline.Delay = (_, _) => Task.CompletedTask;

            await other.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });
            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });

            Assert.Equal("second_key", handler.Requests[0].Header("x-api-key"));
            Assert.Equal(TestClients.ApiKey, handler.Requests[1].Header("x-api-key"));
            Assert.Equal(TestClients.ApiKey, client.Options.ApiKey);
        }

        [Fact]
        public void Options_are_copied_and_validated()
        {
            var options = new DiditClientOptions { ApiKey = "k", HttpClient = new HttpClient(new MockHttpHandler()) };
            using var client = new DiditClient(options);
            options.ApiKey = "changed";
            Assert.Equal("k", client.Options.ApiKey);

            Assert.Throws<ConfigurationException>(() => new DiditClient(new DiditClientOptions { ApiKey = "k", MaxRetries = -1 }));
            Assert.Throws<ConfigurationException>(() => new DiditClient(new DiditClientOptions { ApiKey = "k", Timeout = TimeSpan.Zero }));
            Assert.Throws<ConfigurationException>(() => new DiditClient(new DiditClientOptions { ApiKey = "k", BaseUrl = new Uri("/relative", UriKind.Relative) }));
            Assert.Throws<ArgumentNullException>(() => new DiditClient((DiditClientOptions)null!));
        }

        [Fact]
        public void Api_key_constructor_and_owned_http_client_dispose_cleanly()
        {
            using var client = new DiditClient("key_from_ctor");
            Assert.Equal("key_from_ctor", client.Options.ApiKey);
            Assert.NotNull(client.Sessions);
            Assert.NotNull(client.WithRawResponse.Sessions);
        }

        [Fact]
        public async Task Path_parameters_are_escaped_and_validated()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"session_id\":\"a b/c\"}");
            using var client = TestClients.Create(handler);

            await client.Sessions.RetrieveDecisionAsync("a b/c");

            Assert.Equal("/v3/session/a%20b%2Fc/decision/", handler.Requests[0].Uri.AbsolutePath);
            await Assert.ThrowsAsync<ArgumentException>(() => client.Sessions.RetrieveDecisionAsync(""));
            await Assert.ThrowsAsync<ArgumentNullException>(() => client.Sessions.CreateAsync(null!));
        }

        [Fact]
        public async Task Debug_logger_gets_method_path_status_and_request_id_but_no_secrets_or_bodies()
        {
            var lines = new List<string>();
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson, headers: new Dictionary<string, string> { ["X-Request-Id"] = "req_123" });
            using var client = TestClients.Create(handler, o => o.Logger = lines.Add);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "secret-workflow", VendorData = "user-42" });

            var line = Assert.Single(lines);
            Assert.Contains("POST /v3/session/ -> 201", line);
            Assert.Contains("req_123", line);
            Assert.DoesNotContain(TestClients.ApiKey, line);
            Assert.DoesNotContain("user-42", line);
            Assert.DoesNotContain("secret-workflow", line);
        }
    }
}
