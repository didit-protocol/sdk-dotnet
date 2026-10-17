using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Didit
{
    /// <summary>Base class of every exception the Didit SDK throws.</summary>
    public class DiditException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public DiditException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with the exception that caused it.</summary>
        public DiditException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The client is not configured for the call: no API key, no organization or application id for an
    /// application-scoped operation, no account token, or an empty webhook secret. Raised before any request is sent.
    /// </summary>
    public class ConfigurationException : DiditException
    {
        /// <summary>Creates the exception.</summary>
        public ConfigurationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>No HTTP response was received (DNS, connection, TLS or a broken connection).</summary>
    public class ApiConnectionException : DiditException
    {
        /// <summary>Creates the exception.</summary>
        public ApiConnectionException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>The request did not complete within the configured timeout.</summary>
    public class ApiTimeoutException : ApiConnectionException
    {
        /// <summary>Creates the exception.</summary>
        public ApiTimeoutException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>The API answered with a non-2xx status.</summary>
    public class ApiStatusException : DiditException
    {
        /// <summary>Creates the exception.</summary>
        public ApiStatusException(
            string message,
            int statusCode,
            JsonElement? body,
            string? rawBody,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
            string? requestId)
            : base(message)
        {
            StatusCode = statusCode;
            Body = body;
            RawBody = rawBody;
            Headers = headers;
            RequestId = requestId;
        }

        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>The response body parsed as JSON, or <c>null</c> when it is empty or not JSON.</summary>
        public JsonElement? Body { get; }

        /// <summary>The response body as text.</summary>
        public string? RawBody { get; }

        /// <summary>Response headers (names are case-insensitive).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

        /// <summary>The request id Didit assigned to the request, when the response carries one. Quote it to support.</summary>
        public string? RequestId { get; }
    }

    /// <summary>HTTP 400.</summary>
    public class BadRequestException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public BadRequestException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 401: the API key or token is missing, wrong, revoked or expired.</summary>
    public class AuthenticationException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public AuthenticationException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 403: the credential has no access to this resource or action.</summary>
    public class PermissionDeniedException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public PermissionDeniedException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 404.</summary>
    public class NotFoundException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public NotFoundException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 409.</summary>
    public class ConflictException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public ConflictException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 422.</summary>
    public class UnprocessableEntityException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public UnprocessableEntityException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 429: too many requests. The SDK already retried <see cref="DiditClientOptions.MaxRetries"/> times.</summary>
    public class RateLimitException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public RateLimitException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>HTTP 5xx.</summary>
    public class InternalServerException : ApiStatusException
    {
        /// <summary>Creates the exception.</summary>
        public InternalServerException(string message, int statusCode, JsonElement? body, string? rawBody, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId)
            : base(message, statusCode, body, rawBody, headers, requestId)
        {
        }
    }

    /// <summary>A webhook delivery failed verification. <see cref="Reason"/> says why, as one of <see cref="WebhookVerificationReason"/>.</summary>
    public class WebhookVerificationException : DiditException
    {
        /// <summary>Creates the exception.</summary>
        public WebhookVerificationException(string reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        /// <summary>Machine-readable reason, one of the <see cref="WebhookVerificationReason"/> constants.</summary>
        public string Reason { get; }
    }

    /// <summary>The machine-readable reasons of <see cref="WebhookVerificationException"/>.</summary>
    public static class WebhookVerificationReason
    {
        /// <summary>No signature header was present.</summary>
        public const string MissingSignature = "missing_signature";

        /// <summary>No signature matched any of the secrets.</summary>
        public const string InvalidSignature = "invalid_signature";

        /// <summary>The body's <c>timestamp</c> and the <c>X-Timestamp</c> header differ.</summary>
        public const string TimestampMismatch = "timestamp_mismatch";

        /// <summary>The timestamp is further from now than the tolerance allows.</summary>
        public const string TimestampOutOfTolerance = "timestamp_out_of_tolerance";

        /// <summary>The body has no integer <c>timestamp</c>.</summary>
        public const string MissingTimestamp = "missing_timestamp";

        /// <summary>The body is not a JSON object.</summary>
        public const string InvalidPayload = "invalid_payload";
    }
}
