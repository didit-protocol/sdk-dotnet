using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Didit.Internal;
using Didit.Tests.Support;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class ErrorAndRetryTests
    {
        private const string Page = "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}";
        private const string SessionJson = "{\"session_id\":\"s-1\"}";

        public static IEnumerable<object[]> StatusCases() => new[]
        {
            new object[] { 400, typeof(BadRequestException) },
            new object[] { 401, typeof(AuthenticationException) },
            new object[] { 403, typeof(PermissionDeniedException) },
            new object[] { 404, typeof(NotFoundException) },
            new object[] { 409, typeof(ConflictException) },
            new object[] { 422, typeof(UnprocessableEntityException) },
            new object[] { 429, typeof(RateLimitException) },
            new object[] { 500, typeof(InternalServerException) },
            new object[] { 503, typeof(InternalServerException) },
            new object[] { 418, typeof(ApiStatusException) },
        };

        [Theory]
        [MemberData(nameof(StatusCases))]
        public async Task Maps_each_status_to_its_exception(int status, Type expected)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(status, "{\"detail\":\"Something is wrong.\"}", headers: new Dictionary<string, string> { ["X-Request-Id"] = "req_9" });
            using var client = TestClients.Create(handler, o => o.MaxRetries = 0);

            var error = await Assert.ThrowsAnyAsync<ApiStatusException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));

            Assert.Equal(expected, error.GetType());
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("req_9", error.RequestId);
            Assert.Equal("Something is wrong.", error.Body!.Value.GetProperty("detail").GetString());
            Assert.Contains("Something is wrong.", error.Message);
            Assert.Contains("req_9", error.Message);
            Assert.Equal("req_9", error.Headers["x-request-id"][0]);
            Assert.IsAssignableFrom<DiditException>(error);
        }

        [Fact]
        public async Task Non_json_error_body_is_kept_as_text_and_the_api_key_is_redacted()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(400, "bad key " + TestClients.ApiKey, "text/plain");
            using var client = TestClients.Create(handler);

            var error = await Assert.ThrowsAsync<BadRequestException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));

            Assert.Null(error.Body);
            Assert.DoesNotContain(TestClients.ApiKey, error.Message);
            Assert.DoesNotContain(TestClients.ApiKey, error.RawBody);
            Assert.Contains("[redacted]", error.RawBody);
        }

        [Fact]
        public async Task Field_errors_are_described_from_the_json_body()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(400, "{\"workflow_id\":[\"This field is required.\"]}");
            using var client = TestClients.Create(handler);

            var error = await Assert.ThrowsAsync<BadRequestException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Contains("workflow_id", error.Message);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(408)]
        [InlineData(429)]
        public async Task Get_is_retried_on_retryable_statuses(int status)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(status, "{}");
            handler.Enqueue(200, Page);
            using var client = TestClients.Create(handler);

            var page = await client.Sessions.ListAsync();

            Assert.Equal(2, handler.Requests.Count);
            Assert.Empty(page.Results);
        }

        [Fact]
        public async Task Retries_stop_after_max_retries()
        {
            var handler = new MockHttpHandler();
            for (var i = 0; i < 5; i++)
            {
                handler.Enqueue(503, "{}");
            }

            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<InternalServerException>(() => client.Sessions.ListAsync());
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task Max_retries_can_be_overridden_per_request()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(503, "{}");
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<InternalServerException>(() => client.Sessions.ListAsync(requestOptions: new RequestOptions { MaxRetries = 0 }));
            Assert.Single(handler.Requests);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(503)]
        [InlineData(408)]
        public async Task Post_is_never_retried_once_the_request_may_have_reached_the_server(int status)
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(status, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAnyAsync<ApiStatusException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Patch_is_not_retried_on_5xx()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(502, "{}");
            using var client = TestClients.Create(handler);

            await Assert.ThrowsAsync<InternalServerException>(() =>
                client.Sessions.UpdateStatusAsync("s-1", new SessionsUpdateStatusParams { NewStatus = "Approved" }));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Post_is_retried_on_429()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(429, "{}");
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            var session = await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });

            Assert.Equal("s-1", session.SessionId);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(handler.Requests[0].BodyText, handler.Requests[1].BodyText);
        }

        [Fact]
        public async Task Post_is_retried_when_the_connection_failed_before_sending()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueException(new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            await client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task Post_is_not_retried_after_a_connection_error_that_may_follow_sending()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueException(new HttpRequestException("connection reset"));
            handler.Enqueue(201, SessionJson);
            using var client = TestClients.Create(handler);

            var error = await Assert.ThrowsAsync<ApiConnectionException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Single(handler.Requests);
            Assert.IsType<HttpRequestException>(error.InnerException);
        }

        [Fact]
        public async Task Get_is_retried_after_a_connection_error()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueException(new HttpRequestException("connection reset"));
            handler.Enqueue(200, Page);
            using var client = TestClients.Create(handler);

            await client.Sessions.ListAsync();
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task Timeouts_raise_ApiTimeoutException_and_are_retried_for_get_only()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueHang();
            handler.Enqueue(200, Page);
            handler.EnqueueHang();
            using var client = TestClients.Create(handler, o => o.Timeout = TimeSpan.FromMilliseconds(50));

            await client.Sessions.ListAsync();
            Assert.Equal(2, handler.Requests.Count);

            await Assert.ThrowsAsync<ApiTimeoutException>(() => client.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" }));
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task Caller_cancellation_is_not_turned_into_a_timeout()
        {
            var handler = new MockHttpHandler();
            handler.EnqueueHang();
            using var client = TestClients.Create(handler);
            using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Sessions.ListAsync(cancellationToken: source.Token));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Retry_after_seconds_is_honoured_and_capped()
        {
            var delays = new List<TimeSpan>();
            var handler = new MockHttpHandler();
            handler.Enqueue(429, "{}", headers: new Dictionary<string, string> { ["Retry-After"] = "3" });
            handler.Enqueue(429, "{}", headers: new Dictionary<string, string> { ["Retry-After"] = "600" });
            handler.Enqueue(200, Page);
            using var client = TestClients.Create(handler);
            client.Pipeline.Delay = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            };

            await client.Sessions.ListAsync();

            Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60) }, delays);
        }

        [Fact]
        public void Retry_after_http_date_and_backoff_with_full_jitter()
        {
            var pipeline = new HttpPipeline(new DiditClientOptions(), new HttpClient(new MockHttpHandler()));
            var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
            pipeline.UtcNow = () => now;
            pipeline.Jitter = () => 1.0;

            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Retry-After"] = new[] { now.AddSeconds(5).ToString("r") },
            };
            Assert.Equal(TimeSpan.FromSeconds(5), pipeline.RetryDelay(0, headers));
            headers["Retry-After"] = new[] { now.AddSeconds(-5).ToString("r") };
            Assert.Equal(TimeSpan.Zero, pipeline.RetryDelay(0, headers));

            var none = new Dictionary<string, IReadOnlyList<string>>();
            Assert.Equal(TimeSpan.FromSeconds(0.5), pipeline.RetryDelay(0, none));
            Assert.Equal(TimeSpan.FromSeconds(1), pipeline.Backoff(1));
            Assert.Equal(TimeSpan.FromSeconds(2), pipeline.Backoff(2));
            Assert.Equal(TimeSpan.FromSeconds(8), pipeline.Backoff(4));
            Assert.Equal(TimeSpan.FromSeconds(8), pipeline.Backoff(10));
            pipeline.Jitter = () => 0.25;
            Assert.Equal(TimeSpan.FromSeconds(0.5), pipeline.Backoff(2));
        }

        [Fact]
        public void Retry_policy_by_method()
        {
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethod.Get, 500));
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethod.Put, 408));
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethod.Delete, 503));
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethod.Head, 429));
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethod.Post, 429));
            Assert.True(HttpPipeline.ShouldRetryStatus(HttpMethods.Patch, 429));
            Assert.False(HttpPipeline.ShouldRetryStatus(HttpMethod.Post, 500));
            Assert.False(HttpPipeline.ShouldRetryStatus(HttpMethods.Patch, 503));
            Assert.False(HttpPipeline.ShouldRetryStatus(HttpMethod.Get, 404));
            Assert.False(HttpPipeline.ShouldRetryStatus(HttpMethod.Get, 400));
        }

        [Fact]
        public void Classifies_connection_failures_before_sending()
        {
            Assert.True(HttpPipeline.FailedBeforeSending(new HttpRequestException("x", new SocketException((int)SocketError.HostNotFound))));
            Assert.True(HttpPipeline.FailedBeforeSending(new HttpRequestException("x", new SocketException((int)SocketError.ConnectionRefused))));
            Assert.False(HttpPipeline.FailedBeforeSending(new HttpRequestException("x", new SocketException((int)SocketError.ConnectionReset))));
            Assert.False(HttpPipeline.FailedBeforeSending(new HttpRequestException("x")));
        }

        [Fact]
        public async Task Redirects_are_not_followed_and_surface_as_errors()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(302, "", headers: new Dictionary<string, string> { ["Location"] = "https://evil.example.com/" });
            using var client = TestClients.Create(handler);

            var error = await Assert.ThrowsAsync<ApiStatusException>(() => client.Sessions.ListAsync());
            Assert.Equal(302, error.StatusCode);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Unparseable_success_body_raises_a_DiditException()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "not json", headers: new Dictionary<string, string> { ["X-Request-Id"] = "req_x" });
            using var client = TestClients.Create(handler);

            var error = await Assert.ThrowsAsync<DiditException>(() => client.Sessions.RetrieveDecisionAsync("s-1"));
            Assert.Contains("req_x", error.Message);
        }
    }
}
