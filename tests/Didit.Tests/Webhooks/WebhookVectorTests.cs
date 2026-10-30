using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Didit.Tests.Webhooks
{
    /// <summary>Runs every shared vector of <c>openapi/webhook-vectors.json</c> (DESIGN.md 9.3).</summary>
    public sealed class WebhookVectorTests
    {
        private static readonly Dictionary<string, Type> EventTypes = new(StringComparer.Ordinal)
        {
            ["status.updated"] = typeof(SessionStatusUpdatedEvent),
            ["data.updated"] = typeof(SessionDataUpdatedEvent),
            ["user.status.updated"] = typeof(UserStatusUpdatedEvent),
            ["user.data.updated"] = typeof(UserDataUpdatedEvent),
            ["business.status.updated"] = typeof(BusinessStatusUpdatedEvent),
            ["business.data.updated"] = typeof(BusinessDataUpdatedEvent),
            ["activity.created"] = typeof(ActivityCreatedEvent),
            ["transaction.created"] = typeof(TransactionCreatedEvent),
            ["transaction.status.updated"] = typeof(TransactionStatusUpdatedEvent),
            ["travel_rule.status.updated"] = typeof(TravelRuleStatusUpdatedEvent),
            ["workflow.compliance_changed"] = typeof(WorkflowComplianceChangedEvent),
        };

        public static TheoryData<string> AllVectors => Names(_ => true);

        public static TheoryData<string> ValidVectors => Names(vector => vector.Expect == "valid");

        public static TheoryData<string> RotationVectors =>
            Names(vector => vector.Expect == "valid" && vector.PreviousSecret is not null);

        public static TheoryData<string> ValidVectorsWithMatchingV2 => Names(vector =>
        {
            if (vector.Expect != "valid")
            {
                return false;
            }

            var expected = WebhookTestSupport.Hmac(vector.Secret, vector.CanonicalV2);
            return Header(vector, "X-Signature-V2") == expected || Header(vector, "X-Signature-V2-Previous") == expected;
        });

        [Fact]
        public void TheVectorFileHasEveryCase()
        {
            var expectations = WebhookVectors.All.Select(vector => vector.Expect).ToHashSet();

            Assert.Equal(TimeSpan.FromSeconds(300), WebhookVectors.Tolerance);
            Assert.Contains("valid", expectations);
            Assert.Contains("valid_only_with_allow_simple", expectations);
            Assert.Contains(WebhookVerificationReason.InvalidSignature, expectations);
            Assert.Contains(WebhookVerificationReason.MissingSignature, expectations);
            Assert.Contains(WebhookVerificationReason.TimestampMismatch, expectations);
            Assert.Contains(WebhookVerificationReason.TimestampOutOfTolerance, expectations);
        }

        [Theory]
        [MemberData(nameof(AllVectors))]
        public void CanonicalJsonEqualsTheSignedStringByteForByte(string name)
        {
            var vector = WebhookVectors.Get(name);

            var canonical = DiditWebhooks.Canonicalize(vector.Body);

            // A body altered after signing keeps the canonical_v2 that Didit signed, so the two must differ.
            var alteredAfterSigning = vector.Expect == WebhookVerificationReason.InvalidSignature
                && Header(vector, "X-Signature-V2") == WebhookTestSupport.Hmac(vector.Secret, vector.CanonicalV2);
            if (alteredAfterSigning)
            {
                Assert.NotEqual(vector.CanonicalV2, canonical);
                return;
            }

            Assert.Equal(Encoding.UTF8.GetBytes(vector.CanonicalV2), Encoding.UTF8.GetBytes(canonical));
        }

        [Theory]
        [MemberData(nameof(AllVectors))]
        public void CanonicalJsonOfTheParsedBodyIsTheSame(string name)
        {
            var vector = WebhookVectors.Get(name);
            using var document = JsonDocument.Parse(vector.Body);

            Assert.Equal(DiditWebhooks.Canonicalize(vector.Body), DiditWebhooks.Canonicalize(document.RootElement));
        }

        [Theory]
        [MemberData(nameof(AllVectors))]
        public void VerificationGivesTheExpectedOutcome(string name)
        {
            var vector = WebhookVectors.Get(name);

            switch (vector.Expect)
            {
                case "valid":
                    var verified = WebhookVectors.Verify(vector, allowSimple: false);
                    AssertTypedEvent(vector, verified);
                    Assert.True(verified.IsBodyAuthenticated);
                    Assert.Equal(WebhookSignature.RawBody, verified.VerifiedBy);
                    break;

                case "valid_only_with_allow_simple":
                    Assert.Equal(
                        WebhookVerificationReason.InvalidSignature,
                        WebhookTestSupport.Reason(() => WebhookVectors.Verify(vector, allowSimple: false)));
                    var simple = WebhookVectors.Verify(vector, allowSimple: true);
                    AssertTypedEvent(vector, simple);
                    Assert.False(simple.IsBodyAuthenticated);
                    Assert.Equal(WebhookSignature.Simple, simple.VerifiedBy);
                    break;

                default:
                    Assert.Equal(vector.Expect, WebhookTestSupport.Reason(() => WebhookVectors.Verify(vector, allowSimple: false)));
                    break;
            }
        }

        [Theory]
        [MemberData(nameof(ValidVectorsWithMatchingV2))]
        public void VerifyParsedAcceptsTheV2Signature(string name)
        {
            var vector = WebhookVectors.Get(name);
            using var document = JsonDocument.Parse(vector.Body);

            var verified = DiditWebhooks.VerifyCore(
                null,
                document.RootElement,
                WebhookTestSupport.Lookup(vector.Headers),
                new[] { vector.Secret },
                WebhookVectors.Tolerance,
                allowSimple: false,
                DateTimeOffset.FromUnixTimeSeconds(vector.Now));

            AssertTypedEvent(vector, verified);
            Assert.Equal(WebhookSignature.Canonical, verified.VerifiedBy);
            Assert.True(verified.IsBodyAuthenticated);
        }

        [Theory]
        [MemberData(nameof(ValidVectors))]
        public void ValidVectorsVerifyWithTheSecretAmongOthers(string name)
        {
            var vector = WebhookVectors.Get(name);

            var verified = WebhookVectors.Verify(vector, allowSimple: false, new[] { WebhookTestSupport.OtherSecret, vector.Secret });

            AssertTypedEvent(vector, verified);
        }

        [Theory]
        [MemberData(nameof(RotationVectors))]
        public void ReceiverStillOnThePreviousSecretVerifiesDuringARotation(string name)
        {
            var vector = WebhookVectors.Get(name);

            var verified = WebhookVectors.Verify(vector, allowSimple: false, new[] { vector.PreviousSecret! });

            AssertTypedEvent(vector, verified);
            Assert.Equal(WebhookSignature.RawBody, verified.VerifiedBy);
        }

        [Theory]
        [MemberData(nameof(ValidVectors))]
        public void ValidVectorsFailWithAnotherSecret(string name)
        {
            var vector = WebhookVectors.Get(name);

            Assert.Equal(
                WebhookVerificationReason.InvalidSignature,
                WebhookTestSupport.Reason(() => WebhookVectors.Verify(vector, allowSimple: true, new[] { WebhookTestSupport.OtherSecret })));
        }

        private static TheoryData<string> Names(Func<WebhookVector, bool> predicate)
        {
            var data = new TheoryData<string>();
            foreach (var vector in WebhookVectors.All.Where(predicate))
            {
                data.Add(vector.Name);
            }

            return data;
        }

        private static string? Header(WebhookVector vector, string name) =>
            vector.Headers.TryGetValue(name, out var value) ? value : null;

        private static void AssertTypedEvent(WebhookVector vector, DiditEvent verified)
        {
            using var document = JsonDocument.Parse(vector.Body);
            var body = document.RootElement;
            var webhookType = body.GetProperty("webhook_type").GetString()!;

            Assert.IsType(EventTypes.TryGetValue(webhookType, out var type) ? type : typeof(UnknownEvent), verified);
            Assert.Equal(Text(body, "event_id"), verified.EventId);
            Assert.Equal(webhookType, verified.WebhookType);
            Assert.Equal(body.GetProperty("timestamp").GetInt64(), verified.Timestamp);
            Assert.Equal(body.GetProperty("created_at").GetInt64(), verified.CreatedAt);
            Assert.Equal(Text(body, "application_id"), verified.ApplicationId);
            Assert.Equal(Text(body, "environment"), verified.Environment);
            Assert.Equal(DiditWebhooks.Canonicalize(body), DiditWebhooks.Canonicalize(verified.Payload));

            switch (verified)
            {
                case SessionEvent session:
                    Assert.Equal(Text(body, "session_id"), session.SessionId);
                    Assert.Equal(Text(body, "status"), session.Status);
                    Assert.Equal(Text(body, "vendor_data"), session.VendorData);
                    Assert.Equal(Text(body, "workflow_id"), session.WorkflowId);
                    break;
                case TransactionEvent transaction:
                    Assert.Equal(Text(body, "transaction_id"), transaction.TransactionId);
                    Assert.Equal(Text(body, "txn_id"), transaction.TxnId);
                    Assert.Equal(Text(body, "status"), transaction.Status);
                    Assert.Equal(Text(body, "amount"), transaction.Amount);
                    Assert.Equal(body.GetProperty("score").GetDouble(), transaction.Score);
                    Assert.Equal(Text(body, "currency"), transaction.Currency);
                    break;
                case UserEvent user:
                    Assert.Equal(Text(body, "vendor_user_id"), user.VendorUserId);
                    Assert.Equal(Text(body, "vendor_data"), user.VendorData);
                    Assert.Equal(Text(body, "status"), user.Status);
                    break;
                case BusinessEvent business:
                    Assert.Equal(Text(body, "vendor_business_id"), business.VendorBusinessId);
                    Assert.Equal(Text(body, "status"), business.Status);
                    break;
                case TravelRuleStatusUpdatedEvent travelRule:
                    Assert.Equal(Text(body, "transaction_id"), travelRule.TransactionId);
                    Assert.Equal(Text(body, "travel_rule_status"), travelRule.TravelRuleStatus);
                    Assert.Equal(Text(body, "rail"), travelRule.Rail);
                    Assert.Equal(Text(body, "direction"), travelRule.Direction);
                    break;
            }
        }

        private static string? Text(JsonElement body, string name) =>
            body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
