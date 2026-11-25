using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>A Bank Link event: <c>bank.link.completed</c>, <c>bank.link.cancelled</c>, <c>bank.link.expired</c> or <c>bank.link.failed</c>.</summary>
    public abstract record BankLinkEvent : DiditEvent
    {
        private protected BankLinkEvent()
        {
        }

        private protected BankLinkEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            if (WebhookJson.GetObject(payload, "data") is not { } data)
            {
                return;
            }

            LinkId = WebhookJson.GetString(data, "link_id");
            Status = WebhookJson.GetString(data, "status");
            ConnectionId = WebhookJson.GetString(data, "connection_id");
            RenewsConnectionId = WebhookJson.GetString(data, "renews_connection_id");
            CheckId = WebhookJson.GetString(data, "check_id");
            ErrorCode = WebhookJson.GetString(data, "error_code");
        }

        /// <summary>The link id (<c>data.link_id</c>).</summary>
        public string? LinkId { get; init; }

        /// <summary>The link's final status (<c>data.status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>The connection the link created or renewed, when there is one (<c>data.connection_id</c>).</summary>
        public string? ConnectionId { get; init; }

        /// <summary>The connection targeted when the renewal link was created (<c>data.renews_connection_id</c>).</summary>
        public string? RenewsConnectionId { get; init; }

        /// <summary>The ownership check id, when the link asked for one (<c>data.check_id</c>).</summary>
        public string? CheckId { get; init; }

        /// <summary>Why the link failed, with <c>bank.link.failed</c> (<c>data.error_code</c>).</summary>
        public string? ErrorCode { get; init; }
    }

    /// <summary><c>bank.link.completed</c>: the person connected a bank or renewed a connection.</summary>
    public sealed record BankLinkCompletedEvent : BankLinkEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankLinkCompletedEvent()
        {
        }

        internal BankLinkCompletedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>bank.link.cancelled</c>: the person cancelled the link.</summary>
    public sealed record BankLinkCancelledEvent : BankLinkEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankLinkCancelledEvent()
        {
        }

        internal BankLinkCancelledEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>bank.link.expired</c>: the link's hosted page expired before it completed.</summary>
    public sealed record BankLinkExpiredEvent : BankLinkEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankLinkExpiredEvent()
        {
        }

        internal BankLinkExpiredEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>bank.link.failed</c>: the link ended in a failure; <see cref="BankLinkEvent.ErrorCode"/> says why.</summary>
    public sealed record BankLinkFailedEvent : BankLinkEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankLinkFailedEvent()
        {
        }

        internal BankLinkFailedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary>A connection event: <c>bank.connection.*</c>. Its data fields sit at the top level of the body.</summary>
    public abstract record BankConnectionEvent : DiditEvent
    {
        private protected BankConnectionEvent()
        {
        }

        private protected BankConnectionEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            ConnectionId = WebhookJson.GetString(payload, "connection_id");
        }

        /// <summary>The connection id (<c>connection_id</c>).</summary>
        public string? ConnectionId { get; init; }
    }

    /// <summary><c>bank.connection.created</c>: the first sync finished and the connection's accounts are readable.</summary>
    public sealed record BankConnectionCreatedEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionCreatedEvent()
        {
        }

        internal BankConnectionCreatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            InitialStatus = WebhookJson.GetString(payload, "initial_status");
        }

        /// <summary>How the first sync ended: <c>succeeded</c> or <c>partial</c> (<c>initial_status</c>).</summary>
        public string? InitialStatus { get; init; }
    }

    /// <summary><c>bank.connection.updated</c>: a connection status changed that no other event reports.</summary>
    public sealed record BankConnectionUpdatedEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionUpdatedEvent()
        {
        }

        internal BankConnectionUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
            StatusReason = WebhookJson.GetString(payload, "status_reason");
            EndsAt = BankEventReader.ReadDateTime(payload, "ends_at");
            RevocationStatus = WebhookJson.GetString(payload, "revocation_status");
        }

        /// <summary>The connection status (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>Why the status changed, such as <c>insufficient_balance</c> or <c>first_sync_failed</c> (<c>status_reason</c>).</summary>
        public string? StatusReason { get; init; }

        /// <summary>When the connection is scheduled to end, if known (<c>ends_at</c>).</summary>
        public DateTimeOffset? EndsAt { get; init; }

        /// <summary><c>pending</c> while a revocation waits for the bank to confirm it (<c>revocation_status</c>).</summary>
        public string? RevocationStatus { get; init; }
    }

    /// <summary><c>bank.connection.consent_expiring</c>: the consent ends soon.</summary>
    public sealed record BankConnectionConsentExpiringEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionConsentExpiringEvent()
        {
        }

        internal BankConnectionConsentExpiringEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            DaysLeft = WebhookJson.GetInt64(payload, "days_left");
            ConsentExpiresAt = BankEventReader.ReadDateTime(payload, "consent_expires_at");
            RenewBy = BankEventReader.ReadDateTime(payload, "renew_by");
        }

        /// <summary>Days left before the consent ends: 14, 7 or 1 (<c>days_left</c>).</summary>
        public long? DaysLeft { get; init; }

        /// <summary>When the consent ends (<c>consent_expires_at</c>).</summary>
        public DateTimeOffset? ConsentExpiresAt { get; init; }

        /// <summary>The end of the consent: renew before it for no gap (<c>renew_by</c>).</summary>
        public DateTimeOffset? RenewBy { get; init; }
    }

    /// <summary><c>bank.connection.renewal_required</c>: the consent ended or the bank asks the person to sign in again.</summary>
    public sealed record BankConnectionRenewalRequiredEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionRenewalRequiredEvent()
        {
        }

        internal BankConnectionRenewalRequiredEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
            Reason = WebhookJson.GetString(payload, "reason");
            RenewBy = BankEventReader.ReadDateTime(payload, "renew_by");
            RenewableUntil = BankEventReader.ReadDateTime(payload, "renewable_until");
        }

        /// <summary>The connection status (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>Why renewal is needed (<c>reason</c>).</summary>
        public string? Reason { get; init; }

        /// <summary>The end of the consent, when there is one (<c>renew_by</c>).</summary>
        public DateTimeOffset? RenewBy { get; init; }

        /// <summary>The last moment the same connection can be revived (<c>renewable_until</c>).</summary>
        public DateTimeOffset? RenewableUntil { get; init; }
    }

    /// <summary><c>bank.connection.renewed</c>: a renewal completed and the connection is active again.</summary>
    public sealed record BankConnectionRenewedEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionRenewedEvent()
        {
        }

        internal BankConnectionRenewedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
            ConsentVersion = WebhookJson.GetInt64(payload, "consent_version");
            LinkId = WebhookJson.GetString(payload, "link_id");
            ConsentExpiresAt = BankEventReader.ReadDateTime(payload, "consent_expires_at");
        }

        /// <summary>The connection status (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>The consent version the renewal created (<c>consent_version</c>).</summary>
        public long? ConsentVersion { get; init; }

        /// <summary>The renew link that completed, when there is one (<c>link_id</c>).</summary>
        public string? LinkId { get; init; }

        /// <summary>When the renewed consent ends (<c>consent_expires_at</c>).</summary>
        public DateTimeOffset? ConsentExpiresAt { get; init; }
    }

    /// <summary><c>bank.connection.revoked</c>: access ended and the bank confirmed it.</summary>
    public sealed record BankConnectionRevokedEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionRevokedEvent()
        {
        }

        internal BankConnectionRevokedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
            EndedBy = WebhookJson.GetString(payload, "ended_by");
            StatusReason = WebhookJson.GetString(payload, "status_reason");
            EndedAt = BankEventReader.ReadDateTime(payload, "ended_at");
            RetainUntil = BankEventReader.ReadDateTime(payload, "retain_until");
        }

        /// <summary>Consent status once access is terminated (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>Who ended access, including <c>system</c> when Didit ended the connection (<c>ended_by</c>).</summary>
        public string? EndedBy { get; init; }

        /// <summary>Why access ended, including <c>unpaid</c> for an unpaid coming month or <c>collection_complete</c> for a completed one-time read (<c>status_reason</c>).</summary>
        public string? StatusReason { get; init; }

        /// <summary>When access ended (<c>ended_at</c>).</summary>
        public DateTimeOffset? EndedAt { get; init; }

        /// <summary>How long the stored data stays readable (<c>retain_until</c>).</summary>
        public DateTimeOffset? RetainUntil { get; init; }
    }

    /// <summary><c>bank.connection.deleted</c>: the stored data of a connection was deleted.</summary>
    public sealed record BankConnectionDeletedEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionDeletedEvent()
        {
        }

        internal BankConnectionDeletedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
            ErasureStatus = WebhookJson.GetString(payload, "erasure_status");
            Reason = WebhookJson.GetString(payload, "reason");
            ErasedAt = BankEventReader.ReadDateTime(payload, "erased_at");
        }

        /// <summary>The connection status (<c>status</c>).</summary>
        public string? Status { get; init; }

        /// <summary>How the deletion ended: <c>erased</c> when it finished (<c>erasure_status</c>).</summary>
        public string? ErasureStatus { get; init; }

        /// <summary>Why the connection was deleted (<c>reason</c>).</summary>
        public string? Reason { get; init; }

        /// <summary>When the deletion finished (<c>erased_at</c>).</summary>
        public DateTimeOffset? ErasedAt { get; init; }
    }

    /// <summary><c>bank.connection.history_complete</c>: the history backfill of a connection ended.</summary>
    public sealed record BankConnectionHistoryCompleteEvent : BankConnectionEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankConnectionHistoryCompleteEvent()
        {
        }

        internal BankConnectionHistoryCompleteEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Status = WebhookJson.GetString(payload, "status");
        }

        /// <summary><c>partial</c> when the bank could not deliver every requested range (<c>status</c>).</summary>
        public string? Status { get; init; }
    }

    /// <summary>A sync event: <c>bank.data.updated</c>, <c>bank.sync.completed</c>, <c>bank.sync.failed</c> or <c>bank.sync.recovered</c>.</summary>
    public abstract record BankSyncEvent : DiditEvent
    {
        private protected BankSyncEvent()
        {
        }

        private protected BankSyncEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            ConnectionId = WebhookJson.GetString(payload, "connection_id");
            SyncId = WebhookJson.GetString(payload, "sync_id");
        }

        /// <summary>The connection id (<c>connection_id</c>).</summary>
        public string? ConnectionId { get; init; }

        /// <summary>The sync run id (<c>sync_id</c>).</summary>
        public string? SyncId { get; init; }
    }

    /// <summary>Transaction changes in one account, from <see cref="BankDataUpdatedEvent.Accounts"/>.</summary>
    public sealed record BankDataUpdatedAccount
    {
        /// <summary>The account id (<c>account_id</c>).</summary>
        public string? AccountId { get; init; }

        /// <summary>Transactions added (<c>added</c>).</summary>
        public long? Added { get; init; }

        /// <summary>Transactions modified (<c>modified</c>).</summary>
        public long? Modified { get; init; }

        /// <summary>Transactions removed (<c>removed</c>).</summary>
        public long? Removed { get; init; }
    }

    /// <summary><c>bank.data.updated</c>: a sync stored changes, or the first read published the connection's accounts.</summary>
    public sealed record BankDataUpdatedEvent : BankSyncEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankDataUpdatedEvent()
        {
        }

        internal BankDataUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Trigger = WebhookJson.GetString(payload, "trigger");
            BalancesChanged = WebhookJson.GetBoolean(payload, "balances_changed");
            Accounts = BankEventReader.ReadAccounts(payload);
        }

        /// <summary>What started the sync: <c>initial</c>, <c>scheduled</c> or <c>requested</c> (<c>trigger</c>).</summary>
        public string? Trigger { get; init; }

        /// <summary>The accounts whose transactions changed, with counts (<c>accounts</c>).</summary>
        public IReadOnlyList<BankDataUpdatedAccount>? Accounts { get; init; }

        /// <summary>Whether any balance changed (<c>balances_changed</c>).</summary>
        public bool? BalancesChanged { get; init; }
    }

    /// <summary><c>bank.sync.completed</c>: a sync finished.</summary>
    public sealed record BankSyncCompletedEvent : BankSyncEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankSyncCompletedEvent()
        {
        }

        internal BankSyncCompletedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            Trigger = WebhookJson.GetString(payload, "trigger");
            Outcome = WebhookJson.GetString(payload, "outcome");
            Changed = WebhookJson.GetBoolean(payload, "changed");
        }

        /// <summary>What started the sync (<c>trigger</c>).</summary>
        public string? Trigger { get; init; }

        /// <summary>How the sync ended (<c>outcome</c>).</summary>
        public string? Outcome { get; init; }

        /// <summary>Whether the sync stored changes (<c>changed</c>).</summary>
        public bool? Changed { get; init; }
    }

    /// <summary><c>bank.sync.failed</c>: syncs have been failing for 24 hours.</summary>
    public sealed record BankSyncFailedEvent : BankSyncEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankSyncFailedEvent()
        {
        }

        internal BankSyncFailedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            ErrorCode = WebhookJson.GetString(payload, "error_code");
            FailingSince = BankEventReader.ReadDateTime(payload, "failing_since");
        }

        /// <summary>Didit error code of the failure (<c>error_code</c>).</summary>
        public string? ErrorCode { get; init; }

        /// <summary>When the syncs started to fail (<c>failing_since</c>).</summary>
        public DateTimeOffset? FailingSince { get; init; }
    }

    /// <summary><c>bank.sync.recovered</c>: syncs work again after a failure.</summary>
    public sealed record BankSyncRecoveredEvent : BankSyncEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BankSyncRecoveredEvent()
        {
        }

        internal BankSyncRecoveredEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    internal static class BankEventReader
    {
        public static DateTimeOffset? ReadDateTime(JsonElement payload, string name)
        {
            var text = WebhookJson.GetString(payload, name);
            return text is not null
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                    ? value
                    : null;
        }

        public static IReadOnlyList<BankDataUpdatedAccount>? ReadAccounts(JsonElement payload)
        {
            if (WebhookJson.GetElement(payload, "accounts") is not { ValueKind: JsonValueKind.Array } accounts)
            {
                return null;
            }

            var result = new List<BankDataUpdatedAccount>();
            foreach (var account in accounts.EnumerateArray())
            {
                if (account.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(new BankDataUpdatedAccount
                {
                    AccountId = WebhookJson.GetString(account, "account_id"),
                    Added = WebhookJson.GetInt64(account, "added"),
                    Modified = WebhookJson.GetInt64(account, "modified"),
                    Removed = WebhookJson.GetInt64(account, "removed"),
                });
            }

            return result;
        }
    }
}
