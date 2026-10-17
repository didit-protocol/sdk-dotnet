using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Didit.Tests.Webhooks
{
    /// <summary>Signing helpers and a fixed clock for the webhook tests. Every secret here is synthetic.</summary>
    internal static class WebhookTestSupport
    {
        public const string Secret = "whsec_unit_test_3b7e1c9a5d2f4e6081a7c3b9d5e2f7a1";
        public const string OtherSecret = "whsec_unit_test_other_0f1e2d3c4b5a69788796a5b4c3d2e1f0";
        public const long Timestamp = 1760702400;
        public const long Now = Timestamp + 10;

        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(300);

        public static string Hmac(string secret, string message)
        {
#if NET5_0_OR_GREATER
            var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));
#else
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
#endif
            return string.Concat(mac.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>A minimal session body with the given timestamp and status.</summary>
        public static string SessionBody(long timestamp = Timestamp, string status = "Approved") =>
            "{\"event_id\":\"evt-1\",\"session_id\":\"s-1\",\"status\":\"" + status + "\",\"timestamp\":" + Text(timestamp)
            + ",\"webhook_type\":\"status.updated\"}";

        /// <summary>The headers Didit sends for <paramref name="body"/>, signed with <paramref name="secret"/>.</summary>
        public static Dictionary<string, string> SignedHeaders(string body, string secret = Secret)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var timestamp = root.GetProperty("timestamp").GetInt64();
            var simple = Text(timestamp) + ":" + Field(root, "session_id") + ":" + Field(root, "status") + ":" + Field(root, "webhook_type");
            return new Dictionary<string, string>
            {
                ["X-Signature"] = Hmac(secret, body),
                ["X-Signature-V2"] = Hmac(secret, DiditWebhooks.Canonicalize(body)),
                ["X-Signature-Simple"] = Hmac(secret, simple),
                ["X-Timestamp"] = Text(timestamp),
            };
        }

        public static Func<string, string?> Lookup(IReadOnlyDictionary<string, string> headers)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                values[header.Key] = header.Value;
            }

            return name => values.TryGetValue(name, out var value) ? value : null;
        }

        public static DiditEvent VerifyAt(
            string body,
            IReadOnlyDictionary<string, string> headers,
            long now = Now,
            bool allowSimple = false,
            IReadOnlyList<string>? secrets = null,
            TimeSpan? tolerance = null) =>
            DiditWebhooks.VerifyCore(
                Encoding.UTF8.GetBytes(body),
                null,
                Lookup(headers),
                secrets ?? new[] { Secret },
                tolerance ?? Tolerance,
                allowSimple,
                DateTimeOffset.FromUnixTimeSeconds(now));

        public static DiditEvent VerifyParsedAt(
            string body,
            IReadOnlyDictionary<string, string> headers,
            long now = Now,
            bool allowSimple = false,
            IReadOnlyList<string>? secrets = null)
        {
            using var document = JsonDocument.Parse(body);
            return DiditWebhooks.VerifyCore(
                null,
                document.RootElement,
                Lookup(headers),
                secrets ?? new[] { Secret },
                Tolerance,
                allowSimple,
                DateTimeOffset.FromUnixTimeSeconds(now));
        }

        /// <summary>Signs <paramref name="body"/> as Didit does and verifies it at the fixed clock.</summary>
        public static DiditEvent SignAndVerify(string body) => VerifyAt(body, SignedHeaders(body));

        public static string Reason(Action action) => Assert.Throws<WebhookVerificationException>(action).Reason;

        private static string Field(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.Null ? "None" : value.ToString();
        }
    }

    /// <summary>One entry of <c>openapi/webhook-vectors.json</c>.</summary>
    internal sealed class WebhookVector
    {
        public WebhookVector(
            string name,
            string body,
            string canonicalV2,
            IReadOnlyDictionary<string, string> headers,
            string secret,
            string? previousSecret,
            long now,
            string expect)
        {
            Name = name;
            Body = body;
            CanonicalV2 = canonicalV2;
            Headers = headers;
            Secret = secret;
            PreviousSecret = previousSecret;
            Now = now;
            Expect = expect;
        }

        public string Name { get; }

        public string Body { get; }

        public string CanonicalV2 { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public string Secret { get; }

        public string? PreviousSecret { get; }

        public long Now { get; }

        public string Expect { get; }
    }

    /// <summary>The shared signature vectors, produced by running the backend's own signing functions.</summary>
    internal static class WebhookVectors
    {
        private static readonly Lazy<(TimeSpan Tolerance, IReadOnlyDictionary<string, WebhookVector> Vectors)> Loaded = new(Load);

        public static TimeSpan Tolerance => Loaded.Value.Tolerance;

        public static IEnumerable<WebhookVector> All => Loaded.Value.Vectors.Values;

        public static WebhookVector Get(string name) => Loaded.Value.Vectors[name];

        public static DiditEvent Verify(WebhookVector vector, bool allowSimple, IReadOnlyList<string>? secrets = null) =>
            DiditWebhooks.VerifyCore(
                Encoding.UTF8.GetBytes(vector.Body),
                null,
                WebhookTestSupport.Lookup(vector.Headers),
                secrets ?? new[] { vector.Secret },
                Tolerance,
                allowSimple,
                DateTimeOffset.FromUnixTimeSeconds(vector.Now));

        private static (TimeSpan, IReadOnlyDictionary<string, WebhookVector>) Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "openapi", "webhook-vectors.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var vectors = new Dictionary<string, WebhookVector>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("vectors").EnumerateArray())
            {
                var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var header in item.GetProperty("headers").EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString()!;
                }

                var previous = item.GetProperty("previous_secret");
                var vector = new WebhookVector(
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("body").GetString()!,
                    item.GetProperty("canonical_v2").GetString()!,
                    headers,
                    item.GetProperty("secret").GetString()!,
                    previous.ValueKind == JsonValueKind.String ? previous.GetString() : null,
                    item.GetProperty("now").GetInt64(),
                    item.GetProperty("expect").GetString()!);
                vectors.Add(vector.Name, vector);
            }

            return (TimeSpan.FromSeconds(root.GetProperty("tolerance_seconds").GetInt64()), vectors);
        }
    }
}
