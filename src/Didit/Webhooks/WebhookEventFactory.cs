using System;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>Maps a verified webhook body to its typed event. Never throws: unexpected shapes read as <c>null</c>.</summary>
    internal static class WebhookEventFactory
    {
        public static DiditEvent Create(JsonElement payload, WebhookSignature verifiedBy) =>
            WebhookTypeOf(payload) switch
            {
                "status.updated" => new SessionStatusUpdatedEvent(payload, verifiedBy),
                "data.updated" => new SessionDataUpdatedEvent(payload, verifiedBy),
                "user.status.updated" => new UserStatusUpdatedEvent(payload, verifiedBy),
                "user.data.updated" => new UserDataUpdatedEvent(payload, verifiedBy),
                "business.status.updated" => new BusinessStatusUpdatedEvent(payload, verifiedBy),
                "business.data.updated" => new BusinessDataUpdatedEvent(payload, verifiedBy),
                "activity.created" => new ActivityCreatedEvent(payload, verifiedBy),
                "transaction.created" => new TransactionCreatedEvent(payload, verifiedBy),
                "transaction.status.updated" => new TransactionStatusUpdatedEvent(payload, verifiedBy),
                "travel_rule.status.updated" => new TravelRuleStatusUpdatedEvent(payload, verifiedBy),
                "workflow.compliance_changed" => new WorkflowComplianceChangedEvent(payload, verifiedBy),
                "kyb.registry_search.resolved" => new KybRegistrySearchResolvedEvent(payload, verifiedBy),
                "bank.link.completed" => new BankLinkCompletedEvent(payload, verifiedBy),
                "bank.link.cancelled" => new BankLinkCancelledEvent(payload, verifiedBy),
                "bank.link.expired" => new BankLinkExpiredEvent(payload, verifiedBy),
                "bank.link.failed" => new BankLinkFailedEvent(payload, verifiedBy),
                "bank.connection.created" => new BankConnectionCreatedEvent(payload, verifiedBy),
                "bank.connection.updated" => new BankConnectionUpdatedEvent(payload, verifiedBy),
                "bank.connection.consent_expiring" => new BankConnectionConsentExpiringEvent(payload, verifiedBy),
                "bank.connection.renewal_required" => new BankConnectionRenewalRequiredEvent(payload, verifiedBy),
                "bank.connection.renewed" => new BankConnectionRenewedEvent(payload, verifiedBy),
                "bank.connection.revoked" => new BankConnectionRevokedEvent(payload, verifiedBy),
                "bank.connection.deleted" => new BankConnectionDeletedEvent(payload, verifiedBy),
                "bank.connection.history_complete" => new BankConnectionHistoryCompleteEvent(payload, verifiedBy),
                "bank.data.updated" => new BankDataUpdatedEvent(payload, verifiedBy),
                "bank.sync.completed" => new BankSyncCompletedEvent(payload, verifiedBy),
                "bank.sync.failed" => new BankSyncFailedEvent(payload, verifiedBy),
                "bank.sync.recovered" => new BankSyncRecoveredEvent(payload, verifiedBy),
                _ => new UnknownEvent(payload, verifiedBy),
            };

        /// <summary>The event type: <c>webhook_type</c>, or <c>event_type</c> as the KYB registry search callback names it.</summary>
        public static string WebhookTypeOf(JsonElement payload) =>
            WebhookJson.GetString(payload, "webhook_type") ?? WebhookJson.GetString(payload, "event_type") ?? string.Empty;

        /// <summary>The session decision as the generated type, or <c>null</c> when it is absent or does not parse.</summary>
        public static SessionDecision? ReadDecision(JsonElement payload)
        {
            if (WebhookJson.GetObject(payload, "decision") is not { } decision)
            {
                return null;
            }

            try
            {
                return JsonSupport.Deserialize<SessionDecision>(decision.GetRawText());
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
            {
                return null;
            }
        }
    }
}
