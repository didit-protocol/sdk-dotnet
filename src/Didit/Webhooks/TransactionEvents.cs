using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>A transaction monitoring event: <see cref="TransactionCreatedEvent"/> or <see cref="TransactionStatusUpdatedEvent"/>.</summary>
    public abstract record TransactionEvent : DiditEvent
    {
        private protected TransactionEvent()
        {
        }

        private protected TransactionEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            TransactionId = WebhookJson.GetString(payload, "transaction_id");
            TxnId = WebhookJson.GetString(payload, "txn_id");
            Status = WebhookJson.GetString(payload, "status");
            Score = WebhookJson.GetDouble(payload, "score");
            Severity = WebhookJson.GetString(payload, "severity");
            Amount = WebhookJson.GetStringOrNumber(payload, "amount");
            Currency = WebhookJson.GetString(payload, "currency");
            Direction = WebhookJson.GetString(payload, "direction");
            ActionRequired = WebhookJson.GetElement(payload, "action_required");
        }

        /// <summary>Didit's id of the transaction (<c>transaction_id</c>).</summary>
        public string? TransactionId { get; init; }

        /// <summary>Your id of the transaction (<c>txn_id</c>).</summary>
        public string? TxnId { get; init; }

        /// <summary>The transaction status (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>The risk score (<c>score</c>).</summary>
        public double? Score { get; init; }

        /// <summary>The severity (<c>severity</c>), for example <c>low</c> or <c>high</c>.</summary>
        public string? Severity { get; init; }

        /// <summary>The amount as a decimal string (<c>amount</c>), never converted to a binary floating-point number.</summary>
        public string? Amount { get; init; }

        /// <summary>The currency (<c>currency</c>).</summary>
        public string? Currency { get; init; }

        /// <summary><c>inbound</c> or <c>outbound</c> (<c>direction</c>).</summary>
        public string? Direction { get; init; }

        /// <summary>
        /// What the end user must do before the transaction can proceed (<c>action_required</c>), for example a wallet
        /// ownership proof or a verification session; <c>null</c> when nothing is required.
        /// </summary>
        public JsonElement? ActionRequired { get; init; }
    }

    /// <summary><c>transaction.created</c>: a transaction was created and screened.</summary>
    public sealed record TransactionCreatedEvent : TransactionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public TransactionCreatedEvent()
        {
        }

        internal TransactionCreatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>transaction.status.updated</c>: a transaction's status changed.</summary>
    public sealed record TransactionStatusUpdatedEvent : TransactionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public TransactionStatusUpdatedEvent()
        {
        }

        internal TransactionStatusUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>travel_rule.status.updated</c>: the Travel Rule exchange of a transaction changed status.</summary>
    public sealed record TravelRuleStatusUpdatedEvent : DiditEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public TravelRuleStatusUpdatedEvent()
        {
        }

        internal TravelRuleStatusUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            TransactionId = WebhookJson.GetString(payload, "transaction_id");
            TxnId = WebhookJson.GetString(payload, "txn_id");
            TravelRuleStatus = WebhookJson.GetString(payload, "travel_rule_status");
            Rail = WebhookJson.GetString(payload, "rail");
            Direction = WebhookJson.GetString(payload, "direction");
        }

        /// <summary>Didit's id of the transaction (<c>transaction_id</c>).</summary>
        public string? TransactionId { get; init; }

        /// <summary>Your id of the transaction (<c>txn_id</c>).</summary>
        public string? TxnId { get; init; }

        /// <summary>The Travel Rule exchange status (<c>travel_rule_status</c>), for example <c>COMPLETED</c>.</summary>
        public string? TravelRuleStatus { get; init; }

        /// <summary>The Travel Rule rail (<c>rail</c>), for example <c>TRP</c>; empty when none was chosen.</summary>
        public string? Rail { get; init; }

        /// <summary><c>inbound</c> or <c>outbound</c> (<c>direction</c>).</summary>
        public string? Direction { get; init; }
    }
}
