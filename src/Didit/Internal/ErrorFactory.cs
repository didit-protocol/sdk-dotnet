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

            return Build(status, message, json, rawBody, headers, requestId);
        }

        /// <summary>The whole response of a call whose answer may echo secrets, sanitized once; see <see cref="Sanitize"/>.</summary>
        public sealed class SanitizedResponse
        {
            public SanitizedResponse(string? reason, byte[] body, Dictionary<string, IReadOnlyList<string>> headers)
            {
                Reason = reason;
                Body = body;
                Headers = headers;
            }

            public string? Reason { get; }

            public byte[] Body { get; }

            public Dictionary<string, IReadOnlyList<string>> Headers { get; }
        }

        /// <summary>
        /// Sanitizes the whole response of a call whose answer may echo secrets (the agent consent calls), once, on receipt: the
        /// reason phrase is redacted, each header whose name contains a secret is dropped, the other values are redacted, and the
        /// body is redacted structurally (every string, object key or value, that contains a secret anywhere in it becomes
        /// <c>[redacted]</c> as a whole; colliding redacted keys are numbered; a body that is not JSON, has trailing content, was
        /// cut at the size limit or is empty becomes <c>[redacted]</c>). Every error of these calls is built from the result only.
        /// </summary>
        public static SanitizedResponse Sanitize(
            string? reason,
            byte[] body,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            params string?[] secrets)
        {
            var active = secrets.Where(secret => !string.IsNullOrEmpty(secret)).Select(secret => secret!).ToArray();
            var clean = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                if (RedactText(header.Key, active) == header.Key)
                {
                    clean[header.Key] = header.Value.Select(value => RedactText(value, active)).ToArray();
                }
            }

            var text = Redacted; // an empty body is not JSON
            if (body.Length > 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    using var stream = new System.IO.MemoryStream();
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        WriteRedacted(writer, document.RootElement, active);
                    }

                    text = Encoding.UTF8.GetString(stream.ToArray());
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or InsufficientExecutionStackException)
                {
                    // Not JSON, trailing content, or deeper than the parser's depth limit (64): not JSON.
                    text = Redacted;
                }
            }

            return new SanitizedResponse(reason is null ? null : RedactText(reason, active), Encoding.UTF8.GetBytes(text), clean);
        }

        /// <summary><paramref name="text"/>, or <c>[redacted]</c> as a whole when it contains a secret anywhere.</summary>
        public static string RedactText(string text, IReadOnlyList<string> secrets)
        {
            foreach (var secret in secrets)
            {
                if (secret.Length > 0 && text.IndexOf(secret, StringComparison.Ordinal) >= 0)
                {
                    return Redacted;
                }
            }

            return text;
        }

        private const string Redacted = "[redacted]";

        private static string RedactString(JsonElement value, IReadOnlyList<string> secrets)
        {
            try
            {
                return RedactText(value.GetString() ?? string.Empty, secrets);
            }
            catch (InvalidOperationException)
            {
                // A lone escaped surrogate cannot be decoded, so it cannot be compared: drop it.
                return Redacted;
            }
        }

        private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element, IReadOnlyList<string> secrets)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    writer.WriteStringValue(RedactString(element, secrets));
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteRedacted(writer, item, secrets);
                    }

                    writer.WriteEndArray();
                    break;
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    // Reserve every safe key name first, so a placeholder never takes one.
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (RedactText(property.Name, secrets) == property.Name)
                        {
                            seen.Add(property.Name);
                        }
                    }

                    var next = 1;
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = property.Name;
                        if (RedactText(name, secrets) != name)
                        {
                            do
                            {
                                name = next == 1 ? Redacted : Redacted + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                next++;
                            }
                            while (seen.Contains(name));
                            seen.Add(name);
                        }

                        writer.WritePropertyName(name);
                        WriteRedacted(writer, property.Value, secrets);
                    }

                    writer.WriteEndObject();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static ApiStatusException Build(
            int status,
            string message,
            JsonElement? json,
            string? rawBody,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string? requestId)
        {
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
