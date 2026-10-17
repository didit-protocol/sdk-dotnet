using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Internal
{
    /// <summary>Sends <see cref="ApiRequest"/>s: authentication headers, retries, timeouts, errors and logging.</summary>
    internal sealed class HttpPipeline
    {
        private const string ApplicationPrefix = "/v3/organization/{organization_id}/application/{application_id}/";
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(0.5);
        private static readonly string[] RequestIdHeaders = { "x-request-id", "request-id", "x-amzn-requestid" };

        private readonly DiditClientOptions _options;
        private readonly HttpClient _httpClient;

        public HttpPipeline(DiditClientOptions options, HttpClient httpClient)
        {
            _options = options;
            _httpClient = httpClient;
        }

        /// <summary>Delay between attempts; replaced in tests.</summary>
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, token) => Task.Delay(delay, token);

        /// <summary>Jitter source in [0, 1); replaced in tests.</summary>
        internal Func<double> Jitter { get; set; } = SharedRandom.NextDouble;

        /// <summary>Clock used for HTTP-date <c>Retry-After</c> values; replaced in tests.</summary>
        internal Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

        public async Task<DiditResponse<T>> SendJsonAsync<T>(ApiRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            var raw = await SendAsync(request, requestOptions, cancellationToken).ConfigureAwait(false);
            return new DiditResponse<T>(raw, ParseJson<T>(raw));
        }

        public Task<DiditResponse> SendNoContentAsync(ApiRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) =>
            SendAsync(request, requestOptions, cancellationToken);

        public async Task<DiditResponse<BinaryResponse>> SendBinaryAsync(ApiRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            var raw = await SendAsync(request, requestOptions, cancellationToken).ConfigureAwait(false);
            var binary = new BinaryResponse(raw.Body, raw.GetHeader("Content-Type"), BinaryResponse.ParseFileName(raw.GetHeader("Content-Disposition")));
            return new DiditResponse<BinaryResponse>(raw, binary);
        }

        public async Task<DiditResponse<Page<T>>> SendPageAsync<T>(ApiRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            var raw = await SendAsync(request, requestOptions, cancellationToken).ConfigureAwait(false);
            var body = ParseJson<PageBody<T>>(raw) ?? new PageBody<T>();
            var page = new Page<T>(body, async (next, token) =>
            {
                var nextRequest = request.WithAbsoluteUrl(ResolvePageUrl(next, request.Host, requestOptions));
                var response = await SendPageAsync<T>(nextRequest, requestOptions, token).ConfigureAwait(false);
                return response.Data;
            });
            return new DiditResponse<Page<T>>(raw, page);
        }

        internal static T ParseJson<T>(DiditResponse raw)
        {
            if (raw.Body.Length == 0)
            {
                return default!;
            }

            try
            {
                return JsonSupport.Deserialize<T>(raw.Body)!;
            }
            catch (JsonException ex)
            {
                throw new DiditException(
                    $"Could not parse the response as {typeof(T).Name}: {ex.Message}" + (raw.RequestId is null ? string.Empty : $" (request id {raw.RequestId})"),
                    ex);
            }
        }

        public async Task<DiditResponse> SendAsync(ApiRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            var settings = Resolve(request, requestOptions);
            var url = BuildUrl(request, settings);
            var maxRetries = Math.Max(0, requestOptions?.MaxRetries ?? _options.MaxRetries);
            var timeout = requestOptions?.Timeout ?? _options.Timeout;
            var logPath = url.AbsolutePath;

            for (var attempt = 0; ; attempt++)
            {
                var canRetry = attempt < maxRetries;
                var started = Stopwatch.StartNew();
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutSource.CancelAfter(timeout);
                }

                HttpResponseMessage response;
                byte[] body;
                try
                {
                    using var message = await BuildMessageAsync(request, url, settings, requestOptions, cancellationToken).ConfigureAwait(false);
                    response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
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
                    Log($"{request.Method} {logPath} timed out after {started.ElapsedMilliseconds} ms (attempt {attempt + 1})");
                    if (canRetry && IsIdempotent(request.Method))
                    {
                        await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new ApiTimeoutException($"Request timed out: {request.Method} {logPath}.", ex);
                }
                catch (HttpRequestException ex)
                {
                    var beforeSend = FailedBeforeSending(ex);
                    Log($"{request.Method} {logPath} connection error after {started.ElapsedMilliseconds} ms (attempt {attempt + 1})");
                    if (canRetry && (IsIdempotent(request.Method) || beforeSend))
                    {
                        await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new ApiConnectionException($"Connection error: {request.Method} {logPath}: {Redact(ex.Message, settings.ApiKey)}", ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    var headers = CollectHeaders(response);
                    var requestId = FindRequestId(headers);
                    Log($"{request.Method} {logPath} -> {status} in {started.ElapsedMilliseconds} ms (request id {requestId ?? "-"})");

                    if (status >= 200 && status < 300)
                    {
                        return new DiditResponse(status, headers, requestId, body);
                    }

                    if (canRetry && ShouldRetryStatus(request.Method, status))
                    {
                        await Delay(RetryDelay(attempt, headers), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw CreateStatusException(status, response.ReasonPhrase, body, headers, requestId, settings.ApiKey);
                }
            }
        }

        internal static bool IsIdempotent(HttpMethod method) =>
            method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put || method == HttpMethod.Delete || method == HttpMethod.Options;

        internal static bool ShouldRetryStatus(HttpMethod method, int status)
        {
            if (status == 429)
            {
                return true;
            }

            return IsIdempotent(method) && (status == 408 || status >= 500);
        }

        internal TimeSpan Backoff(int attempt)
        {
            var cap = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, (long)(InitialBackoff.Ticks * Math.Pow(2, attempt))));
            return TimeSpan.FromTicks((long)(cap.Ticks * Jitter()));
        }

        internal TimeSpan RetryDelay(int attempt, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        {
            if (headers.TryGetValue("Retry-After", out var values) && values.Count > 0)
            {
                var value = values[0].Trim();
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                {
                    return seconds >= MaxRetryAfter.TotalSeconds ? MaxRetryAfter : TimeSpan.FromSeconds(seconds);
                }

                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                {
                    var wait = date - UtcNow();
                    if (wait < TimeSpan.Zero)
                    {
                        return TimeSpan.Zero;
                    }

                    return wait > MaxRetryAfter ? MaxRetryAfter : wait;
                }
            }

            return Backoff(attempt);
        }

        internal static bool FailedBeforeSending(HttpRequestException exception)
        {
#if NET8_0_OR_GREATER
            switch (exception.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError:
                case HttpRequestError.ConnectionError:
                case HttpRequestError.SecureConnectionError:
                case HttpRequestError.ProxyTunnelError:
                    return true;
            }
#endif
            for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is SocketException socket)
                {
                    switch (socket.SocketErrorCode)
                    {
                        case SocketError.ConnectionRefused:
                        case SocketError.HostNotFound:
                        case SocketError.HostUnreachable:
                        case SocketError.NetworkUnreachable:
                        case SocketError.NoData:
                        case SocketError.TryAgain:
                        case SocketError.AddressNotAvailable:
                            return true;
                    }
                }
            }

            return false;
        }

        private Uri ResolvePageUrl(string next, ApiHost host, RequestOptions? requestOptions)
        {
            var baseUrl = host == ApiHost.Account
                ? requestOptions?.AccountBaseUrl ?? _options.AccountBaseUrl
                : requestOptions?.BaseUrl ?? _options.BaseUrl;
            if (!Uri.TryCreate(next, UriKind.RelativeOrAbsolute, out var nextUrl))
            {
                throw new DiditException($"The API returned an invalid next page URL.");
            }

            if (!nextUrl.IsAbsoluteUri)
            {
                return new Uri(baseUrl, nextUrl);
            }

            // Only the configured API origin ever receives the API key. A next URL on the same host is accepted with the
            // configured scheme and port (an upstream proxy may report http), anything else is refused.
            var samePort = nextUrl.IsDefaultPort || baseUrl.IsDefaultPort ? nextUrl.IsDefaultPort == baseUrl.IsDefaultPort || nextUrl.Port == baseUrl.Port : nextUrl.Port == baseUrl.Port;
            if (!string.Equals(nextUrl.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase) || !samePort)
            {
                throw new DiditException($"Refusing to follow a next page URL on another origin ({nextUrl.Scheme}://{nextUrl.Authority}); the API key is only sent to {baseUrl.Scheme}://{baseUrl.Authority}.");
            }

            var builder = new UriBuilder(baseUrl) { Path = nextUrl.AbsolutePath, Query = nextUrl.Query.TrimStart('?') };
            return builder.Uri;
        }

        private ResolvedSettings Resolve(ApiRequest request, RequestOptions? requestOptions)
        {
            var settings = new ResolvedSettings
            {
                BaseUrl = request.Host == ApiHost.Account
                    ? requestOptions?.AccountBaseUrl ?? _options.AccountBaseUrl
                    : requestOptions?.BaseUrl ?? _options.BaseUrl,
                ApiKey = NullIfEmpty(requestOptions?.ApiKey) ?? NullIfEmpty(_options.ApiKey),
                AccountToken = NullIfEmpty(requestOptions?.AccountToken) ?? NullIfEmpty(_options.AccountToken),
                OrganizationId = NullIfEmpty(requestOptions?.OrganizationId) ?? NullIfEmpty(_options.OrganizationId),
                ApplicationId = NullIfEmpty(requestOptions?.ApplicationId) ?? NullIfEmpty(_options.ApplicationId),
            };

            switch (request.Auth)
            {
                case AuthKind.ApiKey when settings.ApiKey is null:
                    throw new ConfigurationException("No API key. Pass DiditClientOptions.ApiKey or set the DIDIT_API_KEY environment variable.");
                case AuthKind.AccountToken when settings.AccountToken is null:
                    throw new ConfigurationException("This account operation needs DiditClientOptions.AccountToken: the access_token returned by client.Account.LoginAsync or VerifyEmailAsync.");
            }

            if (request.ApplicationScoped && (settings.OrganizationId is null || settings.ApplicationId is null))
            {
                throw new ConfigurationException(
                    "This operation is application-scoped. Set DiditClientOptions.OrganizationId and ApplicationId (or the DIDIT_ORGANIZATION_ID and DIDIT_APPLICATION_ID environment variables).");
            }

            return settings;
        }

        private static Uri BuildUrl(ApiRequest request, ResolvedSettings settings)
        {
            if (request.AbsoluteUrl is not null)
            {
                return request.AbsoluteUrl;
            }

            var path = request.Path;
            if (request.ApplicationScoped)
            {
                if (!path.StartsWith(ApplicationPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Application-scoped path without the application prefix: " + path);
                }

                path = "/v3/organization/" + Uri.EscapeDataString(settings.OrganizationId!) + "/application/" + Uri.EscapeDataString(settings.ApplicationId!) + "/" + path.Substring(ApplicationPrefix.Length);
            }

            var baseText = settings.BaseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return new Uri(baseText + path + request.Query.Encode(), UriKind.Absolute);
        }

        private async Task<HttpRequestMessage> BuildMessageAsync(ApiRequest request, Uri url, ResolvedSettings settings, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(request.Method, url);
            try
            {
                var headers = message.Headers;
                headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
                headers.TryAddWithoutValidation("X-Didit-Client", UserAgent.ClientHeader);
                headers.TryAddWithoutValidation("Accept", "application/json");

                foreach (var header in _options.DefaultHeaders)
                {
                    SetHeader(message, header.Key, header.Value);
                }

                if (requestOptions?.Headers is not null)
                {
                    foreach (var header in requestOptions.Headers)
                    {
                        SetHeader(message, header.Key, header.Value);
                    }
                }

                if (!string.IsNullOrEmpty(requestOptions?.IdempotencyKey))
                {
                    SetHeader(message, "Idempotency-Key", requestOptions!.IdempotencyKey!);
                }

                switch (request.Auth)
                {
                    case AuthKind.ApiKey:
                        if (!SameOrigin(url, settings.BaseUrl))
                        {
                            throw new DiditException("Refusing to send the API key to an origin other than the configured base URL.");
                        }

                        SetHeader(message, "x-api-key", settings.ApiKey!);
                        break;
                    case AuthKind.AccountToken:
                        SetHeader(message, "Authorization", "Bearer " + settings.AccountToken);
                        break;
                }

                if (request.Body is not null)
                {
                    message.Content = await request.Body.CreateContentAsync(cancellationToken).ConfigureAwait(false);
                }

                return message;
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }

        private static void SetHeader(HttpRequestMessage message, string name, string value)
        {
            message.Headers.Remove(name);
            message.Headers.TryAddWithoutValidation(name, value);
        }

        private static bool SameOrigin(Uri a, Uri b) =>
            string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value.ToList();
            }

            if (response.Content is not null)
            {
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = header.Value.ToList();
                }
            }

            return headers;
        }

        private static string? FindRequestId(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        {
            foreach (var name in RequestIdHeaders)
            {
                if (headers.TryGetValue(name, out var values) && values.Count > 0 && values[0].Length > 0)
                {
                    return values[0];
                }
            }

            return null;
        }

        internal static ApiStatusException CreateStatusException(
            int status,
            string? reason,
            byte[] body,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string? requestId,
            string? apiKey)
        {
            string? rawBody = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
            JsonElement? json = null;
            if (rawBody is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }

            var detail = DescribeBody(json, rawBody);
            var message = $"{status} {reason ?? StatusName(status)}" + (detail is null ? string.Empty : ": " + detail) + (requestId is null ? string.Empty : $" (request id {requestId})");
            message = Redact(message, apiKey);
            rawBody = rawBody is null ? null : Redact(rawBody, apiKey);

            return status switch
            {
                400 => new BadRequestException(message, status, json, rawBody, headers, requestId),
                401 => new AuthenticationException(message, status, json, rawBody, headers, requestId),
                403 => new PermissionDeniedException(message, status, json, rawBody, headers, requestId),
                404 => new NotFoundException(message, status, json, rawBody, headers, requestId),
                409 => new ConflictException(message, status, json, rawBody, headers, requestId),
                422 => new UnprocessableEntityException(message, status, json, rawBody, headers, requestId),
                429 => new RateLimitException(message, status, json, rawBody, headers, requestId),
                >= 500 => new InternalServerException(message, status, json, rawBody, headers, requestId),
                _ => new ApiStatusException(message, status, json, rawBody, headers, requestId),
            };
        }

        private static string? DescribeBody(JsonElement? json, string? rawBody)
        {
            if (json is { ValueKind: JsonValueKind.Object } element)
            {
                foreach (var name in new[] { "detail", "error", "message", "error_description" })
                {
                    if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return Truncate(value.GetString());
                    }
                }

                return Truncate(element.GetRawText());
            }

            return Truncate(rawBody);
        }

        private static string? Truncate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = text!.Trim();
            return text.Length <= 500 ? text : text.Substring(0, 500) + "...";
        }

        internal static string Redact(string text, string? secret)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text))
            {
                return text;
            }

#if NET8_0_OR_GREATER
            return text.Replace(secret, "[redacted]", StringComparison.Ordinal);
#else
            return text.Replace(secret, "[redacted]");
#endif
        }

        private static string StatusName(int status) =>
            Enum.IsDefined(typeof(HttpStatusCode), status) ? ((HttpStatusCode)status).ToString() : "Error";

        private void Log(string line)
        {
            var logger = _options.Logger ?? (DebugFromEnvironment ? Console.Error.WriteLine : null);
            logger?.Invoke("didit: " + line);
        }

        private static readonly bool DebugFromEnvironment =
            string.Equals(Environment.GetEnvironmentVariable("DIDIT_LOG"), "debug", StringComparison.OrdinalIgnoreCase);

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private sealed class ResolvedSettings
        {
            public Uri BaseUrl { get; set; } = DiditClientOptions.DefaultBaseUrl;

            public string? ApiKey { get; set; }

            public string? AccountToken { get; set; }

            public string? OrganizationId { get; set; }

            public string? ApplicationId { get; set; }
        }

        private static class SharedRandom
        {
            private static readonly object Gate = new();
            private static readonly Random Random = new();

            public static double NextDouble()
            {
                lock (Gate)
                {
                    return Random.NextDouble();
                }
            }
        }
    }
}
