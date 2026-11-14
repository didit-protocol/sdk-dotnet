using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Didit.Internal;
using Didit.Tests.Support;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class IdempotencyKeyTests
    {
        private const string SessionJson = "{\"session_id\":\"s-1\"}";
        private const string InProgress = "{\"detail\":\"Still running.\",\"code\":\"idempotency_key_in_progress\"}";
        private const string Reused = "{\"detail\":\"Key reused.\",\"code\":\"idempotency_key_reused\"}";

        private static Task<DiditResponse> Send(DiditClient client, HttpMethod method, string key) =>
            client.Pipeline.SendAsync(new ApiRequest(method, "/v3/probe/"), new RequestOptions { IdempotencyKey = key }, CancellationToken.None);

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task The_header_is_sent_on_write_methods(string method)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{}");
            using var client = TestClients.Create(handler);

            await Send(client, new HttpMethod(method), "order-123");

            Assert.Equal("order-123", Assert.Single(handler.Requests).Header("Idempotency-Key"));
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        public async Task The_header_is_left_out_of_get_and_head(string method)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{}");
            using var client = TestClients.Create(handler);

            await Send(client, new HttpMethod(method), "order-123");

            Assert.Null(Assert.Single(handler.Requests).Header("Idempotency-Key"));
        }

        [Fact]
        public void The_client_options_have_no_idempotency_key()
        {
            Assert.Null(typeof(DiditClientOptions).GetProperty("IdempotencyKey"));
            Assert.NotNull(typeof(RequestOptions).GetProperty("IdempotencyKey"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(256)]
        public async Task An_invalid_key_fails_before_sending(int length)
        {
            var handler = new MockHttpHandler();
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<ConfigurationException>(() =>
                client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = new string('k', length) }));

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task A_key_of_255_characters_is_accepted()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = new string('k', 255) });

            Assert.Equal(255, handler.Requests[0].Header("Idempotency-Key")!.Length);
        }

        [Fact]
        public async Task No_key_is_ever_generated()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });

            Assert.Null(handler.Requests[0].Header("Idempotency-Key"));
        }

        [Theory]
        [InlineData(500)]
        [InlineData(503)]
        [InlineData(408)]
        public async Task Post_with_a_key_is_retried_on_5xx_with_the_same_key(int status)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(status, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = "order-123" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("order-123", r.Header("Idempotency-Key")));
        }

        [Fact]
        public async Task Post_with_a_key_is_retried_on_a_read_timeout()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueHang();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(
                new SessionsCreateParams { WorkflowId = "wf" },
                new RequestOptions { IdempotencyKey = "order-123", Timeout = TimeSpan.FromMilliseconds(100) });

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("order-123", r.Header("Idempotency-Key")));
        }

        [Fact]
        public async Task Post_without_a_key_is_still_not_retried_on_5xx()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<InternalServerException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Patch_with_a_key_is_retried_on_5xx_with_the_same_key()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(502, "{}");
            handler.Enqueue(200, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.UpdateStatusAsync(
                "s-1",
                new SessionsUpdateStatusParams { NewStatus = "Approved" },
                new RequestOptions { IdempotencyKey = "order-123" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("order-123", r.Header("Idempotency-Key")));
        }

        [Fact]
        public async Task An_in_progress_conflict_is_retried_honouring_retry_after()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(409, InProgress, headers: new Dictionary<string, string> { ["Retry-After"] = "1" });
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);
            var delays = new List<TimeSpan>();
            client.Pipeline.Delay = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            };

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = "order-123" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays));
        }

        [Fact]
        public async Task An_in_progress_conflict_without_a_key_is_raised()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(409, InProgress);
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<ConflictException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Single(handler.Requests);
        }

        [Theory]
        [InlineData(Reused)]
        [InlineData("{\"detail\":\"Conflict.\"}")]
        [InlineData("not json")]
        public async Task Every_other_conflict_is_raised_at_once(string body)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(409, body);
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<ConflictException>(() =>
                client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = "order-123" }));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task A_non_allow_listed_method_sends_no_header()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{}");
            using var client = TestClients.Create(handler);

            await Send(client, HttpMethod.Options, "order-123");

            Assert.Null(Assert.Single(handler.Requests).Header("Idempotency-Key"));
        }

        [Fact]
        public async Task A_client_default_header_is_ignored_and_never_counts()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler, o => o.DefaultHeaders["idempotency-key"] = "order-default");

            await Assert.ThrowsAsync<InternalServerException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));

            Assert.Single(handler.Requests);
            Assert.Null(handler.Requests[0].Header("Idempotency-Key"));
        }

        [Fact]
        public async Task The_option_still_works_on_a_client_with_a_default_header()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler, o => o.DefaultHeaders["Idempotency-Key"] = "order-default");

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = "order-123" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("order-123", r.Header("Idempotency-Key")));
        }

        [Fact]
        public async Task A_lower_case_raw_header_is_sent_once_and_makes_a_post_retryable()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(
                new SessionsCreateParams { WorkflowId = "wf" },
                new RequestOptions { Headers = new Dictionary<string, string> { ["idempotency-key"] = "order-123" } });

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("order-123", r.Header("Idempotency-Key")));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(256)]
        public async Task An_invalid_raw_header_value_fails_before_sending(int length)
        {
            var handler = new MockHttpHandler();
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<ConfigurationException>(() =>
                client.Sessions.CreateAsync(
                    new SessionsCreateParams { WorkflowId = "wf" },
                    new RequestOptions { Headers = new Dictionary<string, string> { ["Idempotency-Key"] = new string('k', length) } }));

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task A_raw_header_on_a_get_is_dropped()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{}");
            using var client = TestClients.Create(handler);

            await client.Pipeline.SendAsync(
                new ApiRequest(HttpMethod.Get, "/v3/probe/"),
                new RequestOptions { Headers = new Dictionary<string, string> { ["Idempotency-Key"] = "order-123" } },
                CancellationToken.None);

            Assert.Null(Assert.Single(handler.Requests).Header("Idempotency-Key"));
        }

        [Fact]
        public async Task Option_and_raw_header_together_send_one_header_with_the_option_value()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler, o => o.DefaultHeaders["Idempotency-Key"] = "order-default");

            await client.Sessions.CreateAsync(
                new SessionsCreateParams { WorkflowId = "wf" },
                new RequestOptions { IdempotencyKey = "order-opt", Headers = new Dictionary<string, string> { ["IDEMPOTENCY-KEY"] = "order-hdr" } });

            Assert.Equal("order-opt", Assert.Single(handler.Requests).Header("Idempotency-Key"));
        }

        [Fact]
        public async Task The_key_never_appears_in_debug_logs()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            handler.Enqueue(201, SessionJson);
            var lines = new List<string>();
            using var client = TestClients.Create(handler, o => o.Logger = lines.Add);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }, new RequestOptions { IdempotencyKey = "order-789" });

            Assert.NotEmpty(lines);
            Assert.DoesNotContain(lines, line => line.Contains("order-789"));
        }
    }
}
