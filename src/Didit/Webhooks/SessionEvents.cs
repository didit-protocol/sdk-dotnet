using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// A verification session event: <see cref="SessionStatusUpdatedEvent"/> or <see cref="SessionDataUpdatedEvent"/>.
    /// Business (KYB) sessions send the same events with <see cref="SessionKind"/> <c>business</c>.
    /// </summary>
    public abstract record SessionEvent : DiditEvent
    {
        private protected SessionEvent()
        {
        }

        private protected SessionEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            SessionId = WebhookJson.GetString(payload, "session_id");
            BusinessSessionId = WebhookJson.GetString(payload, "business_session_id");
            SessionKind = WebhookJson.GetString(payload, "session_kind");
            Status = WebhookJson.GetString(payload, "status");
            WorkflowId = WebhookJson.GetString(payload, "workflow_id");
            WorkflowVersion = WebhookJson.GetInt64(payload, "workflow_version");
            VendorData = WebhookJson.GetString(payload, "vendor_data");
            Metadata = WebhookJson.GetElement(payload, "metadata");
            Decision = WebhookEventFactory.ReadDecision(payload);
            ResubmitInfo = WebhookJson.GetElement(payload, "resubmit_info");
        }

        /// <summary>The session id (<c>session_id</c>).</summary>
        public string? SessionId { get; init; }

        /// <summary>The business session id (<c>business_session_id</c>), set for business (KYB) sessions.</summary>
        public string? BusinessSessionId { get; init; }

        /// <summary><c>business</c> for business (KYB) sessions (<c>session_kind</c>); absent for user sessions.</summary>
        public string? SessionKind { get; init; }

        /// <summary>The session status (<c>status</c>), for example <c>Approved</c>, <c>Declined</c> or <c>In Review</c>.</summary>
        public string? Status { get; init; }

        /// <summary>The workflow the session runs (<c>workflow_id</c>).</summary>
        public string? WorkflowId { get; init; }

        /// <summary>The workflow version (<c>workflow_version</c>).</summary>
        public long? WorkflowVersion { get; init; }

        /// <summary>Your identifier for the user or business (<c>vendor_data</c>).</summary>
        public string? VendorData { get; init; }

        /// <summary>The metadata you attached to the session (<c>metadata</c>).</summary>
        public JsonElement? Metadata { get; init; }

        /// <summary>
        /// The decision (<c>decision</c>), sent once the session is approved, declined, in review or abandoned. With webhook
        /// version <c>v3</c> it has the shape of <c>GET /v3/session/{id}/decision/</c>; older versions send fewer fields.
        /// <c>null</c> when absent or unreadable; the raw value is always in <see cref="DiditEvent.Payload"/>.
        /// </summary>
        public SessionDecision? Decision { get; init; }

        /// <summary>What the user must resubmit (<c>resubmit_info</c>), sent when the status is <c>Resubmitted</c>.</summary>
        public JsonElement? ResubmitInfo { get; init; }
    }

    /// <summary><c>status.updated</c>: a session's status changed.</summary>
    public sealed record SessionStatusUpdatedEvent : SessionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public SessionStatusUpdatedEvent()
        {
        }

        internal SessionStatusUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>data.updated</c>: a session's data changed, for example after a manual review edit.</summary>
    public sealed record SessionDataUpdatedEvent : SessionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public SessionDataUpdatedEvent()
        {
        }

        internal SessionDataUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }
}
