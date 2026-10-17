using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// A Didit Authentication publishable key: <c>pk_live_</c> or <c>pk_test_</c> followed by the base64url-encoded issuer URL.
    /// It is public (it ships in browser and mobile apps) and identifies the application's issuer; it is not a secret and not an API key.
    /// </summary>
    public sealed class PublishableKey
    {
        /// <summary>Prefix of live keys.</summary>
        public const string LivePrefix = "pk_live_";

        /// <summary>Prefix of test keys.</summary>
        public const string TestPrefix = "pk_test_";

        private PublishableKey(string value, string issuer, bool isLive)
        {
            Value = value;
            Issuer = issuer;
            IsLive = isLive;
        }

        /// <summary>The key as given.</summary>
        public string Value { get; }

        /// <summary>The issuer URL the key carries, for example <c>https://acme.auth.didit.me</c>.</summary>
        public string Issuer { get; }

        /// <summary><c>true</c> for a <c>pk_live_</c> key.</summary>
        public bool IsLive { get; }

        /// <summary><c>true</c> for a <c>pk_test_</c> key.</summary>
        public bool IsTest => !IsLive;

        /// <summary>Parses a publishable key.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException">The key has no <c>pk_live_</c> or <c>pk_test_</c> prefix, or does not carry a valid issuer URL.</exception>
        public static PublishableKey Parse(string key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (!TryParse(key, out var parsed, out var error))
            {
                throw new FormatException(error);
            }

            return parsed;
        }

        /// <summary>Parses a publishable key; returns <c>false</c> instead of throwing when it is invalid.</summary>
        public static bool TryParse(string? key, [NotNullWhen(true)] out PublishableKey? publishableKey) =>
            TryParse(key, out publishableKey, out _);

        /// <summary>Returns <see cref="Value"/>.</summary>
        public override string ToString() => Value;

        private static bool TryParse(string? key, [NotNullWhen(true)] out PublishableKey? publishableKey, out string error)
        {
            publishableKey = null;
            var text = key?.Trim() ?? string.Empty;
            bool isLive;
            if (text.StartsWith(LivePrefix, StringComparison.Ordinal))
            {
                isLive = true;
            }
            else if (text.StartsWith(TestPrefix, StringComparison.Ordinal))
            {
                isLive = false;
            }
            else
            {
                error = "A publishable key starts with pk_live_ or pk_test_.";
                return false;
            }

            var encoded = text.Substring(LivePrefix.Length).TrimEnd('=');
            if (!Base64Url.TryDecode(encoded, out var bytes) || bytes.Length == 0)
            {
                error = "The publishable key does not carry a base64url-encoded issuer.";
                return false;
            }

            string issuer;
            try
            {
                issuer = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                error = "The publishable key does not carry a UTF-8 issuer URL.";
                return false;
            }

            try
            {
                issuer = IssuerComparer.Normalize(issuer, nameof(key));
            }
            catch (ArgumentException ex)
            {
                error = "The publishable key does not carry a valid issuer URL: " + ex.Message;
                return false;
            }

            publishableKey = new PublishableKey(text, issuer, isLive);
            error = string.Empty;
            return true;
        }
    }
}
