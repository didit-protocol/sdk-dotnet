using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Didit.Internal
{
    /// <summary>
    /// The canonical JSON that <c>X-Signature-V2</c> signs (DESIGN.md 9.2): the output of Python's
    /// <c>json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)</c> after the backend's
    /// <c>shorten_floats</c>.
    /// </summary>
    internal static class WebhookCanonicalJson
    {
        // Python's json module has no fixed depth limit; System.Text.Json defaults to 64.
        public static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 512 };

        private const string HexDigits = "0123456789abcdef";

        private static readonly char[] ExponentChars = { 'E', 'e' };

        public static string Canonicalize(JsonElement value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        /// <summary>Compares strings by Unicode code point, the order of Python's <c>sorted()</c> on <c>str</c>.</summary>
        public static int CompareCodePoints(string left, string right)
        {
            var length = Math.Min(left.Length, right.Length);
            for (var i = 0; i < length; i++)
            {
                var a = left[i];
                var b = right[i];
                if (a != b)
                {
                    return CodePointRank(a) - CodePointRank(b);
                }
            }

            return left.Length - right.Length;
        }

        /// <summary>Formats a JSON number token the way the backend writes it after <c>shorten_floats</c>.</summary>
        public static string FormatNumber(string token)
        {
            foreach (var c in token)
            {
                if (c == '.' || c == 'e' || c == 'E')
                {
                    return FormatFloat(ParseDouble(token));
                }
            }

            // A JSON integer token is already in Python's int form, except for negative zero.
            return token == "-0" ? "0" : token;
        }

        /// <summary>Formats a double like the backend: integral values as integers, others in Python <c>repr</c> form.</summary>
        public static string FormatFloat(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsInfinity(value))
            {
                return value > 0 ? "Infinity" : "-Infinity";
            }

            if (Math.Floor(value) == value)
            {
                return new BigInteger(value).ToString(CultureInfo.InvariantCulture);
            }

            ShortestDigits(Math.Abs(value), out var digits, out var point);
            var builder = new StringBuilder(32);
            if (value < 0)
            {
                builder.Append('-');
            }

            // Python repr: value = 0.<digits> * 10^point; scientific when the exponent (point - 1) is below -4 or at least 16.
            if (point <= -4 || point > 16)
            {
                builder.Append(digits[0]);
                if (digits.Length > 1)
                {
                    builder.Append('.').Append(digits, 1, digits.Length - 1);
                }

                var exponent = point - 1;
                builder.Append('e').Append(exponent < 0 ? '-' : '+');
                var magnitude = Math.Abs(exponent);
                if (magnitude < 10)
                {
                    builder.Append('0');
                }

                builder.Append(magnitude.ToString(CultureInfo.InvariantCulture));
            }
            else if (point <= 0)
            {
                builder.Append("0.").Append('0', -point).Append(digits);
            }
            else if (point >= digits.Length)
            {
                builder.Append(digits).Append('0', point - digits.Length).Append(".0");
            }
            else
            {
                builder.Append(digits, 0, point).Append('.').Append(digits, point, digits.Length - point);
            }

            return builder.ToString();
        }

        private static void Write(StringBuilder builder, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(builder, value);
                    break;
                case JsonValueKind.Array:
                    builder.Append('[');
                    var first = true;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (!first)
                        {
                            builder.Append(',');
                        }

                        first = false;
                        Write(builder, item);
                    }

                    builder.Append(']');
                    break;
                case JsonValueKind.String:
                    WriteString(builder, ReadString(value));
                    break;
                case JsonValueKind.Number:
                    builder.Append(FormatNumber(value.GetRawText()));
                    break;
                case JsonValueKind.True:
                    builder.Append("true");
                    break;
                case JsonValueKind.False:
                    builder.Append("false");
                    break;
                case JsonValueKind.Null:
                    builder.Append("null");
                    break;
                default:
                    throw new ArgumentException("The JSON value is undefined.", nameof(value));
            }
        }

        private static void WriteObject(StringBuilder builder, JsonElement value)
        {
            // Duplicate keys keep the last value, like a Python dict.
            var properties = new List<KeyValuePair<string, JsonElement>>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                var name = ReadName(property);
                if (positions.TryGetValue(name, out var position))
                {
                    properties[position] = new KeyValuePair<string, JsonElement>(name, property.Value);
                }
                else
                {
                    positions[name] = properties.Count;
                    properties.Add(new KeyValuePair<string, JsonElement>(name, property.Value));
                }
            }

            properties.Sort((left, right) => CompareCodePoints(left.Key, right.Key));

            builder.Append('{');
            for (var i = 0; i < properties.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteString(builder, properties[i].Key);
                builder.Append(':');
                Write(builder, properties[i].Value);
            }

            builder.Append('}');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u00").Append(HexDigits[c >> 4]).Append(HexDigits[c & 0xF]);
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static string ReadString(JsonElement value)
        {
            try
            {
                return value.GetString() ?? string.Empty;
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                throw new JsonException("The JSON contains a string that is not valid Unicode.", ex);
            }
        }

        private static string ReadName(JsonProperty property)
        {
            try
            {
                return property.Name;
            }
            catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
            {
                throw new JsonException("The JSON contains a property name that is not valid Unicode.", ex);
            }
        }

        // Maps UTF-16 code units so that ordinal comparison matches code point order: surrogates (U+D800..U+DFFF,
        // always part of a code point above U+FFFF) rank after U+E000..U+FFFF.
        private static int CodePointRank(char c) => c >= '\uE000' ? c - 0x800 : c >= '\uD800' ? c + 0x2000 : c;

        private static double ParseDouble(string token)
        {
#if NETCOREAPP3_0_OR_GREATER
            // Correctly rounded since .NET Core 3.0; out-of-range values become infinities, as in Python.
            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#else
            return WebhookFloat.Parse(token);
#endif
        }

        // The shortest decimal digits that round-trip to the value (Python repr), as value = 0.<digits> * 10^point.
        private static void ShortestDigits(double value, out string digits, out int point)
        {
#if NETCOREAPP3_0_OR_GREATER
            // "R" is the shortest round-trip form since .NET Core 3.0.
            ParseDigits(value.ToString("R", CultureInfo.InvariantCulture), out digits, out point);
#else
            WebhookFloat.ShortestDigits(value, out digits, out point);
#endif
        }

        // Splits a positive formatted double ("123.45", "1.5E-07", "1.000E+005") into significant digits and point position.
        private static void ParseDigits(string text, out string digits, out int point)
        {
            var exponentAt = text.IndexOfAny(ExponentChars);
            var mantissa = exponentAt < 0 ? text : text.Substring(0, exponentAt);
            var exponent = exponentAt < 0
                ? 0
                : int.Parse(text.Substring(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var dot = mantissa.IndexOf('.');
            var integerPart = dot < 0 ? mantissa : mantissa.Substring(0, dot);
            var all = dot < 0 ? mantissa : integerPart + mantissa.Substring(dot + 1);
            point = integerPart.Length + exponent;

            var leading = 0;
            while (leading < all.Length && all[leading] == '0')
            {
                leading++;
            }

            point -= leading;
            digits = all.Substring(leading).TrimEnd('0');
            if (digits.Length == 0)
            {
                digits = "0";
                point = 1;
            }
        }
    }
}
