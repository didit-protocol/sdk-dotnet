using System.Security.Cryptography;
#if !NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Didit.Internal
{
    /// <summary>HMAC-SHA256, strict hex decoding and constant-time comparison for webhook signatures.</summary>
    internal static class WebhookCrypto
    {
        private const int SignatureLength = 32;

        public static byte[] HmacSha256(byte[] key, byte[] data)
        {
#if NET8_0_OR_GREATER
            return HMACSHA256.HashData(key, data);
#else
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
#endif
        }

        /// <summary>
        /// Decodes a signature header. Only a 64-character lowercase hex value is well formed; anything else
        /// returns <c>null</c> and is never compared.
        /// </summary>
        public static byte[]? ParseSignature(string? value)
        {
            if (value is null)
            {
                return null;
            }

            value = value.Trim();
            if (value.Length != SignatureLength * 2)
            {
                return null;
            }

            var bytes = new byte[SignatureLength];
            for (var i = 0; i < SignatureLength; i++)
            {
                var high = HexValue(value[2 * i]);
                var low = HexValue(value[(2 * i) + 1]);
                if (high < 0 || low < 0)
                {
                    return null;
                }

                bytes[i] = (byte)((high << 4) | low);
            }

            return bytes;
        }

        /// <summary>Compares a computed signature with a decoded header value in constant time.</summary>
        public static bool Matches(byte[] computed, byte[]? expected)
        {
            if (expected is null || expected.Length != computed.Length)
            {
                return false;
            }

#if NET8_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(computed, expected);
#else
            return FixedTimeEquals(computed, expected);
#endif
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            return -1;
        }

#if !NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }
#endif
    }
}
