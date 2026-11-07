using System;
using System.Diagnostics.CodeAnalysis;

namespace Didit.Internal
{
    /// <summary>Unpadded base64url (RFC 4648 section 5), as used by JWS, JWK, PKCE and publishable keys.</summary>
    internal static class Base64Url
    {
        public static string Encode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Decodes strict unpadded base64url; returns <c>false</c> for any other character, padding or impossible length.</summary>
        public static bool TryDecode(string? text, [NotNullWhen(true)] out byte[]? data)
        {
            data = null;
            if (text is null || text.Length % 4 == 1)
            {
                return false;
            }

            var chars = new char[text.Length + ((4 - (text.Length % 4)) % 4)];
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '-')
                {
                    chars[i] = '+';
                }
                else if (c == '_')
                {
                    chars[i] = '/';
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    chars[i] = c;
                }
                else
                {
                    return false;
                }
            }

            for (var i = text.Length; i < chars.Length; i++)
            {
                chars[i] = '=';
            }

            try
            {
                data = Convert.FromBase64CharArray(chars, 0, chars.Length);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
