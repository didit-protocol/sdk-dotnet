using System.Text.Json;
using Xunit;
using static Didit.Tests.Webhooks.WebhookTestSupport;

namespace Didit.Tests.Webhooks
{
    /// <summary>The typed events of DESIGN.md 9.4, with payloads shaped like the backend builds them.</summary>
    public sealed class WebhookEventTests
    {
        private static readonly string Envelope =
            "\"application_id\":\"app-1\",\"created_at\":1760702390,\"environment\":\"sandbox\",\"event_id\":\"evt-1\",\"timestamp\":" + Text(Timestamp);

        [Fact]
        public void SessionStatusUpdatedMapsEveryField()
        {
            var verified = SignAndVerify(Body(
                "status.updated",
                "\"session_id\":\"s-1\",\"status\":\"Approved\",\"workflow_id\":\"wf-1\",\"workflow_version\":3,\"vendor_data\":\"user-42\","
                + "\"metadata\":{\"plan\":\"pro\"},\"trigger\":\"manual_review\",\"sandbox_scenario\":null,"
                + "\"decision\":{\"session_id\":\"s-1\",\"status\":\"Approved\",\"id_verifications\":[]}"));

            var session = Assert.IsType<SessionStatusUpdatedEvent>(verified);
            Assert.Equal("evt-1", session.EventId);
            Assert.Equal("status.updated", session.WebhookType);
            Assert.Equal(Timestamp, session.Timestamp);
            Assert.Equal(1760702390, session.CreatedAt);
            Assert.Equal("app-1", session.ApplicationId);
            Assert.Equal(WebhookEnvironment.Sandbox, session.Environment);
            Assert.Equal("s-1", session.SessionId);
            Assert.Null(session.BusinessSessionId);
            Assert.Null(session.SessionKind);
            Assert.Equal("Approved", session.Status);
            Assert.Equal("wf-1", session.WorkflowId);
            Assert.Equal(3, session.WorkflowVersion);
            Assert.Equal("user-42", session.VendorData);
            Assert.Equal("pro", session.Metadata!.Value.GetProperty("plan").GetString());
            Assert.NotNull(session.Decision);
            Assert.Null(session.ResubmitInfo);
            Assert.Equal("manual_review", session.Payload.GetProperty("trigger").GetString());
            Assert.True(session.IsBodyAuthenticated);
        }

        [Fact]
        public void SessionDataUpdatedCarriesResubmitInfo()
        {
            var verified = SignAndVerify(Body(
                "data.updated",
                "\"session_id\":\"s-2\",\"status\":\"Resubmitted\",\"resubmit_info\":{\"nodes_to_resubmit\":[],\"reasons\":{}}"));

            var session = Assert.IsType<SessionDataUpdatedEvent>(verified);
            Assert.Equal("Resubmitted", session.Status);
            Assert.Equal(JsonValueKind.Object, session.ResubmitInfo!.Value.ValueKind);
            Assert.Null(session.Decision);
        }

        [Fact]
        public void BusinessSessionEventsAreSessionEvents()
        {
            var verified = SignAndVerify(Body(
                "status.updated",
                "\"session_id\":\"b-1\",\"business_session_id\":\"b-1\",\"session_kind\":\"business\",\"status\":\"In Review\","
                + "\"vendor_business_id\":\"vb-1\",\"decision\":{\"session_id\":\"b-1\",\"session_kind\":\"business\",\"registry_checks\":[]}"));

            var session = Assert.IsType<SessionStatusUpdatedEvent>(verified);
            Assert.Equal("b-1", session.BusinessSessionId);
            Assert.Equal("business", session.SessionKind);
            Assert.NotNull(session.Decision);
        }

        [Fact]
        public void UnreadableDecisionIsNull()
        {
            var notAnObject = SignAndVerify(Body("status.updated", "\"session_id\":\"s-1\",\"decision\":\"pending\""));
            var wrongShape = SignAndVerify(Body("status.updated", "\"session_id\":\"s-1\",\"decision\":{\"status\":{\"nested\":true}}"));

            Assert.Null(Assert.IsType<SessionStatusUpdatedEvent>(notAnObject).Decision);
            Assert.Null(Assert.IsType<SessionStatusUpdatedEvent>(wrongShape).Decision);
            Assert.Equal(JsonValueKind.Object, wrongShape.Payload.GetProperty("decision").ValueKind);
        }

        [Fact]
        public void UserEventsMapTheirFields()
        {
            var status = SignAndVerify(Body(
                "user.status.updated",
                "\"vendor_user_id\":\"vu-1\",\"vendor_data\":\"customer-1\",\"status\":\"Approved\",\"previous_status\":\"In Review\""));
            var data = SignAndVerify(Body(
                "user.data.updated",
                "\"vendor_user_id\":\"vu-1\",\"vendor_data\":null,\"status\":\"Approved\",\"changed_fields\":[\"full_name\"],\"changes\":{}"));

            var statusEvent = Assert.IsType<UserStatusUpdatedEvent>(status);
            Assert.Equal("vu-1", statusEvent.VendorUserId);
            Assert.Equal("customer-1", statusEvent.VendorData);
            Assert.Equal("Approved", statusEvent.Status);
            var dataEvent = Assert.IsType<UserDataUpdatedEvent>(data);
            Assert.Null(dataEvent.VendorData);
            Assert.Equal("full_name", dataEvent.Payload.GetProperty("changed_fields")[0].GetString());
        }

        [Fact]
        public void BusinessEventsMapTheirFields()
        {
            var status = SignAndVerify(Body(
                "business.status.updated",
                "\"vendor_business_id\":\"vb-1\",\"vendor_data\":\"company-1\",\"status\":\"Declined\""));
            var data = SignAndVerify(Body("business.data.updated", "\"vendor_business_id\":\"vb-1\",\"status\":\"Approved\""));

            var statusEvent = Assert.IsType<BusinessStatusUpdatedEvent>(status);
            Assert.Equal("vb-1", statusEvent.VendorBusinessId);
            Assert.Equal("company-1", statusEvent.VendorData);
            Assert.Equal("Declined", statusEvent.Status);
            Assert.Equal("vb-1", Assert.IsType<BusinessDataUpdatedEvent>(data).VendorBusinessId);
        }

        [Fact]
        public void TransactionEventsMapTheirFields()
        {
            var created = SignAndVerify(Body(
                "transaction.created",
                "\"transaction_id\":\"t-1\",\"txn_id\":\"txn_1\",\"status\":\"AWAITING_USER\",\"score\":75,\"severity\":\"high\","
                + "\"amount\":\"1500.000000000000000000\",\"currency\":\"USD\",\"direction\":\"inbound\","
                + "\"action_required\":{\"type\":\"wallet_ownership\",\"url\":\"https://example.com/w\"}"));
            var updated = SignAndVerify(Body(
                "transaction.status.updated",
                "\"transaction_id\":\"t-1\",\"amount\":1500.5,\"score\":12.5,\"action_required\":null"));

            var createdEvent = Assert.IsType<TransactionCreatedEvent>(created);
            Assert.Equal("t-1", createdEvent.TransactionId);
            Assert.Equal("txn_1", createdEvent.TxnId);
            Assert.Equal("AWAITING_USER", createdEvent.Status);
            Assert.Equal(75, createdEvent.Score);
            Assert.Equal("high", createdEvent.Severity);
            Assert.Equal("1500.000000000000000000", createdEvent.Amount);
            Assert.Equal("USD", createdEvent.Currency);
            Assert.Equal("inbound", createdEvent.Direction);
            Assert.Equal("wallet_ownership", createdEvent.ActionRequired!.Value.GetProperty("type").GetString());
            var updatedEvent = Assert.IsType<TransactionStatusUpdatedEvent>(updated);
            Assert.Equal("1500.5", updatedEvent.Amount);
            Assert.Equal(12.5, updatedEvent.Score);
            Assert.Null(updatedEvent.ActionRequired);
        }

        [Fact]
        public void TravelRuleStatusUpdatedMapsItsFields()
        {
            var verified = SignAndVerify(Body(
                "travel_rule.status.updated",
                "\"transaction_id\":\"t-2\",\"txn_id\":\"txn_2\",\"travel_rule_status\":\"COMPLETED\",\"rail\":\"\",\"direction\":\"outbound\""));

            var travelRule = Assert.IsType<TravelRuleStatusUpdatedEvent>(verified);
            Assert.Equal("t-2", travelRule.TransactionId);
            Assert.Equal("txn_2", travelRule.TxnId);
            Assert.Equal("COMPLETED", travelRule.TravelRuleStatus);
            Assert.Equal(string.Empty, travelRule.Rail);
            Assert.Equal("outbound", travelRule.Direction);
        }

        [Fact]
        public void ActivityCreatedKeepsItsFieldsInThePayload()
        {
            var verified = SignAndVerify(Body("activity.created", "\"activity_id\":\"a-1\",\"activity_type\":\"VERIFICATION_COMPLETED\""));

            var activity = Assert.IsType<ActivityCreatedEvent>(verified);
            Assert.Equal("a-1", activity.Payload.GetProperty("activity_id").GetString());
        }

        [Fact]
        public void WorkflowComplianceChangedMapsItsFields()
        {
            var verified = SignAndVerify(Body(
                "workflow.compliance_changed",
                "\"workflow_id\":\"wf-1\",\"workflow_version\":4,\"workflow_label\":\"Onboarding\","
                + "\"transition\":{\"from\":\"compliant\",\"to\":\"non_compliant\"},\"severity\":\"critical\",\"kb_version\":\"2026.10\""));

            var compliance = Assert.IsType<WorkflowComplianceChangedEvent>(verified);
            Assert.Equal("wf-1", compliance.WorkflowId);
            Assert.Equal(4, compliance.WorkflowVersion);
            Assert.Equal("Onboarding", compliance.WorkflowLabel);
            Assert.Equal("compliant", compliance.FromStatus);
            Assert.Equal("non_compliant", compliance.ToStatus);
            Assert.Equal("critical", compliance.Severity);
            Assert.Equal("2026.10", compliance.KbVersion);
        }

        [Fact]
        public void KybRegistrySearchResolvedIsNamedByEventType()
        {
            var body = "{\"created_at\":" + Text(Timestamp) + ",\"event_id\":\"evt-9\",\"event_type\":\"kyb.registry_search.resolved\","
                + "\"kyb_registry\":{\"companies\":[]},\"metadata\":{\"source\":\"onboarding\"},\"request_id\":\"r-1\","
                + "\"search_resolved\":true,\"search_status\":\"resolved\",\"timestamp\":" + Text(Timestamp) + ",\"vendor_data\":\"company-1234\"}";

            var verified = SignAndVerify(body);

            var search = Assert.IsType<KybRegistrySearchResolvedEvent>(verified);
            Assert.Equal("kyb.registry_search.resolved", search.WebhookType);
            Assert.Equal("r-1", search.RequestId);
            Assert.Equal("company-1234", search.VendorData);
            Assert.Equal("onboarding", search.Metadata!.Value.GetProperty("source").GetString());
            Assert.Equal("resolved", search.SearchStatus);
            Assert.True(search.SearchResolved);
            Assert.Equal(JsonValueKind.Array, search.KybRegistry!.Value.GetProperty("companies").ValueKind);
        }

        [Theory]
        [InlineData("transaction_rule_suggestions.created")]
        [InlineData("auth.login")]
        [InlineData("something.new")]
        public void UnknownTypesKeepTheRawPayload(string webhookType)
        {
            var verified = SignAndVerify(Body(webhookType, "\"run_id\":\"run-1\""));

            var unknown = Assert.IsType<UnknownEvent>(verified);
            Assert.Equal(webhookType, unknown.WebhookType);
            Assert.Equal("evt-1", unknown.EventId);
            Assert.Equal("run-1", unknown.Payload.GetProperty("run_id").GetString());
        }

        [Fact]
        public void MissingWebhookTypeIsUnknown()
        {
            var verified = SignAndVerify("{\"event_id\":\"evt-1\",\"timestamp\":" + Text(Timestamp) + "}");

            Assert.Equal(string.Empty, Assert.IsType<UnknownEvent>(verified).WebhookType);
        }

        [Fact]
        public void FieldsOfAnUnexpectedTypeReadAsNull()
        {
            var verified = SignAndVerify(
                "{\"application_id\":7,\"created_at\":\"2026-10-17T12:00:00Z\",\"environment\":false,\"event_id\":{},\"timestamp\":" + Text(Timestamp)
                + ",\"webhook_type\":\"status.updated\",\"session_id\":42,\"status\":[\"Approved\"],\"workflow_version\":\"3\","
                + "\"metadata\":null,\"decision\":null,\"resubmit_info\":null,\"vendor_data\":1.5}");

            var session = Assert.IsType<SessionStatusUpdatedEvent>(verified);
            Assert.Null(session.ApplicationId);
            Assert.Null(session.CreatedAt);
            Assert.Null(session.Environment);
            Assert.Null(session.EventId);
            Assert.Null(session.SessionId);
            Assert.Null(session.Status);
            Assert.Null(session.WorkflowVersion);
            Assert.Null(session.Metadata);
            Assert.Null(session.Decision);
            Assert.Null(session.ResubmitInfo);
            Assert.Null(session.VendorData);
        }

        [Fact]
        public void TransactionFieldsOfAnUnexpectedTypeReadAsNull()
        {
            var verified = SignAndVerify(Body("transaction.created", "\"score\":\"75\",\"amount\":true,\"txn_id\":5"));

            var transaction = Assert.IsType<TransactionCreatedEvent>(verified);
            Assert.Null(transaction.Score);
            Assert.Null(transaction.Amount);
            Assert.Null(transaction.TxnId);
        }

        [Fact]
        public void DuplicateKeysReadTheLastValue()
        {
            var verified = SignAndVerify(Body("status.updated", "\"status\":\"In Review\",\"status\":\"Approved\""));

            Assert.Equal("Approved", Assert.IsType<SessionStatusUpdatedEvent>(verified).Status);
        }

        [Fact]
        public void EventsCanBeBuiltForTests()
        {
            var built = new SessionStatusUpdatedEvent
            {
                SessionId = "s-1",
                Status = "Approved",
                VerifiedBy = WebhookSignature.Simple,
            };

            Assert.False(built.IsBodyAuthenticated);
            Assert.Equal(string.Empty, built.WebhookType);
            Assert.Equal(built with { }, built);
            Assert.True(new UnknownEvent().IsBodyAuthenticated);
        }

        [Fact]
        public void EnvironmentConstantsMatchTheWire()
        {
            Assert.Equal("live", WebhookEnvironment.Live);
            Assert.Equal("sandbox", WebhookEnvironment.Sandbox);
        }

        private static string Body(string webhookType, string fields) =>
            "{" + Envelope + ",\"webhook_type\":\"" + webhookType + "\"," + fields + "}";
    }
}
