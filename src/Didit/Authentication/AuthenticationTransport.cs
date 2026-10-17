using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Internal
{
    /// <summary>A successful (2xx) response from the issuer.</summary>
    internal sealed class AuthHttpResponse
    {
        public AuthHttpResponse(int status, byte[] body, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, CacheControlHeaderValue? cacheControl)
        {
            Status = status;
            Body = body;
            Headers = headers;
            CacheControl = cacheControl;
        }

        public int Status { get; }

        public byte[] Body { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

        public CacheControlHeaderValue? CacheControl { get; }
    }

    /// <summary>
    /// Sends the Authentication requests to the issuer: SDK headers, timeout, error mapping and redaction. A <c>GET</c> that fails
    /// without a response is retried once; nothing else is retried.
    /// </summary>
    internal sealed class AuthenticationTransport
    {
        private static readonly string[] RequestIdHeaders = { "x-request-id", "request-id" };

        private readonly HttpClient _httpClient;
        private readonly TimeSpan _timeout;

        public AuthenticationTransport(HttpClient httpClient, TimeSpan timeout)
        {
            _httpClient = httpClient;
            _timeout = timeout;
        }

        /// <param name="method">HTTP method.</param>
        /// <param name="url">Absolute URL.</param>
        /// <param name="createContent">Creates the request body for each attempt, or <c>null</c> for none.</param>
        /// <param name="bearerToken">Sent as <c>Authorization: Bearer</c> when not <c>null</c>.</param>
        /// <param name="secret">Removed from every error message (the access, refresh or authorization code).</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<AuthHttpResponse> SendAsync(
            HttpMethod method,
            Uri url,
            Func<HttpContent>? createContent,
            string? bearerToken,
            string? secret,
            CancellationToken cancellationToken)
        {
            var maxAttempts = method == HttpMethod.Get ? 2 : 1;
            for (var attempt = 1; ; attempt++)
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (_timeout > TimeSpan.Zero && _timeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutSource.CancelAfter(_timeout);
                }

                HttpResponseMessage response;
                byte[] body;
                try
                {
                    using var request = new HttpRequestMessage(method, url);
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
                    request.Headers.TryAddWithoutValidation("X-Didit-Client", UserAgent.ClientHeader);
                    request.Headers.TryAddWithoutValidation("Accept", "application/json");
                    if (bearerToken is not null)
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken);
                    }

                    if (createContent is not null)
                    {
                        request.Content = createContent();
                    }

                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
                    try
                    {
#if NET8_0_OR_GREATER
                        body = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false);
#else
                        body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    throw new ApiTimeoutException($"Request timed out: {method} {url.AbsolutePath}.", ex);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt < maxAttempts)
                    {
                        continue;
                    }

                    throw new ApiConnectionException($"Connection error: {method} {url.AbsolutePath}: {ErrorFactory.Redact(ex.Message, secret)}", ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    var headers = CollectHeaders(response);
                    if (status >= 200 && status < 300)
                    {
                        return new AuthHttpResponse(status, body, headers, response.Headers.CacheControl);
                    }

                    throw ErrorFactory.CreateStatusException(status, response.ReasonPhrase, body, headers, FindRequestId(headers), secret);
                }
            }
        }

        private static string? FindRequestId(Dictionary<string, IReadOnlyList<string>> headers)
        {
            foreach (var name in RequestIdHeaders)
            {
                if (headers.TryGetValue(name, out var values) && values.Count > 0 && !string.IsNullOrEmpty(values[0]))
                {
                    return values[0];
                }
            }

            return null;
        }

        private static Dictionary<string, IReadOnlyList<string>> CollectHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                headers[header.Key] = header.Value.ToArray();
            }

            return headers;
        }
    }
}
