using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Tests.Support
{
    /// <summary>A recorded request.</summary>
    public sealed class CapturedRequest
    {
        public CapturedRequest(string method, Uri uri, Dictionary<string, string> headers, byte[] body, string? contentType)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
            Body = body;
            ContentType = contentType;
        }

        public string Method { get; }

        public Uri Uri { get; }

        public Dictionary<string, string> Headers { get; }

        public byte[] Body { get; }

        public string? ContentType { get; }

        public string Path => Uri.AbsolutePath;

        public string Query => Uri.Query;

        public string BodyText => Encoding.UTF8.GetString(Body);

        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>An HttpMessageHandler that records requests and replays queued responses. Never touches the network.</summary>
    public sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<CapturedRequest> Requests { get; } = new();

        public void Enqueue(int status, string? body = null, string contentType = "application/json", IDictionary<string, string>? headers = null) =>
            Enqueue(status, body is null ? null : Encoding.UTF8.GetBytes(body), contentType, headers);

        public void Enqueue(int status, byte[]? body, string contentType, IDictionary<string, string>? headers = null)
        {
            _responses.Enqueue((_, _) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)status);
                if (body is not null)
                {
                    var content = new ByteArrayContent(body);
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    response.Content = content;
                }
                else
                {
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                }

                if (headers is not null)
                {
                    foreach (var header in headers)
                    {
                        if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        {
                            response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                }

                return Task.FromResult(response);
            });
        }

        public void EnqueueException(Exception exception) =>
            _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

        /// <summary>A response that never completes until the request is cancelled (simulates a timeout).</summary>
        public void EnqueueHang() =>
            _responses.Enqueue(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                throw new InvalidOperationException("unreachable");
            });

        public int Pending => _responses.Count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                var separator = string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ",";
                headers[header.Key] = string.Join(separator, header.Value);
            }

            byte[] body = Array.Empty<byte>();
            string? contentType = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                contentType = request.Content.Headers.ContentType?.ToString();
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }
            }

            Requests.Add(new CapturedRequest(request.Method.Method, request.RequestUri!, headers, body, contentType));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}.");
            }

            return await _responses.Dequeue()(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clients wired to a <see cref="MockHttpHandler"/>.</summary>
    public static class TestClients
    {
        public const string ApiKey = "test-api-key";
        public const string OrganizationId = "org-test";
        public const string ApplicationId = "app-test";
        public const string AccountToken = "test_account_token";

        public static DiditClient Create(MockHttpHandler handler, Action<DiditClientOptions>? configure = null)
        {
            var options = new DiditClientOptions
            {
                ApiKey = ApiKey,
                OrganizationId = OrganizationId,
                ApplicationId = ApplicationId,
                AccountToken = AccountToken,
                HttpClient = new HttpClient(handler),
            };
            configure?.Invoke(options);
            var client = new DiditClient(options);
            client.Pipeline.Delay = (_, _) => Task.CompletedTask;
            return client;
        }
    }

    /// <summary>Structural JSON comparison: key order is ignored, numbers compare by value.</summary>
    public static class JsonAssert
    {
        public static void Equivalent(string expected, string actual)
        {
            using var expectedDocument = JsonDocument.Parse(expected);
            using var actualDocument = JsonDocument.Parse(actual);
            var difference = Compare(expectedDocument.RootElement, actualDocument.RootElement, "$");
            if (difference is not null)
            {
                throw new Xunit.Sdk.XunitException($"JSON differs at {difference}.\nExpected: {expected}\nActual:   {actual}");
            }
        }

        private static string? Compare(JsonElement expected, JsonElement actual, string path)
        {
            if (expected.ValueKind != actual.ValueKind)
            {
                return $"{path} (kind {expected.ValueKind} vs {actual.ValueKind})";
            }

            switch (expected.ValueKind)
            {
                case JsonValueKind.Object:
                    var expectedProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                    var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                    foreach (var name in expectedProps.Keys.Union(actualProps.Keys))
                    {
                        if (!expectedProps.TryGetValue(name, out var e) || !actualProps.TryGetValue(name, out var a))
                        {
                            return $"{path}.{name} (missing on one side)";
                        }

                        var inner = Compare(e, a, $"{path}.{name}");
                        if (inner is not null)
                        {
                            return inner;
                        }
                    }

                    return null;
                case JsonValueKind.Array:
                    var expectedItems = expected.EnumerateArray().ToList();
                    var actualItems = actual.EnumerateArray().ToList();
                    if (expectedItems.Count != actualItems.Count)
                    {
                        return $"{path} (length {expectedItems.Count} vs {actualItems.Count})";
                    }

                    for (var i = 0; i < expectedItems.Count; i++)
                    {
                        var inner = Compare(expectedItems[i], actualItems[i], $"{path}[{i}]");
                        if (inner is not null)
                        {
                            return inner;
                        }
                    }

                    return null;
                case JsonValueKind.Number:
                    return expected.GetDecimal() == actual.GetDecimal() ? null : $"{path} ({expected} vs {actual})";
                case JsonValueKind.String:
                    return expected.GetString() == actual.GetString() ? null : $"{path} ({expected} vs {actual})";
                default:
                    return null;
            }
        }
    }
}
