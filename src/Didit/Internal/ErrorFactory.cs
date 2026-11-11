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

        /// <summary>
        /// Builds the exception for an answer that may echo secrets (the agent consent calls) from the redacted response only,
        /// never from its raw text. A body that parses as JSON is redacted structurally: every string, object key or value, that
        /// contains a secret anywhere in it becomes <c>[redacted]</c> as a whole (strings are compared as decoded, so the spelling
        /// of escapes in the raw text does not matter) and a redacted key that collides with another is numbered
        /// (<c>[redacted]</c>, <c>[redacted]2</c>, ...). A body that does not parse (an HTML error page, a body cut at the size
        /// limit) is replaced by <c>[redacted]</c>. The message derives from the redacted body.
        /// </summary>
        public static ApiStatusException CreateRedactedStatusException(
            int status,
            string? reason,
            byte[] body,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string? requestId,
            params string?[] secrets)
        {
            var active = secrets.Where(secret => !string.IsNullOrEmpty(secret)).Select(secret => secret!).ToArray();
            var clean = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                clean[header.Key] = header.Value.Select(value => RedactText(value, active)).ToArray();
            }

            string? rawBody = null;
            JsonElement? json = null;
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

                    rawBody = Encoding.UTF8.GetString(stream.ToArray());
                    using var redacted = JsonDocument.Parse(rawBody);
                    json = redacted.RootElement.Clone();
                }
                catch (JsonException)
                {
                    rawBody = Redacted;
                }
            }

            var detail = DescribeBody(json, rawBody);
            var message = $"{status} {reason ?? StatusName(status)}" + (detail is null ? string.Empty : ": " + detail) + (requestId is null ? string.Empty : $" (request id {requestId})");
            return Build(status, message, json, rawBody, clean, requestId);
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
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = RedactText(property.Name, secrets);
                        for (var number = 2; !seen.Add(name); number++)
                        {
                            name = Redacted + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
