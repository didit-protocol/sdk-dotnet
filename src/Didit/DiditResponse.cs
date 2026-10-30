using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Didit
{
    /// <summary>The raw HTTP response of a call made through <see cref="DiditClient.WithRawResponse"/>.</summary>
    public class DiditResponse
    {
        internal DiditResponse(int statusCode, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string? requestId, byte[] body)
        {
            StatusCode = statusCode;
            Headers = headers;
            RequestId = requestId;
            Body = body;
        }

        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>Response headers (names are case-insensitive).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

        /// <summary>The request id Didit assigned to the request, when the response carries one.</summary>
        public string? RequestId { get; }

        /// <summary>The raw response body.</summary>
        public byte[] Body { get; }

        /// <summary>First value of a response header, or <c>null</c>.</summary>
        public string? GetHeader(string name) =>
            Headers.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
    }

    /// <summary>A raw HTTP response together with its parsed body.</summary>
    /// <typeparam name="T">The parsed body type.</typeparam>
    public sealed class DiditResponse<T> : DiditResponse
    {
        internal DiditResponse(DiditResponse raw, T data)
            : base(raw.StatusCode, raw.Headers, raw.RequestId, raw.Body)
        {
            Data = data;
        }

        /// <summary>The parsed body.</summary>
        public T Data { get; }
    }

    /// <summary>A binary response body, such as a PDF report, a CSV template or an image.</summary>
    public sealed class BinaryResponse
    {
        internal BinaryResponse(byte[] content, string? contentType, string? fileName)
        {
            Content = content;
            ContentType = contentType;
            FileName = fileName;
        }

        /// <summary>The bytes.</summary>
        public byte[] Content { get; }

        /// <summary>The <c>Content-Type</c> of the response, for example <c>application/pdf</c>.</summary>
        public string? ContentType { get; }

        /// <summary>The file name from <c>Content-Disposition</c>, when present.</summary>
        public string? FileName { get; }

        /// <summary>Opens a read-only stream over the content.</summary>
        public Stream OpenRead() => new MemoryStream(Content, writable: false);

        /// <summary>Writes the content to a file.</summary>
        public async Task SaveToAsync(string path, CancellationToken cancellationToken = default)
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await file.WriteAsync(Content, 0, Content.Length, cancellationToken).ConfigureAwait(false);
        }

        internal static string? ParseFileName(string? contentDisposition)
        {
            if (string.IsNullOrEmpty(contentDisposition))
            {
                return null;
            }

            string? plain = null;
            foreach (var rawPart in contentDisposition!.Split(';').Select(p => p.Trim()))
            {
                if (rawPart.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = rawPart.Substring("filename*=".Length).Trim('"');
                    var quote = value.IndexOf("''", StringComparison.Ordinal);
                    return Uri.UnescapeDataString(quote >= 0 ? value.Substring(quote + 2) : value);
                }

                if (rawPart.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    plain = rawPart.Substring("filename=".Length).Trim('"');
                }
            }

            return plain;
        }
    }
}
