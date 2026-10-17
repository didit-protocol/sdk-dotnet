using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>Lenient readers for the Authentication JSON documents: a missing or mistyped field reads as <c>null</c>.</summary>
    internal static class AuthJson
    {
        private static readonly IReadOnlyList<string> EmptyList = Array.Empty<string>();

        public static JsonElement Parse(byte[] utf8, string what)
        {
            try
            {
                using var document = JsonDocument.Parse(utf8);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new DiditException($"The {what} is not a JSON object.");
                }

                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new DiditException($"The {what} is not valid JSON: {ex.Message}", ex);
            }
        }

        public static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }

            value = default;
            return false;
        }

        public static string? GetString(JsonElement element, string name) =>
            TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>A string, or the raw text of a number or boolean (ids are sometimes numeric).</summary>
        public static string? GetText(JsonElement element, string name)
        {
            if (!TryGet(element, name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null,
            };
        }

        public static bool? GetBoolean(JsonElement element, string name)
        {
            if (!TryGet(element, name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }

        public static long? GetInt64(JsonElement element, string name)
        {
            if (!TryGet(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var integer))
                {
                    return integer;
                }

                if (value.TryGetDouble(out var real) && real >= long.MinValue && real <= long.MaxValue)
                {
                    return (long)Math.Floor(real);
                }
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        public static DateTimeOffset? GetDateTime(JsonElement element, string name)
        {
            var text = GetString(element, name);
            if (text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            {
                return value;
            }

            return null;
        }

        public static JsonElement? GetElement(JsonElement element, string name) =>
            TryGet(element, name, out var value) ? value.Clone() : null;

        /// <summary>An array of strings, or a single string (as JWT <c>aud</c> allows); empty when absent.</summary>
        public static IReadOnlyList<string> GetStringList(JsonElement element, string name)
        {
            if (!TryGet(element, name, out var value))
            {
                return EmptyList;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return new[] { value.GetString()! };
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return EmptyList;
            }

            var list = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    list.Add(item.GetString()!);
                }
            }

            return list;
        }

        /// <summary>A space-separated string (OAuth <c>scope</c>) or an array of strings; empty when absent.</summary>
        public static IReadOnlyList<string> GetSpaceSeparated(JsonElement element, string name)
        {
            if (TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return GetStringList(element, name);
        }
    }
}
