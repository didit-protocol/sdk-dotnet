using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Tests.Authentication
{
    /// <summary>A request the stub received, with its body read eagerly.</summary>
    public sealed class RecordedRequest
    {
        public RecordedRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, string? contentType, string? body)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
            ContentType = contentType;
            Body = body;
        }

        public HttpMethod Method { get; }

        public Uri Uri { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public string? ContentType { get; }

        public string? Body { get; }
    }

    /// <summary>An in-memory issuer: routes requests to a responder and records them. Never touches the network.</summary>
    public sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _responder;
        private readonly List<RecordedRequest> _requests = new();
        private readonly object _gate = new();

        public StubHttpHandler(Func<RecordedRequest, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        public int Count(string path)
        {
            lock (_gate)
            {
                return _requests.Count(r => r.Uri.AbsolutePath == path);
            }
        }

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? cacheControl = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (cacheControl is not null)
            {
                response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
            }

            return response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            string? body = null;
            string? contentType = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync();
                contentType = request.Content.Headers.ContentType?.MediaType;
            }

            var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, contentType, body);
            lock (_gate)
            {
                _requests.Add(recorded);
            }

            return _responder(recorded);
        }
    }
}
