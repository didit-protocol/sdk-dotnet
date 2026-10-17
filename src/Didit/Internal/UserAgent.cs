using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Didit.Internal
{
    /// <summary>The <c>User-Agent</c> and <c>X-Didit-Client</c> header values.</summary>
    internal static class UserAgent
    {
        public const string Language = "dotnet";

        public static readonly string Version = ReadVersion();

        public static readonly string ClientHeader = $"{Language}/{Version}";

        public static readonly string Value = $"didit-{Language}/{Version} ({Clean(RuntimeInformation.FrameworkDescription)}; {Clean(RuntimeInformation.OSDescription)})";

        private static string ReadVersion()
        {
            var assembly = typeof(UserAgent).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                var plus = informational!.IndexOf('+');
                return plus >= 0 ? informational.Substring(0, plus) : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        private static string Clean(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                builder.Append(c >= 0x20 && c < 0x7f && c != '(' && c != ')' && c != ';' ? c : ' ');
            }

            return builder.ToString().Trim();
        }
    }
}
