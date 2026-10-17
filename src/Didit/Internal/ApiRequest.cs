using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Internal
{
    /// <summary>Which API an operation belongs to.</summary>
    internal enum ApiHost
    {
        Verification,
        Account,
    }

    /// <summary>How an operation authenticates (x-didit-sdk <c>auth</c>).</summary>
    internal enum AuthKind
    {
        ApiKey,
        AccountToken,
        None,
    }

    /// <summary>One API call, as built by the generated resources.</summary>
    internal sealed class ApiRequest
    {
        public ApiRequest(HttpMethod method, string path)
        {
            Method = method;
            Path = path;
        }

        public HttpMethod Method { get; }

        /// <summary>The path, with path parameters already filled. Application-scoped paths keep the
        /// <c>{organization_id}</c> and <c>{application_id}</c> placeholders, filled from the options when sent.</summary>
        public string Path { get; }

        /// <summary>An absolute URL to call instead of <see cref="Path"/> (pagination). Its origin is checked first.</summary>
        public Uri? AbsoluteUrl { get; set; }

        public ApiHost Host { get; set; } = ApiHost.Verification;

        public AuthKind Auth { get; set; } = AuthKind.ApiKey;

        public bool ApplicationScoped { get; set; }

        public QueryString Query { get; } = new();

        public RequestBody? Body { get; set; }

        public ApiRequest WithAbsoluteUrl(Uri url) => new(Method, Path)
        {
            AbsoluteUrl = url,
            Host = Host,
            Auth = Auth,
            ApplicationScoped = false,
            Body = Body,
        };
    }

    /// <summary>Query string builder used by the generated code.</summary>
    internal sealed class QueryString
    {
        private readonly List<KeyValuePair<string, string>> _pairs = new();

        public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

        public void Add(string name, string? value)
        {
            if (value is not null)
            {
                _pairs.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        public void Add(string name, long? value) => Add(name, value?.ToString(CultureInfo.InvariantCulture));

        public void Add(string name, double? value) => Add(name, value?.ToString("R", CultureInfo.InvariantCulture));

        public void Add(string name, bool? value) => Add(name, value is null ? null : value.Value ? "true" : "false");

        public void Add(string name, DateTimeOffset? value) =>
            Add(name, value?.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture));

        public void Add(string name, IEnumerable<string>? values)
        {
            if (values is null)
            {
                return;
            }

            foreach (var value in values)
            {
                Add(name, value);
            }
        }

        public void Add(string name, IEnumerable<long>? values)
        {
            if (values is null)
            {
                return;
            }

            foreach (var value in values)
            {
                Add(name, value);
            }
        }

        public string Encode()
        {
            if (_pairs.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var pair in _pairs)
            {
                builder.Append(builder.Length == 0 ? '?' : '&');
                builder.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value));
            }

            return builder.ToString();
        }
    }

    /// <summary>A request body that can be rebuilt for every attempt.</summary>
    internal abstract class RequestBody
    {
        public abstract Task<HttpContent> CreateContentAsync(CancellationToken cancellationToken);
    }

    /// <summary>A JSON body, serialised once.</summary>
    internal sealed class JsonRequestBody : RequestBody
    {
        private readonly byte[] _utf8;

        private JsonRequestBody(byte[] utf8)
        {
            _utf8 = utf8;
        }

        public static JsonRequestBody Create<T>(T value) => new(JsonSupport.SerializeToUtf8Bytes(value));

        public override Task<HttpContent> CreateContentAsync(CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(_utf8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            return Task.FromResult<HttpContent>(content);
        }
    }

    /// <summary>A multipart/form-data body. Scalars become text parts, objects and arrays of objects JSON text parts.</summary>
    internal sealed class MultipartRequestBody : RequestBody
    {
        private readonly List<(string Name, string? Text, FileUpload? File)> _parts = new();

        public IReadOnlyList<(string Name, string? Text, FileUpload? File)> Parts => _parts;

        public void Add(string name, string? value)
        {
            if (value is not null)
            {
                _parts.Add((name, value, null));
            }
        }

        public void Add(string name, long? value) => Add(name, value?.ToString(CultureInfo.InvariantCulture));

        public void Add(string name, double? value) => Add(name, value?.ToString("R", CultureInfo.InvariantCulture));

        public void Add(string name, bool? value) => Add(name, value is null ? null : value.Value ? "true" : "false");

        public void Add(string name, DateTimeOffset? value) =>
            Add(name, value?.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture));

        public void Add(string name, FileUpload? file)
        {
            if (file is not null)
            {
                _parts.Add((name, null, file));
            }
        }

        /// <summary>Repeats the field once per value.</summary>
        public void AddEach(string name, IEnumerable<string>? values)
        {
            if (values is null)
            {
                return;
            }

            foreach (var value in values)
            {
                Add(name, value);
            }
        }

        /// <summary>Repeats the file field once per file.</summary>
        public void AddEach(string name, IEnumerable<FileUpload>? files)
        {
            if (files is null)
            {
                return;
            }

            foreach (var file in files)
            {
                Add(name, file);
            }
        }

        /// <summary>Sends the value as JSON text.</summary>
        public void AddJson<T>(string name, T value)
        {
            if (value is not null)
            {
                Add(name, JsonSupport.Serialize(value));
            }
        }

        /// <summary>A free-form value: strings and scalars as text, anything else as JSON text.</summary>
        public void AddAny(string name, object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case string text:
                    Add(name, text);
                    return;
                case bool flag:
                    Add(name, flag);
                    return;
                case IFormattable formattable when value is int or long or short or byte or uint or ulong or ushort or sbyte or double or float or decimal:
                    Add(name, formattable.ToString(null, CultureInfo.InvariantCulture));
                    return;
                case FileUpload file:
                    Add(name, file);
                    return;
                case IStringEnum enumValue:
                    Add(name, enumValue.Value);
                    return;
                default:
                    Add(name, JsonSupport.Serialize(value));
                    return;
            }
        }

        public override async Task<HttpContent> CreateContentAsync(CancellationToken cancellationToken)
        {
            var content = new MultipartFormDataContent("didit-" + Guid.NewGuid().ToString("N"));
            foreach (var (name, text, file) in _parts)
            {
                if (file is null)
                {
                    content.Add(new StringContent(text!, Encoding.UTF8), Quote(name));
                    continue;
                }

                var bytes = await file.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
                content.Add(part, Quote(name), Quote(file.FileName));
            }

            return content;
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>HTTP methods, including PATCH which .NET Standard 2.0 does not define.</summary>
    internal static class HttpMethods
    {
        public static readonly HttpMethod Get = HttpMethod.Get;
        public static readonly HttpMethod Post = HttpMethod.Post;
        public static readonly HttpMethod Put = HttpMethod.Put;
        public static readonly HttpMethod Delete = HttpMethod.Delete;
        public static readonly HttpMethod Head = HttpMethod.Head;
        public static readonly HttpMethod Options = HttpMethod.Options;
        public static readonly HttpMethod Patch = new("PATCH");
    }

    /// <summary>Fills path parameters.</summary>
    internal static class PathParameter
    {
        public static string Encode(string? value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"The path parameter '{name}' must be a non-empty string.", name);
            }

            return Uri.EscapeDataString(value!);
        }

        public static string Encode(long value, string name) => value.ToString(CultureInfo.InvariantCulture);
    }
}
