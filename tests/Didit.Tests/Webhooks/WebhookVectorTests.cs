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
            ["transaction.status.updated"] = typeof(TransactionStatusUpdatedEvent),
            ["travel_rule.status.updated"] = typeof(TravelRuleStatusUpdatedEvent),
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

            Assert.IsType(EventTypes[webhookType], verified);
            Assert.Equal(body.GetProperty("event_id").GetString(), verified.EventId);
            Assert.Equal(webhookType, verified.WebhookType);
            Assert.Equal(body.GetProperty("timestamp").GetInt64(), verified.Timestamp);
            Assert.Equal(body.GetProperty("created_at").GetInt64(), verified.CreatedAt);
            Assert.Equal(body.GetProperty("application_id").GetString(), verified.ApplicationId);
            Assert.Equal(WebhookEnvironment.Live, verified.Environment);
            Assert.Equal(DiditWebhooks.Canonicalize(body), DiditWebhooks.Canonicalize(verified.Payload));

            switch (verified)
            {
                case SessionEvent session:
                    Assert.Equal(body.GetProperty("session_id").GetString(), session.SessionId);
                    Assert.Equal(body.GetProperty("status").GetString(), session.Status);
                    break;
                case TransactionEvent transaction:
                    Assert.Equal(body.GetProperty("transaction_id").GetString(), transaction.TransactionId);
                    Assert.Equal(body.GetProperty("txn_id").GetString(), transaction.TxnId);
                    Assert.Equal("1500.00", transaction.Amount);
                    Assert.Equal(12.5, transaction.Score);
                    break;
                case UserEvent user:
                    Assert.Equal(body.GetProperty("vendor_user_id").GetString(), user.VendorUserId);
                    Assert.Equal(body.GetProperty("vendor_data").GetString(), user.VendorData);
                    break;
                case TravelRuleStatusUpdatedEvent travelRule:
                    Assert.Equal(body.GetProperty("transaction_id").GetString(), travelRule.TransactionId);
                    Assert.Equal("COMPLETED", travelRule.TravelRuleStatus);
                    Assert.Equal("TRP", travelRule.Rail);
                    Assert.Equal("inbound", travelRule.Direction);
                    break;
                default:
                    Assert.Fail("Unexpected event type " + verified.GetType().Name);
                    break;
            }
        }
    }
}
