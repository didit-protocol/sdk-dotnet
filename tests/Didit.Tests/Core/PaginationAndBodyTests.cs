using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Didit.Tests.Support;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class PaginationAndBodyTests
    {
        private static string PageJson(int count, string? next, params string[] ids) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["count"] = count,
                ["next"] = next,
                ["previous"] = null,
                ["results"] = ids.Select(id => new Dictionary<string, object> { ["session_id"] = id }).ToList(),
            });

        [Fact]
        public async Task Await_foreach_walks_every_page_following_next()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, PageJson(5, "https://verification.didit.me/v3/sessions/?limit=2&offset=2", "a", "b"));
            handler.Enqueue(200, PageJson(5, "https://verification.didit.me/v3/sessions/?limit=2&offset=4", "c", "d"));
            handler.Enqueue(200, PageJson(5, null, "e"));
            using var client = TestClients.Create(handler);

            var page = await client.Sessions.ListAsync(new SessionsListParams { Limit = 2 });
            var ids = new List<string?>();
            await foreach (var session in page)
            {
                ids.Add(session.SessionId);
            }

            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, ids);
            Assert.Equal(5, page.Count);
            Assert.True(page.HasNextPage);
            Assert.Equal("?limit=2", handler.Requests[0].Query);
            Assert.Equal("?limit=2&offset=4", handler.Requests[2].Query);
            Assert.All(handler.Requests, r => Assert.Equal(TestClients.ApiKey, r.Header("x-api-key")));
        }

        [Fact]
        public async Task Pages_can_be_walked_one_by_one()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, PageJson(3, "/v3/sessions/?offset=2", "a", "b"));
            handler.Enqueue(200, PageJson(3, null, "c"));
            using var client = TestClients.Create(handler);

            var first = await client.Sessions.ListAsync();
            var second = await first.GetNextPageAsync();
            Assert.NotNull(second);
            Assert.Null(await second!.GetNextPageAsync());
            Assert.Equal("https://verification.didit.me/v3/sessions/?offset=2", handler.Requests[1].Uri.ToString());

            var handler2 = new MockHttpHandler();
            handler2.Enqueue(200, PageJson(3, "/v3/sessions/?offset=2", "a", "b"));
            handler2.Enqueue(200, PageJson(3, null, "c"));
            using var client2 = TestClients.Create(handler2);
            var counts = new List<int>();
            await foreach (var p in (await client2.Sessions.ListAsync()).AsPagesAsync())
            {
                counts.Add(p.Results.Count);
            }

            Assert.Equal(new[] { 2, 1 }, counts);
        }

        [Fact]
        public async Task Next_url_on_another_origin_is_refused()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, PageJson(4, "https://attacker.example.com/v3/sessions/?offset=2", "a", "b"));
            using var client = TestClients.Create(handler);

            var page = await client.Sessions.ListAsync();
            var error = await Assert.ThrowsAsync<DiditException>(() => page.GetNextPageAsync());
            Assert.Contains("another origin", error.Message);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task Next_url_reported_over_http_by_a_proxy_is_upgraded_to_the_configured_origin()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, PageJson(3, "http://verification.didit.me/v3/sessions/?offset=2", "a", "b"));
            handler.Enqueue(200, PageJson(3, null, "c"));
            using var client = TestClients.Create(handler);

            var page = await client.Sessions.ListAsync();
            await page.GetNextPageAsync();
            Assert.Equal("https://verification.didit.me/v3/sessions/?offset=2", handler.Requests[1].Uri.ToString());
        }

        [Fact]
        public async Task Extra_list_fields_are_exposed()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[],\"total_transactions\":12}");
            using var client = TestClients.Create(handler);

            var page = await client.Transactions.ListAsync();
            Assert.Equal(12, page.AdditionalProperties["total_transactions"].GetInt32());
            Assert.False(page.HasNextPage);
        }

        [Fact]
        public async Task Page_built_by_hand_cannot_fetch_more()
        {
            var page = new Page<string>(new[] { "x" }, 2, "https://verification.didit.me/next");
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetNextPageAsync());
        }

        [Fact]
        public async Task Multipart_upload_sends_files_and_fields_and_replays_them_on_retry()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(429, "{}");
            handler.Enqueue(200, "{\"request_id\":\"r-1\"}");
            using var client = TestClients.Create(handler);
            using var stream = new NonSeekableStream(new byte[] { 0xFF, 0xD8, 0xFF });
            var path = Path.Combine(Path.GetTempPath(), "didit-test-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            try
            {
                await client.IdVerification.VerifyAsync(new IdVerificationVerifyParams
                {
                    FrontImage = FileUpload.FromStream(stream, "front.jpg"),
                    BackImage = FileUpload.FromPath(path),
                    VendorData = "user-1",
                    PerformDocumentLiveness = true,
                    MinimumAge = 18,
                    Metadata = new Dictionary<string, object> { ["flow"] = "onboarding" },
                });
            }
            finally
            {
                File.Delete(path);
            }

            Assert.Equal(2, handler.Requests.Count);
            foreach (var request in handler.Requests)
            {
                var body = Encoding.UTF8.GetString(request.Body);
                Assert.StartsWith("multipart/form-data", request.ContentType);
                Assert.Contains("name=\"front_image\"; filename=\"front.jpg\"", body);
                Assert.Contains("Content-Type: image/jpeg", body);
                Assert.Contains("Content-Type: image/png", body);
                Assert.Contains("filename=\"" + Path.GetFileName(path) + "\"", body);
                Assert.Contains("name=\"vendor_data\"\r\n\r\nuser-1", body.Replace("Content-Type: text/plain; charset=utf-8\r\n", string.Empty));
                Assert.Contains("true", body);
                Assert.Contains("{\"flow\":\"onboarding\"}", body);
            }
        }

        [Fact]
        public void File_uploads_guess_content_types()
        {
            Assert.Equal("image/jpeg", FileUpload.FromBytes(new byte[1], "a.JPG").ContentType);
            Assert.Equal("application/pdf", FileUpload.FromBytes(new byte[1], "a.pdf").ContentType);
            Assert.Equal("application/octet-stream", FileUpload.FromBytes(new byte[1], "a.unknown").ContentType);
            Assert.Equal("video/mp4", FileUpload.FromBytes(new byte[1], "a.bin", "video/mp4").ContentType);
            FileUpload implicitUpload = new byte[] { 1 };
            Assert.Equal("file", implicitUpload.FileName);
            Assert.Throws<ArgumentException>(() => FileUpload.FromPath(""));
            Assert.Throws<ArgumentNullException>(() => FileUpload.FromBytes(null!));
            Assert.Throws<ArgumentNullException>(() => FileUpload.FromStream(null!));
        }

        [Fact]
        public async Task Binary_responses_return_bytes_content_type_and_file_name()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, new byte[] { 1, 2, 3 }, "application/pdf", new Dictionary<string, string> { ["Content-Disposition"] = "attachment; filename*=UTF-8''session%20report.pdf" });
            using var client = TestClients.Create(handler);

            var pdf = await client.Sessions.GeneratePdfAsync("s-1");

            Assert.Equal(new byte[] { 1, 2, 3 }, pdf.Content);
            Assert.Equal("application/pdf", pdf.ContentType);
            Assert.Equal("session report.pdf", pdf.FileName);
            using var read = pdf.OpenRead();
            Assert.Equal(3, read.Length);
            var target = Path.Combine(Path.GetTempPath(), "didit-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                await pdf.SaveToAsync(target);
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
            }
            finally
            {
                File.Delete(target);
            }

            Assert.Equal("a.pdf", BinaryResponse.ParseFileName("attachment; filename=\"a.pdf\""));
            Assert.Null(BinaryResponse.ParseFileName(null));
        }

        [Fact]
        public async Task Raw_responses_expose_status_headers_request_id_and_data()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(201, "{\"session_id\":\"s-1\"}", headers: new Dictionary<string, string> { ["X-Request-Id"] = "req_1", ["X-RateLimit-Remaining"] = "299" });
            handler.Enqueue(204);
            using var client = TestClients.Create(handler);

            var response = await client.WithRawResponse.Sessions.CreateAsync(new SessionsCreateParams { WorkflowId = "wf" });
            Assert.Equal(201, response.StatusCode);
            Assert.Equal("req_1", response.RequestId);
            Assert.Equal("299", response.GetHeader("x-ratelimit-remaining"));
            Assert.Equal("s-1", response.Data.SessionId);
            Assert.Equal("{\"session_id\":\"s-1\"}", Encoding.UTF8.GetString(response.Body));

            var deleted = await client.WithRawResponse.Webhooks.Destinations.DeleteAsync("d-1");
            Assert.Equal(204, deleted.StatusCode);
            Assert.Null(deleted.GetHeader("missing"));
        }

        private sealed class NonSeekableStream : MemoryStream
        {
            public NonSeekableStream(byte[] buffer)
                : base(buffer)
            {
            }

            public override bool CanSeek => false;
        }
    }
}
