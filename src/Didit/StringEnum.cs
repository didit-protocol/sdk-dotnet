using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Didit
{
    /// <summary>
    /// An open enumeration backed by a string. The generated enum types expose the known values as static
    /// properties, but accept any string, so a value added to the API later never breaks deserialisation.
    /// </summary>
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "DESIGN.md 5.1 names them open string enums.")]
    public interface IStringEnum
    {
        /// <summary>The wire value.</summary>
        string Value { get; }
    }
}

namespace Didit.Internal
{
    /// <summary>Base JSON converter of the generated open string enums.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    internal abstract class StringEnumJsonConverter<T> : JsonConverter<T>
        where T : struct, IStringEnum
    {
        /// <summary>Creates the enum value from its wire value.</summary>
        protected abstract T Create(string value);

        /// <inheritdoc />
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return Create(reader.GetString() ?? string.Empty);
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    using (var document = JsonDocument.ParseValue(ref reader))
                    {
                        return Create(document.RootElement.GetRawText());
                    }

                default:
                    throw new JsonException($"Expected a string for {typeof(T).Name}, got {reader.TokenType}.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);

        /// <inheritdoc />
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Create(reader.GetString() ?? string.Empty);

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.Value);
    }
}
