using System.Globalization;

namespace CLOOPS.microservices;

/// <summary>
/// Declares cache configuration for a <see cref="BaseCacheService{TValue}"/> implementation.
/// Apply this attribute to every cache service class; it is required and read once at startup.
/// </summary>
/// <example>
/// <code>
/// [CacheConfig(
///     name: "patient-cache",
///     l1Ttl: "00:05:00",
///     l2Ttl: "01:00:00",
///     RefreshCron = "0 */15 * * * *")]
/// public class PatientCacheService : BaseCacheService&lt;Patient&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class CacheConfigAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="CacheConfigAttribute"/>.
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
    /// Stable cache name used in cache keys (prefix) and HybridCache tags.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Local in-process cache TTL.
    /// </summary>
    public TimeSpan L1Ttl { get; }

    /// <summary>
    /// Distributed cache TTL.
    /// </summary>
    public TimeSpan L2Ttl { get; }

    /// <summary>
    /// Optional cron expression that periodically triggers <c>HydrateAllAsync</c>.
    /// Requires the cache service to override <c>HydrateAllAsync</c>.
    /// </summary>
    public string? RefreshCron { get; set; }

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
