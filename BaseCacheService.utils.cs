using Microsoft.Extensions.Caching.Hybrid;

namespace CLOOPS.microservices;

public abstract partial class BaseCacheService<TValue>
{
    /// <summary>
    /// Gets a cache value and throws <see cref="KeyNotFoundException"/> if the source of truth
    /// reports the entry as missing (i.e. <c>HydrateSingleAsync</c> returned <c>null</c>/<c>default</c>).
    /// Use this when absence is genuinely an error condition for the caller; prefer the plain
    /// <see cref="GetAsync(string, System.Threading.CancellationToken)"/> for read paths where
    /// "not found" is expected.
    /// </summary>
    public async Task<TValue> GetRequiredAsync(string key, CancellationToken ct = default)
    {
        var value = await GetAsync(key, ct);
        if (value is null)
        {
            throw new KeyNotFoundException(
                $"Cache key '{key}' not found in '{CacheName}' (typeof({typeof(TValue).Name})).");
        }

        return value;
    }

    private HybridCacheEntryOptions ResolveEntryOptions(CacheGetOptions? options)
    {
        if (options == null || (options.L1Ttl == null && options.L2Ttl == null))
        {
            return defaultEntryOptions;
        }

        return new HybridCacheEntryOptions
        {
            Expiration = options.L2Ttl ?? config.L2Ttl,
            LocalCacheExpiration = options.L1Ttl ?? config.L1Ttl
        };
    }

    private string GetCacheKey(string key)
    {
        return $"{CacheName}:{key}";
    }
}

/// <summary>
/// Extension methods for <see cref="BaseCacheService{TValue}"/>.
/// </summary>
public static class BaseCacheServiceExtensions
{
    /// <summary>
    /// Gets a cache value and throws <see cref="KeyNotFoundException"/> if the source of truth
    /// reports the entry as missing (i.e. <c>HydrateSingleAsync</c> returned <c>null</c>/<c>default</c>).
    /// </summary>
    public static Task<TValue> GetRequiredAsync<TValue>(
        this BaseCacheService<TValue> cacheService,
        string key,
        CancellationToken ct = default)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(cacheService);

        return cacheService.GetRequiredAsync(key, ct);
    }
}
