using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Didit
{
    /// <summary>
    /// One page of a list (<c>limit</c>/<c>offset</c> pagination). Enumerate it with <c>await foreach</c> to walk every
    /// item of every following page; the next pages are fetched on demand.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    public sealed class Page<T> : IAsyncEnumerable<T>
    {
        private readonly Func<string, CancellationToken, Task<Page<T>>>? _fetchPage;

        internal Page(Internal.PageBody<T> body, Func<string, CancellationToken, Task<Page<T>>>? fetchPage)
        {
            Count = body.Count;
            Next = body.Next;
            Previous = body.Previous;
            Results = body.Results ?? Array.Empty<T>();
            AdditionalProperties = body.AdditionalProperties is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>(body.AdditionalProperties);
            _fetchPage = fetchPage;
        }

        /// <summary>Creates a page, for tests and fakes.</summary>
        public Page(IReadOnlyList<T> results, long? count = null, string? next = null, string? previous = null)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
            Count = count;
            Next = next;
            Previous = previous;
            AdditionalProperties = new Dictionary<string, JsonElement>();
        }

        /// <summary>Total number of items across all pages.</summary>
        public long? Count { get; }

        /// <summary>URL of the next page, or <c>null</c> on the last page.</summary>
        public string? Next { get; }

        /// <summary>URL of the previous page, or <c>null</c> on the first page.</summary>
        public string? Previous { get; }

        /// <summary>The items of this page.</summary>
        public IReadOnlyList<T> Results { get; }

        /// <summary>Other top-level fields of the list response (for example <c>total_transactions</c> or <c>kpis</c>).</summary>
        public IReadOnlyDictionary<string, JsonElement> AdditionalProperties { get; }

        /// <summary><c>true</c> when there is a next page.</summary>
        public bool HasNextPage => !string.IsNullOrEmpty(Next);

        /// <summary>Fetches the next page, or returns <c>null</c> on the last page.</summary>
        public async Task<Page<T>?> GetNextPageAsync(CancellationToken cancellationToken = default)
        {
            if (!HasNextPage)
            {
                return null;
            }

            if (_fetchPage is null)
            {
                throw new InvalidOperationException("This page was not returned by a DiditClient and cannot fetch the next page.");
            }

            return await _fetchPage(Next!, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Walks the items of this page and of every following page.</summary>
        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Page<T>? page = this;
            while (page is not null)
            {
                foreach (var item in page.Results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item;
                }

                if (page.Results.Count == 0)
                {
                    yield break;
                }

                page = await page.GetNextPageAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Walks this page and every following page.</summary>
        public async IAsyncEnumerable<Page<T>> AsPagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Page<T>? page = this;
            while (page is not null)
            {
                yield return page;
                if (page.Results.Count == 0)
                {
                    yield break;
                }

                page = await page.GetNextPageAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

namespace Didit.Internal
{
    /// <summary>Wire shape of a list response.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    internal sealed class PageBody<T>
    {
        /// <summary>Total number of items.</summary>
        [JsonPropertyName("count")]
        public long? Count { get; set; }

        /// <summary>Next page URL.</summary>
        [JsonPropertyName("next")]
        public string? Next { get; set; }

        /// <summary>Previous page URL.</summary>
        [JsonPropertyName("previous")]
        public string? Previous { get; set; }

        /// <summary>Items.</summary>
        [JsonPropertyName("results")]
        public IReadOnlyList<T>? Results { get; set; }

        /// <summary>Other fields.</summary>
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
