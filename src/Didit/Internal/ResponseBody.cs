using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Internal
{
    /// <summary>Reads a response body under the request's timeout and cancellation token.</summary>
    internal static class ResponseBody
    {
        public static async Task<byte[]> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            // .NET Framework handlers may return a response without content.
            if (response.Content is null)
            {
                return Array.Empty<byte>();
            }

            // .NET Standard 2.0 has no cancellable ReadAsByteArrayAsync, and some handlers ignore the token while reading: abort
            // the response when it fires, so a stalled body cannot outlive the timeout.
            using (cancellationToken.Register(response.Dispose))
            {
                try
                {
                    using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
                catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
                {
                    throw new OperationCanceledException("The response body was not read before the timeout.", ex, cancellationToken);
                }
            }
#endif
        }
    }
}
