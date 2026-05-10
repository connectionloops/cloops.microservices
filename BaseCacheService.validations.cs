using System.Reflection;

namespace CLOOPS.microservices;

public abstract partial class BaseCacheService<TValue>
{
    private static void ValidateConfig(CacheConfigAttribute config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("[CacheConfig] Name cannot be empty.");
        }

        if (config.Name.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("[CacheConfig] Name cannot contain ':'.");
        }

        if (config.L1Ttl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("[CacheConfig] L1Ttl must be positive.");
        }

        if (config.L2Ttl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("[CacheConfig] L2Ttl must be positive.");
        }
    }

    private void ValidateBulkHydrationRequirements()
    {
        if (!string.IsNullOrWhiteSpace(config.RefreshCron) && !hasBulkHydration.Value)
        {
            throw new InvalidOperationException(
                $"{GetType().FullName} must override HydrateAllAsync when [CacheConfig.RefreshCron] is set.");
        }

        if (config.RefreshOnStartup && !hasBulkHydration.Value)
        {
            throw new InvalidOperationException(
                $"{GetType().FullName} must override HydrateAllAsync when [CacheConfig.RefreshOnStartup = true].");
        }
    }

    private void ValidateEntryKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        if (key.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Cache key cannot contain ':'.", nameof(key));
        }
    }

    private void EnsureBulkHydrationSupported()
    {
        if (!hasBulkHydration.Value)
        {
            throw new InvalidOperationException(
                $"{GetType().FullName} does not support bulk cache refresh. Override HydrateAllAsync to enable RefreshCron / RefreshOnStartup / explicit RefreshAllAsync calls.");
        }
    }

    private bool ComputeHasBulkHydration()
    {
        var method = GetType().GetMethod(
            nameof(HydrateAllAsync),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        return method != null && method.DeclaringType != typeof(BaseCacheService<TValue>);
    }
}
