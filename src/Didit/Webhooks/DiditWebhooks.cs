using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// Verifies Didit webhook deliveries and parses them into typed events. Framework independent: pass the raw body,
    /// the request headers and your webhook secret (or several during a rotation).
    /// </summary>
    /// <remarks>
    /// A delivery is accepted when its <c>timestamp</c> is within the tolerance of the current time and one of
    /// <c>X-Signature</c> (HMAC-SHA256 of the raw body), <c>X-Signature-V2</c> (HMAC-SHA256 of the canonical JSON) or their
    /// <c>-Previous</c> variants matches one of the secrets. The deprecated <c>X-Signature-Simple</c> counts only when
    /// <c>allowSimple</c> is <c>true</c>, and then <see cref="DiditEvent.IsBodyAuthenticated"/> is <c>false</c>.
    /// </remarks>
    public static class DiditWebhooks
    {
        /// <summary>How far the delivery's <c>timestamp</c> may be from the current time: 300 seconds.</summary>
        public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

        internal const string SignatureHeader = "X-Signature";
        internal const string SignatureV2Header = "X-Signature-V2";
        internal const string SignatureSimpleHeader = "X-Signature-Simple";
        internal const string SignaturePreviousHeader = "X-Signature-Previous";
        internal const string SignatureV2PreviousHeader = "X-Signature-V2-Previous";
        internal const string SignatureSimplePreviousHeader = "X-Signature-Simple-Previous";
        internal const string TimestampHeader = "X-Timestamp";

        private const long UnixEpochTicks = 621355968000000000L;

        /// <summary>Verifies a webhook delivery and returns its typed event.</summary>
        /// <param name="rawBody">The request body exactly as received, decoded as UTF-8.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent Verify(string rawBody, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            Verify(rawBody, headers, new[] { secret }, tolerance, allowSimple);

        /// <summary>Verifies a webhook delivery against any of several secrets (during a rotation) and returns its typed event.</summary>
        /// <param name="rawBody">The request body exactly as received, decoded as UTF-8.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent Verify(string rawBody, IEnumerable<KeyValuePair<string, string>> headers, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(Utf8(rawBody, nameof(rawBody)), null, HeaderLookup(headers), secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>Verifies a webhook delivery and returns its typed event.</summary>
        /// <param name="rawBody">The request body exactly as received, decoded as UTF-8.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent Verify(string rawBody, Func<string, string?> getHeader, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            Verify(rawBody, getHeader, new[] { secret }, tolerance, allowSimple);

        /// <summary>Verifies a webhook delivery against any of several secrets (during a rotation) and returns its typed event.</summary>
        /// <param name="rawBody">The request body exactly as received, decoded as UTF-8.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent Verify(string rawBody, Func<string, string?> getHeader, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(Utf8(rawBody, nameof(rawBody)), null, getHeader, secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>Verifies a webhook delivery and returns its typed event.</summary>
        /// <param name="rawBody">The request body bytes exactly as received.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent Verify(byte[] rawBody, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            Verify(rawBody, headers, new[] { secret }, tolerance, allowSimple);

        /// <summary>Verifies a webhook delivery against any of several secrets (during a rotation) and returns its typed event.</summary>
        /// <param name="rawBody">The request body bytes exactly as received.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent Verify(byte[] rawBody, IEnumerable<KeyValuePair<string, string>> headers, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(NotNull(rawBody, nameof(rawBody)), null, HeaderLookup(headers), secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>Verifies a webhook delivery and returns its typed event.</summary>
        /// <param name="rawBody">The request body bytes exactly as received.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent Verify(byte[] rawBody, Func<string, string?> getHeader, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            Verify(rawBody, getHeader, new[] { secret }, tolerance, allowSimple);

        /// <summary>Verifies a webhook delivery against any of several secrets (during a rotation) and returns its typed event.</summary>
        /// <param name="rawBody">The request body bytes exactly as received.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent Verify(byte[] rawBody, Func<string, string?> getHeader, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(NotNull(rawBody, nameof(rawBody)), null, getHeader, secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Verifies a webhook delivery from its already parsed JSON body, for frameworks that do not expose the raw body.
        /// Only <c>X-Signature-V2</c> (and <c>X-Signature-V2-Previous</c>) can be checked, plus <c>X-Signature-Simple</c> when
        /// <paramref name="allowSimple"/> is <c>true</c>. Prefer <c>Verify</c> with the raw body when you have it.
        /// </summary>
        /// <param name="payload">The parsed request body.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent VerifyParsed(JsonElement payload, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            VerifyParsed(payload, headers, new[] { secret }, tolerance, allowSimple);

        /// <summary>
        /// Verifies a webhook delivery from its already parsed JSON body against any of several secrets. Only
        /// <c>X-Signature-V2</c> (and <c>X-Signature-V2-Previous</c>) can be checked, plus <c>X-Signature-Simple</c> when
        /// <paramref name="allowSimple"/> is <c>true</c>.
        /// </summary>
        /// <param name="payload">The parsed request body.</param>
        /// <param name="headers">The request headers. Names are matched case-insensitively.</param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent VerifyParsed(JsonElement payload, IEnumerable<KeyValuePair<string, string>> headers, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(null, payload, HeaderLookup(headers), secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Verifies a webhook delivery from its already parsed JSON body. Only <c>X-Signature-V2</c> (and
        /// <c>X-Signature-V2-Previous</c>) can be checked, plus <c>X-Signature-Simple</c> when <paramref name="allowSimple"/> is <c>true</c>.
        /// </summary>
        /// <param name="payload">The parsed request body.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secret">The webhook secret of the destination.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">The secret is null or empty.</exception>
        public static DiditEvent VerifyParsed(JsonElement payload, Func<string, string?> getHeader, string secret, TimeSpan? tolerance = null, bool allowSimple = false) =>
            VerifyParsed(payload, getHeader, new[] { secret }, tolerance, allowSimple);

        /// <summary>
        /// Verifies a webhook delivery from its already parsed JSON body against any of several secrets. Only
        /// <c>X-Signature-V2</c> (and <c>X-Signature-V2-Previous</c>) can be checked, plus <c>X-Signature-Simple</c> when
        /// <paramref name="allowSimple"/> is <c>true</c>.
        /// </summary>
        /// <param name="payload">The parsed request body.</param>
        /// <param name="getHeader">
        /// Returns the value of a request header, or <c>null</c> when it is absent. In ASP.NET Core:
        /// <c>name =&gt; request.Headers[name].FirstOrDefault()</c>.
        /// </param>
        /// <param name="secrets">The webhook secrets to accept, for example the new and the old one during a rotation.</param>
        /// <param name="tolerance">How far the delivery's timestamp may be from now. Defaults to <see cref="DefaultTolerance"/>.</param>
        /// <param name="allowSimple">Also accept the deprecated <c>X-Signature-Simple</c>, which does not cover the body.</param>
        /// <returns>The verified event.</returns>
        /// <exception cref="WebhookVerificationException">The delivery failed verification; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        /// <exception cref="ConfigurationException">No secret is given, or one of them is null or empty.</exception>
        public static DiditEvent VerifyParsed(JsonElement payload, Func<string, string?> getHeader, IEnumerable<string> secrets, TimeSpan? tolerance = null, bool allowSimple = false)
        {
            var secretList = ToSecretList(secrets);
            return VerifyCore(null, payload, getHeader, secretList, ToleranceOrDefault(tolerance), allowSimple, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Returns the canonical JSON of a document: the string <c>X-Signature-V2</c> signs. Keys are sorted by code point,
        /// there is no whitespace, non-ASCII characters are written as is, and numbers follow Python's formatting.
        /// </summary>
        /// <param name="json">A JSON document.</param>
        /// <returns>The canonical JSON.</returns>
        /// <exception cref="JsonException"><paramref name="json"/> is not valid JSON.</exception>
        public static string Canonicalize(string json)
        {
            using var document = JsonDocument.Parse(NotNull(json, nameof(json)), WebhookCanonicalJson.DocumentOptions);
            return WebhookCanonicalJson.Canonicalize(document.RootElement);
        }

        /// <summary>
        /// Returns the canonical JSON of a parsed value: the string <c>X-Signature-V2</c> signs. Keys are sorted by code
        /// point, there is no whitespace, non-ASCII characters are written as is, and numbers follow Python's formatting.
        /// </summary>
        /// <param name="value">A parsed JSON value.</param>
        /// <returns>The canonical JSON.</returns>
        /// <exception cref="ArgumentException"><paramref name="value"/> is the default (undefined) element.</exception>
        /// <exception cref="JsonException">A string in <paramref name="value"/> is not valid Unicode.</exception>
        public static string Canonicalize(JsonElement value) => WebhookCanonicalJson.Canonicalize(value);

        /// <summary>The verification itself, with an explicit clock. Exactly one of <paramref name="rawBody"/> and <paramref name="parsed"/> is set.</summary>
        internal static DiditEvent VerifyCore(
            byte[]? rawBody,
            JsonElement? parsed,
            Func<string, string?> getHeader,
            IReadOnlyList<string> secrets,
            TimeSpan tolerance,
            bool allowSimple,
            DateTimeOffset now)
        {
            var keys = SecretKeys(secrets);
            NotNull(getHeader, nameof(getHeader));
            if (tolerance < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance), "The tolerance must not be negative.");
            }

            var payload = rawBody is not null ? ParseBody(rawBody) : ParsedBody(parsed);

            // 1. The body's timestamp, cross-checked with X-Timestamp.
            if (!payload.TryGetProperty("timestamp", out var timestampElement)
                || timestampElement.ValueKind != JsonValueKind.Number
                || !timestampElement.TryGetInt64(out var timestamp))
            {
                throw new WebhookVerificationException(WebhookVerificationReason.MissingTimestamp, "The webhook body has no integer timestamp.");
            }

            var timestampHeader = ReadHeader(getHeader, TimestampHeader, "x-timestamp");
            if (timestampHeader is not null
                && !(long.TryParse(timestampHeader, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var headerTimestamp) && headerTimestamp == timestamp))
            {
                throw new WebhookVerificationException(WebhookVerificationReason.TimestampMismatch, "The X-Timestamp header does not match the body's timestamp.");
            }

            // 2. Freshness.
            var nowSeconds = (now.UtcTicks - UnixEpochTicks) / (decimal)TimeSpan.TicksPerSecond;
            var toleranceSeconds = tolerance.Ticks / (decimal)TimeSpan.TicksPerSecond;
            if (Math.Abs(nowSeconds - timestamp) > toleranceSeconds)
            {
                throw new WebhookVerificationException(
                    WebhookVerificationReason.TimestampOutOfTolerance,
                    "The webhook timestamp is outside the tolerance of " + toleranceSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.");
            }

            // 3. Signatures. Malformed values count as present but are never compared.
            var signature = ReadHeader(getHeader, SignatureHeader, "x-signature");
            var signaturePrevious = ReadHeader(getHeader, SignaturePreviousHeader, "x-signature-previous");
            var signatureV2 = ReadHeader(getHeader, SignatureV2Header, "x-signature-v2");
            var signatureV2Previous = ReadHeader(getHeader, SignatureV2PreviousHeader, "x-signature-v2-previous");
            var signatureSimple = ReadHeader(getHeader, SignatureSimpleHeader, "x-signature-simple");
            var signatureSimplePrevious = ReadHeader(getHeader, SignatureSimplePreviousHeader, "x-signature-simple-previous");
            if (signature is null && signaturePrevious is null && signatureV2 is null && signatureV2Previous is null
                && signatureSimple is null && signatureSimplePrevious is null)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.MissingSignature, "The webhook has no signature header.");
            }

            var verifiedBy = Match(
                keys,
                rawBody,
                payload,
                timestamp,
                allowSimple,
                WebhookCrypto.ParseSignature(signature),
                WebhookCrypto.ParseSignature(signaturePrevious),
                WebhookCrypto.ParseSignature(signatureV2),
                WebhookCrypto.ParseSignature(signatureV2Previous),
                WebhookCrypto.ParseSignature(signatureSimple),
                WebhookCrypto.ParseSignature(signatureSimplePrevious));
            if (verifiedBy is null)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidSignature, "No webhook signature matches the secret.");
            }

            return WebhookEventFactory.Create(payload, verifiedBy.Value);
        }

        /// <summary>The <c>X-Signature-Simple</c> message: <c>"{timestamp}:{session_id}:{status}:{webhook_type}"</c>.</summary>
        internal static string SimpleMessage(JsonElement payload, long timestamp) =>
            string.Concat(
                timestamp.ToString(CultureInfo.InvariantCulture),
                ":",
                SimpleField(payload, "session_id"),
                ":",
                SimpleField(payload, "status"),
                ":",
                SimpleField(payload, "webhook_type"));

        private static WebhookSignature? Match(
            byte[][] keys,
            byte[]? rawBody,
            JsonElement payload,
            long timestamp,
            bool allowSimple,
            byte[]? signature,
            byte[]? signaturePrevious,
            byte[]? signatureV2,
            byte[]? signatureV2Previous,
            byte[]? signatureSimple,
            byte[]? signatureSimplePrevious)
        {
            if (rawBody is not null && (signature is not null || signaturePrevious is not null)
                && AnyMatches(keys, rawBody, signature, signaturePrevious))
            {
                return WebhookSignature.RawBody;
            }

            if (signatureV2 is not null || signatureV2Previous is not null)
            {
                var canonical = TryCanonicalBytes(payload);
                if (canonical is not null && AnyMatches(keys, canonical, signatureV2, signatureV2Previous))
                {
                    return WebhookSignature.Canonical;
                }
            }

            if (allowSimple && (signatureSimple is not null || signatureSimplePrevious is not null)
                && AnyMatches(keys, Encoding.UTF8.GetBytes(SimpleMessage(payload, timestamp)), signatureSimple, signatureSimplePrevious))
            {
                return WebhookSignature.Simple;
            }

            return null;
        }

        private static bool AnyMatches(byte[][] keys, byte[] message, byte[]? current, byte[]? previous)
        {
            foreach (var key in keys)
            {
                var computed = WebhookCrypto.HmacSha256(key, message);
                if (WebhookCrypto.Matches(computed, current) || WebhookCrypto.Matches(computed, previous))
                {
                    return true;
                }
            }

            return false;
        }

        private static byte[]? TryCanonicalBytes(JsonElement payload)
        {
            try
            {
                return Encoding.UTF8.GetBytes(WebhookCanonicalJson.Canonicalize(payload));
            }
            catch (JsonException)
            {
                // Not representable (a lone surrogate); the backend cannot have signed it either.
                return null;
            }
        }

        // The backend's str(data.get(name, "")): a missing field is empty, JSON null is "None", booleans are "True"/"False".
        private static string SimpleField(JsonElement payload, string name)
        {
            if (!payload.TryGetProperty(name, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => WebhookJson.ReadString(value) ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                JsonValueKind.Null => "None",
                _ => string.Empty,
            };
        }

        private static byte[][] SecretKeys(IReadOnlyList<string>? secrets)
        {
            if (secrets is null || secrets.Count == 0)
            {
                throw new ConfigurationException("A webhook secret is required.");
            }

            var keys = new byte[secrets.Count][];
            for (var i = 0; i < secrets.Count; i++)
            {
                var secret = secrets[i];
                if (string.IsNullOrEmpty(secret))
                {
                    throw new ConfigurationException("The webhook secret must not be empty.");
                }

                keys[i] = Encoding.UTF8.GetBytes(secret);
            }

            return keys;
        }

        // Validates the secrets first, so a configuration error wins over any other failure.
        private static List<string> ToSecretList(IEnumerable<string>? secrets)
        {
            var list = secrets is null ? new List<string>() : new List<string>(secrets);
            SecretKeys(list);
            return list;
        }

        private static TimeSpan ToleranceOrDefault(TimeSpan? tolerance) => tolerance ?? DefaultTolerance;

        private static JsonElement ParseBody(byte[] rawBody)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawBody, WebhookCanonicalJson.DocumentOptions);
            }
            catch (JsonException)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidPayload, "The webhook body is not valid JSON.");
            }

            using (document)
            {
                return ParsedBody(document.RootElement);
            }
        }

        private static JsonElement ParsedBody(JsonElement? parsed)
        {
            if (parsed is not { } value)
            {
                throw new ArgumentNullException(nameof(parsed));
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidPayload, "The webhook body is not a JSON object.");
            }

            return value.Clone();
        }

        // Absent, empty and blank values all read as absent. Adapters that look headers up case-sensitively still
        // find the lowercase spelling.
        private static string? ReadHeader(Func<string, string?> getHeader, string name, string lowercaseName)
        {
            var value = getHeader(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = getHeader(lowercaseName);
            }

            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static Func<string, string?> HeaderLookup(IEnumerable<KeyValuePair<string, string>> headers)
        {
            NotNull(headers, nameof(headers));
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                // The first non-empty value of a repeated header wins.
                if (header.Key is not null && !string.IsNullOrWhiteSpace(header.Value) && !values.ContainsKey(header.Key))
                {
                    values[header.Key] = header.Value;
                }
            }

            return name => values.TryGetValue(name, out var value) ? value : null;
        }

        private static byte[] Utf8(string value, string parameterName) => Encoding.UTF8.GetBytes(NotNull(value, parameterName));

        private static T NotNull<T>(T value, string parameterName)
            where T : class =>
            value ?? throw new ArgumentNullException(parameterName);
    }
}
