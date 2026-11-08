using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Didit
{
    /// <summary>A file sent in a multipart request: from a path, bytes or a stream, with an optional file name and content type.</summary>
    public sealed class FileUpload
    {
        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".heic"] = "image/heic",
            [".heif"] = "image/heif",
            [".bmp"] = "image/bmp",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".pdf"] = "application/pdf",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".txt"] = "text/plain",
            [".mp4"] = "video/mp4",
            [".mov"] = "video/quicktime",
            [".webm"] = "video/webm",
        };

        private readonly string? _path;
        private readonly Stream? _stream;
        private byte[]? _bytes;

        private FileUpload(string fileName, string contentType, string? path, byte[]? bytes, Stream? stream)
        {
            FileName = fileName;
            ContentType = contentType;
            _path = path;
            _bytes = bytes;
            _stream = stream;
        }

        /// <summary>The file name sent in the part's <c>Content-Disposition</c>.</summary>
        public string FileName { get; }

        /// <summary>The part's content type.</summary>
        public string ContentType { get; }

        /// <summary>A file on disk. It is read when the request is sent.</summary>
        public static FileUpload FromPath(string path, string? fileName = null, string? contentType = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            var name = fileName ?? Path.GetFileName(path);
            return new FileUpload(name, contentType ?? GuessContentType(name), path, null, null);
        }

        /// <summary>Bytes in memory.</summary>
        public static FileUpload FromBytes(byte[] content, string fileName = "file", string? contentType = null)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new FileUpload(fileName, contentType ?? GuessContentType(fileName), null, content, null);
        }

        /// <summary>
        /// A stream. A seekable stream is rewound for every attempt; a non-seekable one is buffered in memory once so the
        /// request can be retried. The SDK does not dispose the stream.
        /// </summary>
        public static FileUpload FromStream(Stream content, string fileName = "file", string? contentType = null)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new FileUpload(fileName, contentType ?? GuessContentType(fileName), null, null, content);
        }

        /// <summary>Converts bytes into an upload named <c>file</c>.</summary>
        public static implicit operator FileUpload(byte[] content) => FromBytes(content);

        internal async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken)
        {
            if (_bytes is not null)
            {
                return _bytes;
            }

            if (_path is not null)
            {
                using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }

            var stream = _stream!;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }

            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                _bytes = buffer.ToArray();
            }

            return _bytes;
        }

        internal static string GuessContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName ?? string.Empty);
            return extension is not null && ContentTypes.TryGetValue(extension, out var type) ? type : "application/octet-stream";
        }
    }
}
