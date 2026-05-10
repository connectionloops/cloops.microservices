namespace CLOOPS.microservices;

/// <summary>
/// Per-call overrides for cache lookup behavior.
/// Pass to <see cref="BaseCacheService{TValue}.GetAsync(string, CacheGetOptions, System.Threading.CancellationToken)"/>
/// when a specific read should use a different TTL than the cache service's defaults.
/// </summary>
/// <example>
/// <code>
/// // Cache this specific entry for only 30 seconds in L1 and 5 minutes in L2.
/// var patient = await patients.GetAsync(
///     id,
///     new CacheGetOptions { L1Ttl = TimeSpan.FromSeconds(30), L2Ttl = TimeSpan.FromMinutes(5) },
///     ct);
/// </code>
/// </example>
public sealed class CacheGetOptions
{
    /// <summary>
    /// Overrides the cache service's <c>L1Ttl</c> for this single lookup if set.
    /// When <c>null</c>, the default <c>L1Ttl</c> is used.
    /// </summary>
    public TimeSpan? L1Ttl { get; init; }

    /// <summary>
    /// Overrides the cache service's <c>L2Ttl</c> for this single lookup if set.
    /// When <c>null</c>, the default <c>L2Ttl</c> is used.
    /// </summary>
    public TimeSpan? L2Ttl { get; init; }
}
