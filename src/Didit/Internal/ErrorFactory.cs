using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>Builds the <see cref="ApiStatusException"/> subclass for a non-2xx response.</summary>
    internal static class ErrorFactory
    {
        public static ApiStatusException CreateStatusException(
            int status,
            string? reason,
            byte[] body,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string? requestId,
            params string?[] secrets)
        {
            // Longest first, so a secret that contains another is replaced whole.
            secrets = secrets.OrderByDescending(secret => secret?.Length ?? 0).ToArray();
            headers = RedactHeaders(headers, secrets);
            string? rawBody = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
            foreach (var secret in secrets)
            {
                rawBody = rawBody is null ? null : Redact(rawBody, secret);
            }

            // Parsed from the redacted text, so a credential the server echoes back is not kept in Body either.
            JsonElement? json = null;
            if (rawBody is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(rawBody);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }

            var detail = DescribeBody(json, rawBody);
            var message = $"{status} {reason ?? StatusName(status)}" + (detail is null ? string.Empty : ": " + detail) + (requestId is null ? string.Empty : $" (request id {requestId})");
            foreach (var secret in secrets)
            {
                message = Redact(message, secret);
            }

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
                    if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && TryGetString(value, out var text))
                    {
                        return Truncate(text);
                    }
                }

                return Truncate(element.GetRawText());
            }

            return Truncate(rawBody);
        }

        // GetString throws on a lone escaped surrogate; the raw text is described instead.
        private static bool TryGetString(JsonElement value, out string? text)
        {
            try
            {
                text = value.GetString();
                return true;
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                text = null;
                return false;
            }
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

        public static string Redact(string text, string? secret)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text))
            {
                return text;
            }

            // The secret as sent and as a JSON body may spell it (quotes, slashes and non-ASCII escaped).
            foreach (var form in Spellings(secret!))
            {
#if NET8_0_OR_GREATER
                text = text.Replace(form, "[redacted]", StringComparison.Ordinal);
#else
                text = text.Replace(form, "[redacted]");
#endif
            }

            return text;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> RedactHeaders(
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string?[] secrets)
        {
            var clean = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                clean[header.Key] = header.Value.Select(value => secrets.Aggregate(value, Redact)).ToArray();
            }

            return clean;
        }

        private static readonly JsonSerializerOptions[] EscapingOptions =
        {
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
        };

        private static IEnumerable<string> Spellings(string secret)
        {
            var forms = new List<string> { secret };
            foreach (var options in EscapingOptions)
            {
                var escaped = JsonSerializer.Serialize(secret, options);
                forms.Add(escaped.Substring(1, escaped.Length - 2));
                forms.Add(escaped.Substring(1, escaped.Length - 2).Replace("/", "\\/"));
            }

            forms.Add(secret.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("/", "\\/"));
            return forms.Where(form => form.Length > 0).Distinct().OrderByDescending(form => form.Length);
        }

        private static string StatusName(int status) =>
            Enum.IsDefined(typeof(HttpStatusCode), status) ? ((HttpStatusCode)status).ToString() : "Error";
    }
}
