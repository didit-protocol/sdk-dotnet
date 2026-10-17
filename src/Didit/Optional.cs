using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// A request field that is either absent (the default, not sent) or set, possibly to <c>null</c>.
    /// Assign a value directly: <c>VendorData = "user-42"</c> sends the value, <c>VendorData = null</c> sends
    /// JSON <c>null</c>, and leaving the property alone sends nothing.
    /// </summary>
    /// <typeparam name="T">The field type.</typeparam>
    [JsonConverter(typeof(OptionalJsonConverterFactory))]
    public readonly struct Optional<T> : IEquatable<Optional<T>>
    {
        private readonly T _value;

        /// <summary>Creates a set value.</summary>
        public Optional(T value)
        {
            _value = value;
            IsSet = true;
        }

        /// <summary>The absent value: the field is not sent.</summary>
        public static Optional<T> Unset => default;

        /// <summary><c>true</c> when the field is set (to a value or to <c>null</c>).</summary>
        public bool IsSet { get; }

        /// <summary>The value. Throws <see cref="InvalidOperationException"/> when the field is not set.</summary>
        public T Value => IsSet ? _value : throw new InvalidOperationException("The optional value is not set.");

        /// <summary>Converts a value into a set <see cref="Optional{T}"/>.</summary>
        public static implicit operator Optional<T>(T value) => new(value);

        /// <summary>Creates a set value.</summary>
        public static Optional<T> FromValue(T value) => new(value);

        /// <summary>The value when set, otherwise <paramref name="defaultValue"/>.</summary>
        public T GetValueOrDefault(T defaultValue) => IsSet ? _value : defaultValue;

        /// <summary>The value when set, otherwise the default of <typeparamref name="T"/>.</summary>
        public T? GetValueOrDefault() => IsSet ? _value : default;

        /// <summary>Gets the value when set.</summary>
        public bool TryGetValue(out T value)
        {
            value = _value;
            return IsSet;
        }

        /// <inheritdoc />
        public bool Equals(Optional<T> other) =>
            IsSet == other.IsSet && (!IsSet || EqualityComparer<T>.Default.Equals(_value, other._value));

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => IsSet ? (_value is null ? 1 : _value.GetHashCode() * 31 + 1) : 0;

        /// <summary>Equality.</summary>
        public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

        /// <summary>Inequality.</summary>
        public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => IsSet ? (_value?.ToString() ?? "null") : "(unset)";
    }
}

namespace Didit.Internal
{
    /// <summary>Serialises <see cref="Optional{T}"/> as its value. Absent values are skipped by the property's ignore condition.</summary>
    internal sealed class OptionalJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

        /// <inheritdoc />
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var inner = typeToConvert.GetGenericArguments()[0];
            return (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(inner))!;
        }
    }

    /// <summary>Converter for one closed <see cref="Optional{T}"/>.</summary>
    /// <typeparam name="T">The field type.</typeparam>
    internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new Optional<T>(default!);
            }

            var info = JsonSupport.TypeInfo<T>(options);
            return new Optional<T>(JsonSerializer.Deserialize(ref reader, info)!);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            var inner = value.GetValueOrDefault();
            if (inner is null)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, inner, JsonSupport.TypeInfo<T>(options));
        }
    }
}
