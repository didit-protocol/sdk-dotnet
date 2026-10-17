using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Didit.Internal
{
    /// <summary>
    /// Lazily fetched, thread-safe cache of the issuer's discovery document and JWKS. Entries live for the response's
    /// <c>Cache-Control: max-age</c> (5 minutes when absent). An unknown <c>kid</c> forces at most one JWKS refetch per minute.
    /// </summary>
    internal sealed class IssuerMetadataCache : IDisposable
    {
        internal static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(1);
        internal static readonly TimeSpan UnknownKeyRefetchInterval = TimeSpan.FromMinutes(1);

        private readonly AuthenticationTransport _transport;
        private readonly Uri _discoveryUrl;
        private readonly string _issuer;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly SemaphoreSlim _discoveryLock = new(1, 1);
        private readonly SemaphoreSlim _keysLock = new(1, 1);

        private Entry<OpenIdConfiguration>? _discovery;
        private Entry<IReadOnlyList<SigningKey>>? _keys;
        private DateTimeOffset? _lastForcedKeyRefetch;

        public IssuerMetadataCache(AuthenticationTransport transport, string issuer, Uri discoveryUrl, Func<DateTimeOffset> utcNow)
        {
            _transport = transport;
            _issuer = issuer;
            _discoveryUrl = discoveryUrl;
            _utcNow = utcNow;
        }

        public async Task<OpenIdConfiguration> GetDiscoveryAsync(CancellationToken cancellationToken)
        {
            var cached = Volatile.Read(ref _discovery);
            if (cached is not null && cached.ExpiresAt > _utcNow())
            {
                return cached.Value;
            }

            await _discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cached = _discovery;
                if (cached is not null && cached.ExpiresAt > _utcNow())
                {
                    return cached.Value;
                }

                var response = await _transport.SendAsync(HttpMethod.Get, _discoveryUrl, null, null, null, cancellationToken).ConfigureAwait(false);
                var document = OpenIdConfiguration.FromJson(AuthJson.Parse(response.Body, "discovery document"));
                if (!IssuerComparer.Matches(document.Issuer, _issuer))
                {
                    throw new DiditException($"The discovery document at {_discoveryUrl} names the issuer '{document.Issuer}', expected '{_issuer}'.");
                }

                if (document.JwksUri is null)
                {
                    throw new DiditException($"The discovery document at {_discoveryUrl} has no valid jwks_uri.");
                }

                Volatile.Write(ref _discovery, new Entry<OpenIdConfiguration>(document, _utcNow() + Lifetime(response.CacheControl)));
                return document;
            }
            finally
            {
                _discoveryLock.Release();
            }
        }

        /// <summary>
        /// The keys that may have signed a token with this <c>kid</c> (every key when the token has none). Empty when no key
        /// matches even after the rate-limited refetch.
        /// </summary>
        public async Task<IReadOnlyList<SigningKey>> GetKeysAsync(string? keyId, CancellationToken cancellationToken)
        {
            var cached = Volatile.Read(ref _keys);
            if (cached is not null && cached.ExpiresAt > _utcNow())
            {
                var found = Match(cached.Value, keyId);
                if (found.Count > 0)
                {
                    return found;
                }
            }

            await _keysLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _utcNow();
                cached = _keys;
                if (cached is null || cached.ExpiresAt <= now)
                {
                    // A set fetched just now is current; an unknown kid is not worth a second request.
                    cached = await FetchKeysAsync(cancellationToken).ConfigureAwait(false);
                    return Match(cached.Value, keyId);
                }

                // Another caller may have refreshed the set while this one waited for the lock.
                var found = Match(cached.Value, keyId);
                if (found.Count > 0)
                {
                    return found;
                }

                // Unknown kid: the issuer may have rotated its keys. Refetch at most once per interval so a flood of tokens with
                // bogus key ids cannot turn into a flood of JWKS requests.
                if (_lastForcedKeyRefetch is { } last && now - last < UnknownKeyRefetchInterval)
                {
                    return Array.Empty<SigningKey>();
                }

                _lastForcedKeyRefetch = now;
                cached = await FetchKeysAsync(cancellationToken).ConfigureAwait(false);
                return Match(cached.Value, keyId);
            }
            finally
            {
                _keysLock.Release();
            }
        }

        public void Dispose()
        {
            _discoveryLock.Dispose();
            _keysLock.Dispose();
        }

        internal static TimeSpan Lifetime(CacheControlHeaderValue? cacheControl)
        {
            if (cacheControl is null)
            {
                return DefaultLifetime;
            }

            if (cacheControl.NoStore || cacheControl.NoCache)
            {
                return TimeSpan.Zero;
            }

            if (cacheControl.MaxAge is { } maxAge)
            {
                return maxAge < TimeSpan.Zero ? TimeSpan.Zero : (maxAge > MaxLifetime ? MaxLifetime : maxAge);
            }

            return DefaultLifetime;
        }

        private static List<SigningKey> Match(IReadOnlyList<SigningKey> keys, string? keyId) =>
            keyId is null ? keys.ToList() : keys.Where(key => key.KeyId == keyId).ToList();

        private async Task<Entry<IReadOnlyList<SigningKey>>> FetchKeysAsync(CancellationToken cancellationToken)
        {
            var discovery = await GetDiscoveryAsync(cancellationToken).ConfigureAwait(false);
            var response = await _transport.SendAsync(HttpMethod.Get, discovery.JwksUri!, null, null, null, cancellationToken).ConfigureAwait(false);
            var keys = SigningKey.ParseSet(AuthJson.Parse(response.Body, "JWKS"));
            var entry = new Entry<IReadOnlyList<SigningKey>>(keys, _utcNow() + Lifetime(response.CacheControl));
            Volatile.Write(ref _keys, entry);
            return entry;
        }

        private sealed class Entry<T>
        {
            public Entry(T value, DateTimeOffset expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
            }

            public T Value { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
