using System;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>Lenient readers for webhook payloads: a missing field, or one of an unexpected JSON type, reads as <c>null</c>.</summary>
    internal static class WebhookJson
    {
        public static string? GetString(JsonElement payload, string name) =>
            TryGet(payload, name, out var value) && value.ValueKind == JsonValueKind.String ? ReadString(value) : null;

        public static long? GetInt64(JsonElement payload, string name) =>
            TryGet(payload, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? number
                : null;

        public static double? GetDouble(JsonElement payload, string name) =>
            TryGet(payload, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
                ? number
                : null;

        public static bool? GetBoolean(JsonElement payload, string name)
        {
            if (!TryGet(payload, name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        /// <summary>A string or a number, as text (money amounts arrive either way).</summary>
        public static string? GetStringOrNumber(JsonElement payload, string name)
        {
            if (!TryGet(payload, name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => ReadString(value),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }

        /// <summary>Any JSON value except <c>null</c>.</summary>
        public static JsonElement? GetElement(JsonElement payload, string name) =>
            TryGet(payload, name, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined
                ? value
                : null;

        /// <summary>A JSON object, or <c>null</c>.</summary>
        public static JsonElement? GetObject(JsonElement payload, string name) =>
            TryGet(payload, name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

        public static string? ReadString(JsonElement value)
        {
            try
            {
                return value.GetString();
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                // Not valid Unicode.
                return null;
            }
        }

        private static bool TryGet(JsonElement payload, string name, out JsonElement value)
        {
            if (payload.ValueKind == JsonValueKind.Object)
            {
                return payload.TryGetProperty(name, out value);
            }

            value = default;
            return false;
        }
    }
}
