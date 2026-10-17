using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using static Didit.Tests.Webhooks.WebhookTestSupport;

namespace Didit.Tests.Webhooks
{
    /// <summary>The verification rules of DESIGN.md 9.3.</summary>
    public sealed class WebhookVerificationTests
    {
        private static readonly string[] SignatureHeaders =
        {
            "X-Signature", "X-Signature-V2", "X-Signature-Simple",
            "X-Signature-Previous", "X-Signature-V2-Previous", "X-Signature-Simple-Previous",
        };

        [Fact]
        public void DefaultToleranceIsFiveMinutes()
        {
            Assert.Equal(TimeSpan.FromSeconds(300), DiditWebhooks.DefaultTolerance);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptySecretIsAConfigurationError(string? secret)
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);

            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify(body, headers, secret!));
            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify(Encoding.UTF8.GetBytes(body), Lookup(headers), secret!));
            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify(body, headers, new[] { Secret, secret! }));
        }

        [Fact]
        public void NoSecretsIsAConfigurationError()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);

            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify(body, headers, Array.Empty<string>()));
            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify(body, headers, (IEnumerable<string>)null!));
            Assert.Throws<ConfigurationException>(() => VerifyAt(body, headers, secrets: Array.Empty<string>()));
        }

        [Fact]
        public void ConfigurationErrorComesBeforeAnythingElse()
        {
            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify("not json", new Dictionary<string, string>(), string.Empty));
            Assert.Throws<ConfigurationException>(() => DiditWebhooks.Verify((string)null!, (Func<string, string?>)null!, string.Empty));
        }

        [Fact]
        public void NullArgumentsThrow()
        {
            var headers = new Dictionary<string, string>();

            Assert.Throws<ArgumentNullException>(() => DiditWebhooks.Verify((string)null!, headers, Secret));
            Assert.Throws<ArgumentNullException>(() => DiditWebhooks.Verify((byte[])null!, headers, Secret));
            Assert.Throws<ArgumentNullException>(() => DiditWebhooks.Verify(SessionBody(), (IEnumerable<KeyValuePair<string, string>>)null!, Secret));
            Assert.Throws<ArgumentNullException>(() => DiditWebhooks.Verify(SessionBody(), (Func<string, string?>)null!, Secret));
        }

        [Fact]
        public void NegativeToleranceThrows()
        {
            var body = SessionBody();

            Assert.Throws<ArgumentOutOfRangeException>(() => VerifyAt(body, SignedHeaders(body), tolerance: TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void AcceptsAnyOfSeveralSecrets()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body, OtherSecret);

            var verified = VerifyAt(body, headers, secrets: new[] { Secret, OtherSecret });

            Assert.IsType<SessionStatusUpdatedEvent>(verified);
            Assert.Equal(WebhookSignature.RawBody, verified.VerifiedBy);
        }

        [Fact]
        public void PreviousSignatureHeadersVerifyTheReplacedSecret()
        {
            var body = SessionBody();
            var current = SignedHeaders(body, OtherSecret);
            var previous = SignedHeaders(body, Secret);
            var headers = new Dictionary<string, string>(current)
            {
                ["X-Signature-Previous"] = previous["X-Signature"],
                ["X-Signature-V2-Previous"] = previous["X-Signature-V2"],
                ["X-Signature-Simple-Previous"] = previous["X-Signature-Simple"],
            };

            Assert.Equal(WebhookSignature.RawBody, VerifyAt(body, headers).VerifiedBy);
            Assert.Equal(WebhookSignature.Canonical, VerifyParsedAt(body, headers).VerifiedBy);

            headers.Remove("X-Signature");
            headers.Remove("X-Signature-Previous");
            headers.Remove("X-Signature-V2");
            headers.Remove("X-Signature-V2-Previous");
            Assert.Equal(WebhookSignature.Simple, VerifyAt(body, headers, allowSimple: true).VerifiedBy);
        }

        [Fact]
        public void MalformedSignaturesAreInvalidNotMissing()
        {
            var body = SessionBody();
            var headers = new Dictionary<string, string>
            {
                ["X-Signature"] = "zz",
                ["X-Signature-V2"] = "not-hex",
                ["X-Timestamp"] = Text(Timestamp),
            };

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void UppercaseHexIsRejected()
        {
            var body = SessionBody();
            var signed = SignedHeaders(body);
            var headers = new Dictionary<string, string>
            {
                ["X-Signature"] = signed["X-Signature"].ToUpperInvariant(),
                ["X-Signature-V2"] = signed["X-Signature-V2"].ToUpperInvariant(),
                ["X-Timestamp"] = Text(Timestamp),
            };

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyAt(body, headers)));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        public void SignaturesOfTheWrongLengthAreRejected(int lengthChange)
        {
            var body = SessionBody();
            var signature = SignedHeaders(body)["X-Signature"];
            var wrongLength = lengthChange < 0 ? signature.Substring(1) : signature + "0";
            var headers = new Dictionary<string, string> { ["X-Signature"] = wrongLength };

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void NoSignatureHeaderIsMissingSignature()
        {
            var body = SessionBody();
            var headers = new Dictionary<string, string> { ["X-Timestamp"] = Text(Timestamp) };

            Assert.Equal(WebhookVerificationReason.MissingSignature, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void BlankSignatureHeadersCountAsMissing()
        {
            var body = SessionBody();
            var headers = SignatureHeaders.ToDictionary(name => name, _ => " ");

            Assert.Equal(WebhookVerificationReason.MissingSignature, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void TimestampHeaderMustMatchTheBody()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);

            headers["X-Timestamp"] = Text(Timestamp + 5);
            Assert.Equal(WebhookVerificationReason.TimestampMismatch, Reason(() => VerifyAt(body, headers)));

            headers["X-Timestamp"] = "not-a-number";
            Assert.Equal(WebhookVerificationReason.TimestampMismatch, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void TimestampHeaderIsComparedNumerically()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);
            headers["X-Timestamp"] = "0" + Text(Timestamp);

            Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, headers));
        }

        [Fact]
        public void TimestampHeaderIsOptional()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);
            headers.Remove("X-Timestamp");

            Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, headers));
        }

        [Theory]
        [InlineData("{\"session_id\":\"s-1\",\"webhook_type\":\"status.updated\"}")]
        [InlineData("{\"timestamp\":null,\"webhook_type\":\"status.updated\"}")]
        [InlineData("{\"timestamp\":\"1760702400\",\"webhook_type\":\"status.updated\"}")]
        [InlineData("{\"timestamp\":1760702400.5,\"webhook_type\":\"status.updated\"}")]
        [InlineData("{\"timestamp\":1760702400.0,\"webhook_type\":\"status.updated\"}")]
        [InlineData("{\"timestamp\":true,\"webhook_type\":\"status.updated\"}")]
        public void BodyWithoutAnIntegerTimestampIsRejected(string body)
        {
            var headers = new Dictionary<string, string> { ["X-Signature"] = Hmac(Secret, body) };

            Assert.Equal(WebhookVerificationReason.MissingTimestamp, Reason(() => VerifyAt(body, headers)));
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("42")]
        [InlineData("null")]
        [InlineData("not json")]
        [InlineData("{\"timestamp\":1760702400")]
        [InlineData("")]
        public void BodyThatIsNotAJsonObjectIsRejected(string body)
        {
            var headers = new Dictionary<string, string> { ["X-Signature"] = Hmac(Secret, body) };

            Assert.Equal(WebhookVerificationReason.InvalidPayload, Reason(() => VerifyAt(body, headers)));
        }

        [Fact]
        public void ParsedPayloadThatIsNotAnObjectIsRejected()
        {
            using var document = JsonDocument.Parse("[1,2]");

            Assert.Equal(
                WebhookVerificationReason.InvalidPayload,
                Reason(() => DiditWebhooks.VerifyParsed(document.RootElement, new Dictionary<string, string>(), Secret)));
        }

        [Theory]
        [InlineData(300, true)]
        [InlineData(-300, true)]
        [InlineData(301, false)]
        [InlineData(-301, false)]
        public void TimestampMustBeWithinTheTolerance(long offset, bool accepted)
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);

            if (accepted)
            {
                Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, headers, now: Timestamp + offset));
            }
            else
            {
                Assert.Equal(WebhookVerificationReason.TimestampOutOfTolerance, Reason(() => VerifyAt(body, headers, now: Timestamp + offset)));
            }
        }

        [Fact]
        public void ToleranceCanBeChanged()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);

            Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, headers, now: Timestamp + 900, tolerance: TimeSpan.FromMinutes(15)));
            Assert.Equal(
                WebhookVerificationReason.TimestampOutOfTolerance,
                Reason(() => VerifyAt(body, headers, now: Timestamp + 11, tolerance: TimeSpan.FromSeconds(10))));
        }

        [Fact]
        public void TimestampIsCheckedBeforeSignatures()
        {
            var body = SessionBody();
            var headers = new Dictionary<string, string> { ["X-Signature"] = Hmac(OtherSecret, body) };

            Assert.Equal(WebhookVerificationReason.TimestampOutOfTolerance, Reason(() => VerifyAt(body, headers, now: Timestamp + 1000)));
        }

        [Fact]
        public void HeaderNamesAreCaseInsensitive()
        {
            var body = SessionBody();
            var lower = SignedHeaders(body).ToDictionary(header => header.Key.ToLowerInvariant(), header => header.Value, StringComparer.Ordinal);
            var upper = SignedHeaders(body).ToDictionary(header => header.Key.ToUpperInvariant(), header => header.Value, StringComparer.Ordinal);

            Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, lower));
            Assert.IsType<SessionStatusUpdatedEvent>(VerifyAt(body, upper));

            // A case-sensitive lookup of lowercase names still works through the getHeader adapter.
            var verified = DiditWebhooks.VerifyCore(
                Encoding.UTF8.GetBytes(body),
                null,
                name => lower.TryGetValue(name, out var value) ? value : null,
                new[] { Secret },
                Tolerance,
                allowSimple: false,
                DateTimeOffset.FromUnixTimeSeconds(Now));
            Assert.IsType<SessionStatusUpdatedEvent>(verified);
        }

        [Fact]
        public void HeaderEnumerableIsMatchedCaseInsensitively()
        {
            var body = SessionBody(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var headers = SignedHeaders(body)
                .Select(header => new KeyValuePair<string, string>(header.Key.ToUpperInvariant(), header.Value))
                .ToList();

            Assert.IsType<SessionStatusUpdatedEvent>(DiditWebhooks.Verify(body, headers, Secret));
        }

        [Fact]
        public void SimpleSignatureCountsOnlyWhenAllowed()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);
            headers.Remove("X-Signature");
            headers.Remove("X-Signature-V2");

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyAt(body, headers)));

            var verified = VerifyAt(body, headers, allowSimple: true);
            Assert.Equal(WebhookSignature.Simple, verified.VerifiedBy);
            Assert.False(verified.IsBodyAuthenticated);
        }

        [Fact]
        public void SimpleSignatureUsesPythonStringsForMissingAndNullFields()
        {
            var body = "{\"status\":null,\"timestamp\":" + Text(Timestamp) + ",\"webhook_type\":\"user.status.updated\"}";
            var headers = new Dictionary<string, string>
            {
                ["X-Signature-Simple"] = Hmac(Secret, Text(Timestamp) + "::None:user.status.updated"),
            };

            using var document = JsonDocument.Parse(body);
            Assert.Equal(Text(Timestamp) + "::None:user.status.updated", DiditWebhooks.SimpleMessage(document.RootElement, Timestamp));
            Assert.IsType<UserStatusUpdatedEvent>(VerifyAt(body, headers, allowSimple: true));
        }

        [Fact]
        public void RawBodySignatureIsPreferred()
        {
            var body = SessionBody();

            var verified = VerifyAt(body, SignedHeaders(body), allowSimple: true);

            Assert.Equal(WebhookSignature.RawBody, verified.VerifiedBy);
            Assert.True(verified.IsBodyAuthenticated);
        }

        [Fact]
        public void BodyReEncodedByMiddlewareVerifiesOnTheCanonicalSignature()
        {
            // Didit sends non-ASCII as \u escapes; a proxy that re-serialises the JSON writes the characters instead.
            var sent = "{\"session_id\":\"s-1\",\"status\":\"Approved\",\"timestamp\":" + Text(Timestamp)
                + ",\"vendor_data\":\"Jos\\u00e9 \\ud83d\\ude00\",\"webhook_type\":\"status.updated\"}";
            var headers = SignedHeaders(sent);
            var reEncoded = "{\"webhook_type\": \"status.updated\", \"vendor_data\": \"José \U0001F600\", \"timestamp\": " + Text(Timestamp)
                + ", \"status\": \"Approved\", \"session_id\": \"s-1\"}";

            var verified = VerifyAt(reEncoded, headers);

            Assert.Equal(WebhookSignature.Canonical, verified.VerifiedBy);
            Assert.Equal("José \U0001F600", ((SessionEvent)verified).VendorData);
        }

        [Fact]
        public void TamperedBodyFailsEverySignature()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);
            var tampered = body.Replace("Approved", "Declined", StringComparison.Ordinal);

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyAt(tampered, headers)));
        }

        [Fact]
        public void VerifyParsedCannotUseTheRawBodySignature()
        {
            var body = SessionBody();
            var headers = new Dictionary<string, string> { ["X-Signature"] = SignedHeaders(body)["X-Signature"] };

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyParsedAt(body, headers)));
        }

        [Fact]
        public void VerifyParsedAcceptsSimpleOnlyWhenAllowed()
        {
            var body = SessionBody();
            var headers = new Dictionary<string, string> { ["X-Signature-Simple"] = SignedHeaders(body)["X-Signature-Simple"] };

            Assert.Equal(WebhookVerificationReason.InvalidSignature, Reason(() => VerifyParsedAt(body, headers)));
            Assert.Equal(WebhookSignature.Simple, VerifyParsedAt(body, headers, allowSimple: true).VerifiedBy);
        }

        [Fact]
        public void PayloadOutlivesTheParsedDocument()
        {
            var body = SessionBody();
            var headers = SignedHeaders(body);
            DiditEvent verified;
            using (var document = JsonDocument.Parse(body))
            {
                verified = DiditWebhooks.VerifyCore(null, document.RootElement, Lookup(headers), new[] { Secret }, Tolerance, false, DateTimeOffset.FromUnixTimeSeconds(Now));
            }

            Assert.Equal("s-1", verified.Payload.GetProperty("session_id").GetString());
        }

        [Fact]
        public void EveryPublicOverloadVerifiesWithTheCurrentClock()
        {
            var body = SessionBody(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var bytes = Encoding.UTF8.GetBytes(body);
            var headers = SignedHeaders(body);
            var getHeader = Lookup(headers);
            var secrets = new List<string> { OtherSecret, Secret };
            using var document = JsonDocument.Parse(body);
            var payload = document.RootElement;

            var results = new[]
            {
                DiditWebhooks.Verify(body, headers, Secret),
                DiditWebhooks.Verify(body, headers, secrets),
                DiditWebhooks.Verify(body, getHeader, Secret),
                DiditWebhooks.Verify(body, getHeader, secrets),
                DiditWebhooks.Verify(bytes, headers, Secret),
                DiditWebhooks.Verify(bytes, headers, secrets),
                DiditWebhooks.Verify(bytes, getHeader, Secret, TimeSpan.FromSeconds(60)),
                DiditWebhooks.Verify(bytes, getHeader, secrets, allowSimple: true),
                DiditWebhooks.VerifyParsed(payload, headers, Secret),
                DiditWebhooks.VerifyParsed(payload, headers, secrets),
                DiditWebhooks.VerifyParsed(payload, getHeader, Secret),
                DiditWebhooks.VerifyParsed(payload, getHeader, secrets, TimeSpan.FromSeconds(60), allowSimple: true),
            };

            Assert.All(results, result => Assert.Equal("s-1", Assert.IsType<SessionStatusUpdatedEvent>(result).SessionId));
            Assert.All(results.Take(8), result => Assert.Equal(WebhookSignature.RawBody, result.VerifiedBy));
            Assert.All(results.Skip(8), result => Assert.Equal(WebhookSignature.Canonical, result.VerifiedBy));
        }

        [Fact]
        public void PublicOverloadsUseTheCurrentClock()
        {
            var body = SessionBody(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

            Assert.Equal(
                WebhookVerificationReason.TimestampOutOfTolerance,
                Reason(() => DiditWebhooks.Verify(body, SignedHeaders(body), Secret)));
        }
    }
}
