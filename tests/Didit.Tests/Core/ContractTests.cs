using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Didit.Tests.Core
{
    /// <summary>DESIGN.md section 11: every server operation in openapi/ has exactly one SDK method, and nothing else exists.</summary>
    public sealed class ContractTests
    {
        private static readonly string[] HttpMethods = { "get", "put", "post", "delete", "options", "head", "patch", "trace" };

        [Fact]
        public void Every_server_operation_has_exactly_one_method_and_nothing_else_exists()
        {
            var expected = ExpectedMethods();
            var actual = SdkMethods(typeof(DiditClient), "Resource");

            var missing = expected.Keys.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var extra = actual.Except(expected.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, "Operations without an SDK method:\n" + string.Join("\n", missing.Select(m => $"{m} ({expected[m]})")));
            Assert.True(extra.Count == 0, "SDK methods without a server operation:\n" + string.Join("\n", extra));
            Assert.Equal(257, expected.Count);
        }

        [Fact]
        public void The_raw_response_tree_mirrors_the_main_tree()
        {
            var main = SdkMethods(typeof(DiditClient), "Resource");
            var raw = SdkMethods(typeof(DiditRawClient), "RawResource");
            Assert.Equal(main.OrderBy(x => x, StringComparer.Ordinal), raw.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void Every_method_takes_request_options_and_a_cancellation_token_last()
        {
            foreach (var method in ResourceTypes(typeof(DiditClient), "Resource").SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            {
                var parameters = method.GetParameters();
                Assert.True(parameters.Length >= 2, method.Name);
                Assert.Equal(typeof(RequestOptions), parameters[parameters.Length - 2].ParameterType);
                Assert.Equal(typeof(System.Threading.CancellationToken), parameters[parameters.Length - 1].ParameterType);
                Assert.EndsWith("Async", method.Name, StringComparison.Ordinal);
            }
        }

        private static Dictionary<string, string> ExpectedMethods()
        {
            var overlay = Load("overlay.json").RootElement.GetProperty("operations");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (file, prefix) in new[] { ("verification.json", string.Empty), ("account.json", "account ") })
            {
                using var document = Load(file);
                foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
                {
                    foreach (var method in HttpMethods)
                    {
                        if (!path.Value.TryGetProperty(method, out var operation))
                        {
                            continue;
                        }

                        var key = $"{prefix}{method.ToUpperInvariant()} {path.Name}";
                        JsonElement naming;
                        if (!operation.TryGetProperty("x-didit-sdk", out naming))
                        {
                            Assert.True(overlay.TryGetProperty(key, out naming), $"No SDK naming for {key}");
                        }

                        if (!naming.GetProperty("server").GetBoolean())
                        {
                            continue;
                        }

                        var resource = string.Join(".", naming.GetProperty("resource").GetString()!.Split('.').Select(Pascal));
                        var name = $"{resource}.{Pascal(naming.GetProperty("method").GetString()!)}Async";
                        Assert.False(result.ContainsKey(name), $"Two operations map to {name}");
                        result[name] = key;
                    }
                }
            }

            return result;
        }

        private static HashSet<string> SdkMethods(Type root, string suffix)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            void Walk(Type type, string prefix)
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType.Name.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                    foreach (var method in property.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!method.IsSpecialName)
                        {
                            Assert.True(result.Add(path + "." + method.Name), $"Overloaded SDK method {path}.{method.Name}");
                        }
                    }

                    Walk(property.PropertyType, path);
                }
            }

            Walk(root, string.Empty);
            return result;
        }

        private static IEnumerable<Type> ResourceTypes(Type root, string suffix)
        {
            foreach (var property in root.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType.Name.EndsWith(suffix, StringComparison.Ordinal)))
            {
                yield return property.PropertyType;
                foreach (var child in ResourceTypes(property.PropertyType, suffix))
                {
                    yield return child;
                }
            }
        }

        private static string Pascal(string snake) =>
            string.Concat(snake.Split('_').Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1)));

        private static JsonDocument Load(string name) =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", name)));
    }
}
