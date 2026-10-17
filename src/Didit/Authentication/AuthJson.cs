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
        private static readonly char[] Space = { ' ' };

        /// <summary>A detached empty JSON object.</summary>
        public static JsonElement EmptyObject { get; } = CreateEmptyObject();

        public static JsonElement Parse(byte[] utf8, string what)
        {
            try
            {
                using var document = JsonDocument.Parse(utf8);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new DiditException($"The {what} is not a JSON object.");
                }

                if (!IsValidUnicode(document.RootElement))
                {
                    throw new DiditException($"The {what} contains a string that is not valid Unicode.");
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
                return value.GetString()!.Split(Space, StringSplitOptions.RemoveEmptyEntries);
            }

            return GetStringList(element, name);
        }

        /// <summary>
        /// Whether every string and property name decodes. The parser accepts a lone escaped surrogate (<c>"\ud800"</c>) and
        /// <see cref="JsonElement.GetString"/> then throws <see cref="InvalidOperationException"/>, so documents are checked once
        /// when parsed and the readers above never throw.
        /// </summary>
        public static bool IsValidUnicode(JsonElement element)
        {
            try
            {
                return Check(element);
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                return false;
            }

            static bool Check(JsonElement value)
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        _ = value.GetString();
                        return true;
                    case JsonValueKind.Object:
                        foreach (var property in value.EnumerateObject())
                        {
                            _ = property.Name;
                            if (!Check(property.Value))
                            {
                                return false;
                            }
                        }

                        return true;
                    case JsonValueKind.Array:
                        foreach (var item in value.EnumerateArray())
                        {
                            if (!Check(item))
                            {
                                return false;
                            }
                        }

                        return true;
                    default:
                        return true;
                }
            }
        }

        private static JsonElement CreateEmptyObject()
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }
}
