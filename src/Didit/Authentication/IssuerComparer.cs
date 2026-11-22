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
        /// Validates an issuer URL: absolute <c>https</c> (plain <c>http</c> only for loopback hosts) with no query, fragment or
        /// user info. Returns the trimmed URL, or <c>null</c> and the reason.
        /// </summary>
        public static string? Validate(string? issuer, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(issuer))
            {
                error = "The issuer must be a non-empty URL.";
                return null;
            }

            var text = issuer!.Trim();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                error = $"The issuer '{text}' is not an absolute URL.";
                return null;
            }

            var secure = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            var loopbackHttp = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
            if (!secure && !loopbackHttp)
            {
                error = $"The issuer '{text}' must use https.";
                return null;
            }

            if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            {
                error = $"The issuer '{text}' must not have a query, fragment or user info.";
                return null;
            }

            return text;
        }
    }
}
