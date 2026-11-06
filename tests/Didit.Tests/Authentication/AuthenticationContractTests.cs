using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Didit.Tests.Authentication
{
    /// <summary>
    /// DESIGN.md sections 10 and 11: every server operation of openapi/authentication.json has its hand-written method, that method
    /// calls the documented endpoint and reads the documented response, and the module has no other public method. A spec sync
    /// that adds, removes or renames an Authentication operation fails here until the module follows.
    /// </summary>
    public sealed class AuthenticationContractTests
    {
        private const string AccessToken = "eyJhbGciOiJFUzI1NiJ9.access-token-under-test.sig";
        private static readonly string[] HttpMethods = { "get", "put", "post", "delete", "options", "head", "patch", "trace" };

        /// <summary>The x-didit-sdk name of each server operation (one per variant), the method that implements it, and a call.</summary>
        private static readonly Dictionary<string, (string Method, Func<Didit.Authentication, TestIssuer, Task> Call)> Implementations =
            new Dictionary<string, (string Method, Func<Didit.Authentication, TestIssuer, Task> Call)>(StringComparer.Ordinal)
            {
                ["authentication.get_openid_configuration"] = (nameof(Didit.Authentication.GetDiscoveryDocumentAsync), (auth, _) => auth.GetDiscoveryDocumentAsync()),

                // The JWKS is fetched lazily and cached for token verification (section 10), never on its own.
                ["authentication.get_jwks"] = (nameof(Didit.Authentication.VerifyAccessTokenAsync), (auth, issuer) => auth.VerifyAccessTokenAsync(issuer.Sign(issuer.AccessClaims()))),
                ["authentication.exchange_code"] = (nameof(Didit.Authentication.ExchangeCodeAsync), (auth, _) => auth.ExchangeCodeAsync("code-abc", "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", "https://app.example.com/callback")),
                ["authentication.refresh"] = (nameof(Didit.Authentication.RefreshAsync), (auth, _) => auth.RefreshAsync("rt_old")),
                ["authentication.get_session"] = (nameof(Didit.Authentication.GetSessionAsync), (auth, _) => auth.GetSessionAsync(AccessToken)),
                ["authentication.revoke_session"] = (nameof(Didit.Authentication.RevokeSessionAsync), (auth, _) => auth.RevokeSessionAsync(AccessToken)),
                ["authentication.create_step_up"] = (nameof(Didit.Authentication.CreateStepUpAsync), (auth, _) => auth.CreateStepUpAsync(AccessToken, "payout", "250.00", "EUR", "https://app.example.com/payouts")),
                ["authentication.retrieve_step_up"] = (nameof(Didit.Authentication.RetrieveStepUpAsync), (auth, _) => auth.RetrieveStepUpAsync(AccessToken, "00000000-0000-4000-8000-000000000001")),
            };

        /// <summary>Public methods that call no operation of their own: ID token verification (discovery and JWKS), helpers, Dispose.</summary>
        private static readonly string[] LocalMethods =
        {
            nameof(Didit.Authentication.VerifyIdTokenAsync),
            nameof(Didit.Authentication.CreatePkce),
            nameof(Didit.Authentication.CreateState),
            nameof(Didit.Authentication.CreateNonce),
            nameof(Didit.Authentication.FromPublishableKey),
            nameof(Didit.Authentication.ParsePublishableKey),
            nameof(Didit.Authentication.Dispose),
        };

        [Fact]
        public void Every_server_operation_has_its_hand_written_method_and_nothing_else_exists()
        {
            var documented = ServerOperations().Select(o => o.Name).ToList();
            Assert.Equal(documented.Count, documented.Distinct(StringComparer.Ordinal).Count());

            var missing = documented.Except(Implementations.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var extra = Implementations.Keys.Except(documented, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, "authentication.json operations without a hand-written method:\n" + string.Join("\n", missing));
            Assert.True(extra.Count == 0, "Hand-written methods for operations authentication.json no longer has:\n" + string.Join("\n", extra));

            var methods = typeof(Didit.Authentication)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .ToList();
            Assert.Equal(methods.Count, methods.Distinct(StringComparer.Ordinal).Count());
            var expected = Implementations.Values.Select(i => i.Method).Concat(LocalMethods).OrderBy(x => x, StringComparer.Ordinal).Distinct();
            Assert.Equal(expected, methods.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public async Task Every_method_calls_the_documented_endpoint_and_reads_the_documented_response()
        {
            var operations = ServerOperations();
            foreach (var operation in operations)
            {
                using var issuer = new TestIssuer();
                issuer.Runtime = request => Respond(operations, request);
                using var auth = issuer.Create();

                await Implementations[operation.Name].Call(auth, issuer);

                Assert.True(
                    issuer.Handler.Requests.Any(r => operation.Matches(r)),
                    $"{operation.Name}: no {operation.Verb} {operation.Path} among " +
                    string.Join(", ", issuer.Handler.Requests.Select(r => $"{r.Method.Method} {r.Uri.AbsolutePath}")));
            }
        }

        /// <summary>The documented example of the first 2xx response; discovery and JWKS come from the test issuer's own key.</summary>
        private static HttpResponseMessage Respond(IEnumerable<Operation> operations, RecordedRequest request)
        {
            var operation = operations.FirstOrDefault(o => o.Matches(request));
            if (operation is null)
            {
                return StubHttpHandler.Json("{\"detail\":\"not found\"}", HttpStatusCode.NotFound);
            }

            if (operation.Example is null)
            {
                return new HttpResponseMessage(operation.Status);
            }

            return new HttpResponseMessage(operation.Status) { Content = new StringContent(operation.Example, Encoding.UTF8, "application/json") };
        }

        private static List<Operation> ServerOperations()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", "authentication.json")));
            var root = document.RootElement;
            var result = new List<Operation>();
            foreach (var path in root.GetProperty("paths").EnumerateObject())
            {
                foreach (var verb in HttpMethods)
                {
                    if (!path.Value.TryGetProperty(verb, out var operation))
                    {
                        continue;
                    }

                    Assert.True(operation.TryGetProperty("x-didit-sdk", out var naming), $"No x-didit-sdk for {verb.ToUpperInvariant()} {path.Name}");
                    if (!naming.GetProperty("server").GetBoolean())
                    {
                        continue;
                    }

                    var (status, example) = DocumentedResponse(root, operation);
                    var resource = naming.GetProperty("resource").GetString();
                    var names = naming.TryGetProperty("variants", out var variants)
                        ? variants.EnumerateArray().Select(v => v.GetProperty("method").GetString()).ToList()
                        : new List<string?> { naming.GetProperty("method").GetString() };
                    foreach (var name in names)
                    {
                        result.Add(new Operation($"{resource}.{name}", verb.ToUpperInvariant(), path.Name, status, example));
                    }
                }
            }

            return result;
        }

        private static (HttpStatusCode Status, string? Example) DocumentedResponse(JsonElement root, JsonElement operation)
        {
            var success = operation.GetProperty("responses").EnumerateObject()
                .Where(r => r.Name.StartsWith("2", StringComparison.Ordinal))
                .OrderBy(r => r.Name, StringComparer.Ordinal)
                .First();
            var response = Resolve(root, success.Value);
            var status = (HttpStatusCode)int.Parse(success.Name, System.Globalization.CultureInfo.InvariantCulture);
            if (!response.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var media))
            {
                return (status, null);
            }

            if (media.TryGetProperty("example", out var example))
            {
                return (status, example.GetRawText());
            }

            var first = media.GetProperty("examples").EnumerateObject().First().Value;
            return (status, Resolve(root, first).GetProperty("value").GetRawText());
        }

        private static JsonElement Resolve(JsonElement root, JsonElement node)
        {
            while (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("$ref", out var reference))
            {
                node = root;
                foreach (var part in reference.GetString()!.Substring(2).Split('/'))
                {
                    node = node.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
                }
            }

            return node;
        }

        private sealed class Operation
        {
            private readonly Regex _path;

            public Operation(string name, string verb, string path, HttpStatusCode status, string? example)
            {
                Name = name;
                Verb = verb;
                Path = path;
                Status = status;
                Example = example;
                _path = new Regex("^" + Regex.Replace(Regex.Escape(path), @"\\\{[^}]*\}", "[^/]+") + "$");
            }

            public string Name { get; }

            public string Verb { get; }

            public string Path { get; }

            public HttpStatusCode Status { get; }

            public string? Example { get; }

            public bool Matches(RecordedRequest request) =>
                string.Equals(request.Method.Method, Verb, StringComparison.Ordinal) && _path.IsMatch(request.Uri.AbsolutePath);
        }
    }
}
