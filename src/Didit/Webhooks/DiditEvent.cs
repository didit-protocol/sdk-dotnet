using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// A verified webhook event. Match on the subtype (<see cref="SessionStatusUpdatedEvent"/>,
    /// <see cref="TransactionCreatedEvent"/>, ...); any type this SDK version does not know is an <see cref="UnknownEvent"/>.
    /// Fields of an unexpected JSON type read as <c>null</c>; the whole body is always available in <see cref="Payload"/>.
    /// </summary>
    public abstract record DiditEvent
    {
        private protected DiditEvent()
        {
        }

        private protected DiditEvent(JsonElement payload, WebhookSignature verifiedBy)
        {
            Payload = payload;
            VerifiedBy = verifiedBy;
            EventId = WebhookJson.GetString(payload, "event_id");
            WebhookType = WebhookEventFactory.WebhookTypeOf(payload);
            Timestamp = WebhookJson.GetInt64(payload, "timestamp") ?? 0;
            CreatedAt = WebhookJson.GetInt64(payload, "created_at");
            ApplicationId = WebhookJson.GetString(payload, "application_id");
            Environment = WebhookJson.GetString(payload, "environment");
        }

        /// <summary>Unique id of the event (<c>event_id</c>). Use it to discard duplicate deliveries.</summary>
        public string? EventId { get; init; }

        /// <summary>The event type as sent (<c>webhook_type</c>), for example <c>status.updated</c>.</summary>
        public string WebhookType { get; init; } = string.Empty;

        /// <summary>When the delivery was signed (<c>timestamp</c>), in unix seconds.</summary>
        public long Timestamp { get; init; }

        /// <summary>When the underlying change happened (<c>created_at</c>), in unix seconds.</summary>
        public long? CreatedAt { get; init; }

        /// <summary>The application the event belongs to (<c>application_id</c>).</summary>
        public string? ApplicationId { get; init; }

        /// <summary><c>live</c> or <c>sandbox</c> (<c>environment</c>); see <see cref="WebhookEnvironment"/>.</summary>
        public string? Environment { get; init; }

        /// <summary>The whole verified body, including fields this SDK version has no property for.</summary>
        public JsonElement Payload { get; init; }

        /// <summary>The signature that authenticated the delivery.</summary>
        public WebhookSignature VerifiedBy { get; init; }

        /// <summary>
        /// <c>false</c> when the delivery was accepted on the deprecated <c>X-Signature-Simple</c>, which covers only the
        /// timestamp, <c>session_id</c>, <c>status</c> and <c>webhook_type</c>. Then treat every other field as untrusted and
        /// re-fetch the record from the API.
        /// </summary>
        public bool IsBodyAuthenticated => VerifiedBy != WebhookSignature.Simple;
    }

    /// <summary>The signature that authenticated a webhook delivery.</summary>
    public enum WebhookSignature
    {
        /// <summary><c>X-Signature</c> (or <c>X-Signature-Previous</c>): HMAC-SHA256 of the exact raw body.</summary>
        RawBody = 0,

        /// <summary><c>X-Signature-V2</c> (or <c>X-Signature-V2-Previous</c>): HMAC-SHA256 of the canonical JSON.</summary>
        Canonical = 1,

        /// <summary>
        /// <c>X-Signature-Simple</c> (or <c>X-Signature-Simple-Previous</c>), deprecated: covers the envelope only, so the
        /// body is not authenticated. Accepted only when the caller passes <c>allowSimple: true</c>.
        /// </summary>
        Simple = 2,
    }

    /// <summary>The values of <see cref="DiditEvent.Environment"/>.</summary>
    public static class WebhookEnvironment
    {
        /// <summary>A live application.</summary>
        public const string Live = "live";

        /// <summary>A sandbox application.</summary>
        public const string Sandbox = "sandbox";
    }
}
