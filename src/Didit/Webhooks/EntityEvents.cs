using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>A user entity event: <see cref="UserStatusUpdatedEvent"/> or <see cref="UserDataUpdatedEvent"/>.</summary>
    public abstract record UserEvent : DiditEvent
    {
        private protected UserEvent()
        {
        }

        private protected UserEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            VendorUserId = WebhookJson.GetString(payload, "vendor_user_id");
            VendorData = WebhookJson.GetString(payload, "vendor_data");
            Status = WebhookJson.GetString(payload, "status");
        }

        /// <summary>Didit's id of the user (<c>vendor_user_id</c>).</summary>
        public string? VendorUserId { get; init; }

        /// <summary>Your identifier for the user (<c>vendor_data</c>).</summary>
        public string? VendorData { get; init; }

        /// <summary>The user's status (<c>status</c>).</summary>
        public string? Status { get; init; }
    }

    /// <summary><c>user.status.updated</c>: a user's status changed.</summary>
    public sealed record UserStatusUpdatedEvent : UserEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public UserStatusUpdatedEvent()
        {
        }

        internal UserStatusUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>user.data.updated</c>: a user's data changed.</summary>
    public sealed record UserDataUpdatedEvent : UserEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public UserDataUpdatedEvent()
        {
        }

        internal UserDataUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary>A business entity event: <see cref="BusinessStatusUpdatedEvent"/> or <see cref="BusinessDataUpdatedEvent"/>.</summary>
    public abstract record BusinessEvent : DiditEvent
    {
        private protected BusinessEvent()
        {
        }

        private protected BusinessEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
            VendorBusinessId = WebhookJson.GetString(payload, "vendor_business_id");
            VendorData = WebhookJson.GetString(payload, "vendor_data");
            Status = WebhookJson.GetString(payload, "status");
        }

        /// <summary>Didit's id of the business (<c>vendor_business_id</c>).</summary>
        public string? VendorBusinessId { get; init; }

        /// <summary>Your identifier for the business (<c>vendor_data</c>).</summary>
        public string? VendorData { get; init; }

        /// <summary>The business's status (<c>status</c>).</summary>
        public string? Status { get; init; }
    }

    /// <summary><c>business.status.updated</c>: a business's status changed.</summary>
    public sealed record BusinessStatusUpdatedEvent : BusinessEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BusinessStatusUpdatedEvent()
        {
        }

        internal BusinessStatusUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }

    /// <summary><c>business.data.updated</c>: a business's data changed.</summary>
    public sealed record BusinessDataUpdatedEvent : BusinessEvent
    {
        /// <summary>Creates an empty event, for tests.</summary>
        public BusinessDataUpdatedEvent()
        {
        }

        internal BusinessDataUpdatedEvent(JsonElement payload, WebhookSignature verifiedBy)
            : base(payload, verifiedBy)
        {
        }
    }
}
