using System;
using System.Collections.Generic;
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

        public static string Redact(string text, string? secret)
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
    }
}
