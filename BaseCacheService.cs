using CLOOPS.NATS;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace CLOOPS.microservices;

/// <summary>
/// Base class for typed cache services backed by <c>HybridCache</c>.
/// Cache services are read-through: <see cref="GetAsync(string, System.Threading.CancellationToken)"/>
/// checks L1, then L2, then calls <see cref="HydrateSingleAsync"/> on a miss.
/// Apply <see cref="CacheConfigAttribute"/> on derived types to declare cache name and TTLs.
/// </summary>
/// <typeparam name="TValue">The value type stored by the cache service.</typeparam>
public abstract partial class BaseCacheService<TValue> : IHostedService, IDisposable
{
    // The container holding actual data
    private readonly HybridCache cache;
    private readonly ICloopsNatsClient? natsClient;
    private readonly ILogger<BaseCacheService<TValue>> logger;
    private readonly CacheConfigAttribute config;
    private readonly HybridCacheEntryOptions defaultEntryOptions;
    private readonly string[] entryTags;
    private readonly Lazy<bool> hasBulkHydration;
    private readonly CancellationTokenSource stoppingCts = new();

    private Task? scheduledRefreshTask;
    private bool disposed;

    /// <summary>
    /// Initializes a new cache service instance.
    /// </summary>
    /// <param name="serviceProvider">
    /// The application's <see cref="IServiceProvider"/>. The base class resolves
    /// <c>HybridCache</c>, <c>ILogger&lt;BaseCacheService&lt;TValue&gt;&gt;</c>, and
    /// (optionally) <c>ICloopsNatsClient</c> from it.
    /// </param>
    protected BaseCacheService(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        cache = serviceProvider.GetRequiredService<HybridCache>();
        logger = serviceProvider.GetRequiredService<ILogger<BaseCacheService<TValue>>>();
        natsClient = serviceProvider.GetService<ICloopsNatsClient>();

        config = GetType().GetCustomAttribute<CacheConfigAttribute>(inherit: true)
            ?? throw new InvalidOperationException(
                $"{GetType().FullName} must declare a [CacheConfig(...)] attribute.");

        ValidateConfig(config);

        defaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = config.L2Ttl,
            LocalCacheExpiration = config.L1Ttl
        };
        entryTags = [config.Name];
        hasBulkHydration = new Lazy<bool>(ComputeHasBulkHydration, isThreadSafe: true);
    }

    /// <summary>
    /// Stable cache name from <see cref="CacheConfigAttribute.Name"/>.
    /// </summary>
    public string CacheName => config.Name;

    /// <summary>
    /// Gets a cache value from L1, then L2, then the source of truth via <see cref="HydrateSingleAsync"/>.
    /// Returns <c>null</c> (or <c>default</c> for value types) when the value does not exist in the
    /// source of truth. The null/default outcome is itself cached for the configured TTL.
    /// </summary>
    public Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        return GetAsync(key, options: null, ct);
    }

    /// <summary>
    /// Gets a cache value with optional per-call TTL overrides.
    /// </summary>
    /// <param name="key">The entry key (without the cache name prefix).</param>
    /// <param name="options">
    /// Optional overrides for this single call. When <c>null</c>, the cache service's default TTLs are used.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<TValue?> GetAsync(string key, CacheGetOptions? options, CancellationToken ct = default)
    {
        ValidateEntryKey(key);
        var entryOptions = ResolveEntryOptions(options);
        return await cache.GetOrCreateAsync(
            // key: fully qualifies the entry with the cache name (e.g. patients:123)
            GetCacheKey(key),
            // state: passes data into the static factory without capturing this method's locals
            (service: this, key),
            // factory: hydrates the value from the source of truth on a cache miss (both L1 and L2)
            static async (state, token) => await state.service.HydrateSingleAsync(state.key, token),
            // options: controls L1 and L2 expiration for the cached value
            entryOptions,
            // tags: groups entries so the whole typed cache can be invalidated at once
            entryTags,
            // cancellationToken: propagates caller cancellation through cache lookup and hydration
            ct);
    }

    /// <summary>
    /// Stores a cache value in L1 and L2 using the cache service's default TTLs.
    /// </summary>
    public async Task SetAsync(string key, TValue value, CancellationToken ct = default)
    {
        ValidateEntryKey(key);
        await cache.SetAsync(GetCacheKey(key), value, defaultEntryOptions, entryTags, ct);
    }

    /// <summary>
    /// Removes a cache value from L1 and L2.
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ValidateEntryKey(key);
        await cache.RemoveAsync(GetCacheKey(key), ct);
    }

    /// <summary>
    /// Loads one value from the source of truth on a cache miss.
    /// Return <c>null</c> (or <c>default</c> for value types) when the entry does not exist in the source.
    /// The null/default outcome is cached for the configured TTL like any other value.
    /// Throw only for system errors (network, DB, etc.) — thrown exceptions are NOT cached.
    /// </summary>
    protected abstract Task<TValue?> HydrateSingleAsync(string key, CancellationToken ct);

    /// <summary>
    /// Loads all values from the source of truth. Override to enable startup refresh and scheduled refresh.
    /// </summary>
    protected virtual Task<IReadOnlyDictionary<string, TValue>> HydrateAllAsync(CancellationToken ct)
    {
        throw new NotSupportedException($"{GetType().Name} does not implement bulk cache hydration.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources held by the cache service.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            stoppingCts.Dispose();
        }

        disposed = true;
    }
}
