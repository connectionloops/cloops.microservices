using System.Globalization;

namespace CLOOPS.microservices;

/// <summary>
/// Declares cache configuration for a <see cref="BaseCacheService{TValue}"/> implementation.
/// Apply this attribute to every cache service class; it is required and read once at startup.
/// </summary>
/// <example>
/// <code>
/// // L1 + L2 cache (Redis used as L2 when REDIS_CONNECTION_STRING is set):
/// [CacheConfig(
///     name: "patient-cache",
///     l1Ttl: "00:05:00",
///     l2Ttl: "01:00:00",
///     RefreshCron = "0 */15 * * * *")]
/// public class PatientCacheService : BaseCacheService&lt;Patient&gt; { ... }
///
/// // L1-only cache (no L2 even when Redis is configured):
/// [CacheConfig(
///     name: "tigerbeetle-readiness",
///     l1Ttl: "00:05:00",
///     RefreshCron = "0 */4 * * * *")]
/// public class TigerBeetleReadinessCacheService : BaseCacheService&lt;bool&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class CacheConfigAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="CacheConfigAttribute"/> with both L1 and L2 TTLs.
    /// <see cref="EnableL2"/> defaults to <c>true</c>; the distributed cache is only activated
    /// when <see cref="EnableL2"/> is <c>true</c> AND a Redis connection string is configured.
    /// </summary>
    /// <param name="name">Stable cache name used in cache keys and tags. Must be non-empty and cannot contain ':'.</param>
    /// <param name="l1Ttl">Local in-process cache TTL, in <see cref="TimeSpan"/> string format (e.g. "00:05:00" for 5 minutes).</param>
    /// <param name="l2Ttl">Distributed cache TTL, in <see cref="TimeSpan"/> string format (e.g. "01:00:00" for 1 hour).</param>
    public CacheConfigAttribute(string name, string l1Ttl, string l2Ttl)
    {
        Name = name;
        L1Ttl = ParseTimeSpan(l1Ttl, nameof(l1Ttl));
        L2Ttl = ParseTimeSpan(l2Ttl, nameof(l2Ttl));
    }

    /// <summary>
    /// Initializes a new <see cref="CacheConfigAttribute"/> for an L1-only cache.
    /// Sets <see cref="EnableL2"/> to <c>false</c>, so the distributed cache is not used
    /// even when a Redis connection string is configured.
    /// </summary>
    /// <param name="name">Stable cache name used in cache keys and tags. Must be non-empty and cannot contain ':'.</param>
    /// <param name="l1Ttl">Local in-process cache TTL, in <see cref="TimeSpan"/> string format (e.g. "00:05:00" for 5 minutes).</param>
    public CacheConfigAttribute(string name, string l1Ttl)
        : this(name, l1Ttl, l1Ttl)
    {
        EnableL2 = false;
    }

    /// <summary>
    /// Stable cache name used in cache keys (prefix) and HybridCache tags.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Local in-process cache TTL.
    /// </summary>
    public TimeSpan L1Ttl { get; }

    /// <summary>
    /// Distributed cache TTL. Ignored when <see cref="EnableL2"/> is <c>false</c>.
    /// </summary>
    public TimeSpan L2Ttl { get; }

    /// <summary>
    /// Whether this cache service may use a distributed L2 cache (Redis).
    /// Defaults to <c>true</c>. Set to <c>false</c> to force L1-only behavior even
    /// when <c>REDIS_CONNECTION_STRING</c> is configured.
    /// The distributed cache is only effectively used when both this flag is <c>true</c>
    /// AND a Redis connection string is configured at the host level.
    /// </summary>
    public bool EnableL2 { get; set; } = true;

    /// <summary>
    /// Optional cron expression that periodically triggers <c>HydrateAllAsync</c>.
    /// Requires the cache service to override <c>HydrateAllAsync</c>.
    /// </summary>
    public string? RefreshCron { get; set; }

    /// <summary>
    /// Whether bulk refresh should acquire a NATS-backed distributed lock so only one
    /// pod hydrates per refresh cycle. Derived from <see cref="EnableL2"/>:
    /// <list type="bullet">
    ///   <item><c>EnableL2 = true</c>: refresh updates shared distributed state, so the
    ///     distributed lock is used to avoid every pod hammering the source of truth.</item>
    ///   <item><c>EnableL2 = false</c>: refresh updates per-pod L1 state only, so every
    ///     pod must hydrate independently on every cron tick.</item>
    /// </list>
    /// </summary>
    public bool UseDistributedRefreshLock => EnableL2;

    /// <summary>
    /// When <c>true</c>, the cache will run one best-effort bulk refresh during host startup.
    /// Startup hydration BLOCKS host startup until it completes (or until another instance
    /// is found holding the refresh lock). Use only for small reference datasets.
    /// Requires the cache service to override <c>HydrateAllAsync</c>.
    /// </summary>
    public bool RefreshOnStartup { get; set; }

    private static TimeSpan ParseTimeSpan(string value, string paramName)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException(
                $"Could not parse '{value}' as a TimeSpan. Use a format like '00:05:00' (5 minutes) or '01:00:00' (1 hour).",
                paramName);
        }

        return parsed;
    }
}
