using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Didit.Internal
{
    /// <summary>The JSON configuration every request and response goes through.</summary>
    internal static class JsonSupport
    {
        private static readonly Lazy<JsonSerializerOptions> LazyOptions = new(Create);

        public static JsonSerializerOptions Options => LazyOptions.Value;

        public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

        public static JsonTypeInfo<T> TypeInfo<T>() => TypeInfo<T>(Options);

        public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>());

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

        public static T? Deserialize<T>(byte[] utf8Json) => JsonSerializer.Deserialize(utf8Json, TypeInfo<T>());

        public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

        private static JsonSerializerOptions Create()
        {
            IJsonTypeInfoResolver resolver = JsonTypeInfoResolver.Combine(DiditJsonContext.Default, CoreJsonContext.Default);
            if (ReflectionAllowed())
            {
                // Free-form request values (metadata dictionaries, anonymous objects) are serialised by reflection.
                resolver = JsonTypeInfoResolver.Combine(resolver, new DefaultJsonTypeInfoResolver());
            }

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                PropertyNameCaseInsensitive = false,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                TypeInfoResolver = resolver.WithAddedModifier(RelaxRequiredMembers),
            };
            options.Converters.Add(new LenientDateTimeOffsetConverter());
            options.Converters.Add(new LenientStringConverter());
            return options;
        }

        // C# `required` marks what a request must carry. Responses are read leniently: a field the API leaves out is null,
        // never an exception, so older SDK versions keep working when the API changes.
        private static void RelaxRequiredMembers(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                property.IsRequired = false;
            }
        }

        private static bool ReflectionAllowed()
        {
#if NET8_0_OR_GREATER
            return JsonSerializer.IsReflectionEnabledByDefault;
#else
            return true;
#endif
        }
    }

    /// <summary>Reads ISO 8601 date-times, with a fallback to other invariant formats and unix seconds.</summary>
    internal sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a date-time string, got {reader.TokenType}.");
            }

            if (reader.TryGetDateTimeOffset(out var value))
            {
                return value;
            }

            var text = reader.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out value))
            {
                return value;
            }

            throw new JsonException($"'{text}' is not a valid date-time.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture));
    }

    /// <summary>Reads strings, and numbers or booleans where the API sends them in a string field.</summary>
    internal sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    using (var document = JsonDocument.ParseValue(ref reader))
                    {
                        return document.RootElement.GetRawText();
                    }

                default:
                    throw new JsonException($"Expected a string, got {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
