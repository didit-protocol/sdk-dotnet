using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary><c>activity.created</c>: an activity record was created. Its fields are in <see cref="DiditEvent.Payload"/>.</summary>
    public sealed record ActivityCreatedEvent : DiditEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public ActivityCreatedEvent()
        {
        }

        internal ActivityCreatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary>
    /// <c>workflow.compliance_changed</c>: re-evaluating a published workflow against the compliance knowledge base
    /// changed its compliance status.
    /// </summary>
    public sealed record WorkflowComplianceChangedEvent : DiditEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public WorkflowComplianceChangedEvent()
        {
        }

        internal WorkflowComplianceChangedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            WorkflowId = WebhookJson.GetString(payload, "workflow_id");
            WorkflowVersion = WebhookJson.GetInt64(payload, "workflow_version");
            WorkflowLabel = WebhookJson.GetString(payload, "workflow_label");
            var transition = WebhookJson.GetObject(payload, "transition");
            if (transition is { } value)
            {
                FromStatus = WebhookJson.GetString(value, "from");
                ToStatus = WebhookJson.GetString(value, "to");
            }

            Severity = WebhookJson.GetString(payload, "severity");
            KbVersion = WebhookJson.GetString(payload, "kb_version");
        }

        /// <summary>The workflow (<c>workflow_id</c>).</summary>
        public string? WorkflowId { get; init; }

        /// <summary>The workflow version (<c>workflow_version</c>).</summary>
        public long? WorkflowVersion { get; init; }

        /// <summary>The workflow's label (<c>workflow_label</c>).</summary>
        public string? WorkflowLabel { get; init; }

        /// <summary>The previous compliance status (<c>transition.from</c>).</summary>
        public string? FromStatus { get; init; }

        /// <summary>The new compliance status (<c>transition.to</c>).</summary>
        public string? ToStatus { get; init; }

        /// <summary>How serious the change is (<c>severity</c>).</summary>
        public string? Severity { get; init; }

        /// <summary>The knowledge base version the workflow was evaluated against (<c>kb_version</c>).</summary>
        public string? KbVersion { get; init; }
    }

    /// <summary>
    /// <c>kyb.registry_search.resolved</c>: an asynchronous KYB registry search finished. The type arrives in
    /// <c>event_type</c> rather than <c>webhook_type</c>.
    /// </summary>
    /// <remarks>
    /// Didit currently sends this callback unsigned (header <c>X-Didit-Unsigned-Callback: true</c>), so
    /// <c>DiditWebhooks.Verify</c> rejects it with <see cref="WebhookVerificationReason.MissingSignature"/>. Treat its content
    /// as untrusted and fetch the result from the API.
    /// </remarks>
    public sealed record KybRegistrySearchResolvedEvent : DiditEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public KybRegistrySearchResolvedEvent()
        {
        }

        internal KybRegistrySearchResolvedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            RequestId = WebhookJson.GetString(payload, "request_id");
            VendorData = WebhookJson.GetString(payload, "vendor_data");
            Metadata = WebhookJson.GetElement(payload, "metadata");
            SearchStatus = WebhookJson.GetString(payload, "search_status");
            SearchResolved = WebhookJson.GetBoolean(payload, "search_resolved");
            KybRegistry = WebhookJson.GetElement(payload, "kyb_registry");
        }

        /// <summary>The search request id returned by <c>POST /v3/kyb/search/</c> (<c>request_id</c>).</summary>
        public string? RequestId { get; init; }

        /// <summary>Your identifier for the lookup (<c>vendor_data</c>).</summary>
        public string? VendorData { get; init; }

        /// <summary>The metadata you attached to the search (<c>metadata</c>).</summary>
        public JsonElement? Metadata { get; init; }

        /// <summary>The search status (<c>search_status</c>), for example <c>resolved</c>.</summary>
        public string? SearchStatus { get; init; }

        /// <summary>Whether the search resolved (<c>search_resolved</c>).</summary>
        public bool? SearchResolved { get; init; }

        /// <summary>The candidate list (<c>kyb_registry</c>).</summary>
        public JsonElement? KybRegistry { get; init; }
    }

    /// <summary>
    /// An event of a type this SDK version does not know. <see cref="DiditEvent.WebhookType"/> names it and
    /// <see cref="DiditEvent.Payload"/> holds the whole body.
    /// </summary>
    public sealed record UnknownEvent : DiditEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public UnknownEvent()
        {
        }

        internal UnknownEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }
}
