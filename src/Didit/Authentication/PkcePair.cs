using System.Security.Cryptography;
using System.Text;
using Didit.Internal;

namespace Didit
{
    /// <summary>A PKCE (RFC 7636) code verifier and its S256 code challenge. Create one per authorization request with <see cref="Authentication.CreatePkce"/>.</summary>
    public sealed class PkcePair
    {
        /// <summary>The only challenge method Didit accepts.</summary>
        public const string S256 = "S256";

        private PkcePair(string codeVerifier, string codeChallenge)
        {
            CodeVerifier = codeVerifier;
            CodeChallenge = codeChallenge;
        }

        /// <summary>The secret verifier (43 base64url characters). Keep it server-side, for example in the session, until the code exchange.</summary>
        public string CodeVerifier { get; }

        /// <summary><c>base64url(SHA-256(CodeVerifier))</c>: send it as <c>code_challenge</c> in the authorization request.</summary>
        public string CodeChallenge { get; }

        /// <summary>Always <c>S256</c>: send it as <c>code_challenge_method</c>.</summary>
        public string CodeChallengeMethod => S256;

        internal static PkcePair FromVerifier(string codeVerifier) => new(codeVerifier, ComputeChallenge(codeVerifier));

        internal static string ComputeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            return Base64Url.Encode(sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)));
        }
    }
}
