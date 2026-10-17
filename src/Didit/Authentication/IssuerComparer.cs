using System;

namespace Didit.Internal
{
    /// <summary>Issuer identifiers are compared as exact strings, except that one trailing slash on either side is ignored.</summary>
    internal static class IssuerComparer
    {
        public static bool Matches(string? candidate, string issuer) =>
            candidate is not null && string.Equals(Trim(candidate), Trim(issuer), StringComparison.Ordinal);

        public static string Trim(string issuer) =>
            issuer.Length > 0 && issuer[issuer.Length - 1] == '/' ? issuer.Substring(0, issuer.Length - 1) : issuer;

        /// <summary>
        /// Validates and normalises an issuer URL: absolute <c>https</c> (plain <c>http</c> only for loopback hosts), no query or
        /// fragment, surrounding whitespace removed.
        /// </summary>
        public static string Normalize(string? issuer, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(issuer))
            {
                throw new ArgumentException("The issuer must be a non-empty URL.", parameterName);
            }

            var text = issuer!.Trim();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"The issuer '{text}' is not an absolute URL.", parameterName);
            }

            var secure = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            var loopbackHttp = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
            if (!secure && !loopbackHttp)
            {
                throw new ArgumentException($"The issuer '{text}' must use https.", parameterName);
            }

            if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            {
                throw new ArgumentException($"The issuer '{text}' must not have a query, fragment or user info.", parameterName);
            }

            return text;
        }
    }
}
