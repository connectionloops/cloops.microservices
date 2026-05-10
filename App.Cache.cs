using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System.Reflection;

namespace CLOOPS.microservices;

public partial class App
{
    /// <summary>
    /// Configures HybridCache (L1 in-memory + optional Redis L2) for the host.
    /// Redis is wired up first so HybridCache picks it up as the distributed cache
    /// once it resolves IDistributedCache from DI.
    /// </summary>
    private void ConfigureCaching()
    {
        if (!string.IsNullOrWhiteSpace(appSettings.RedisConnectionString))
        {
            var redisOptions = ConfigurationOptions.Parse(appSettings.RedisConnectionString);
            var instanceName = !string.IsNullOrWhiteSpace(appSettings.RedisInstanceName)
                ? appSettings.RedisInstanceName
                : $"{appSettings.AssemblyName}:";

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = redisOptions;
                options.InstanceName = instanceName;
            });

            Console.WriteLine($"Configured HybridCache with Redis L2 cache (instance prefix: {instanceName})");
        }
        else
        {
            Console.WriteLine("Configured HybridCache with L1 memory cache only (REDIS_CONNECTION_STRING not set)");
        }

        builder.Services.AddHybridCache();
    }

    /// <summary>
    /// Scans the target assembly and registers all cache services in DI.
    /// A cache service is any non-abstract, non-generic-open class that inherits from
    /// <see cref="BaseCacheService{TValue}"/> and is decorated with <see cref="CacheConfigAttribute"/>.
    /// </summary>
    private void RegisterCacheServices()
    {
        var cacheServiceTypes = GetTargetTypes()
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith(".Cache", StringComparison.OrdinalIgnoreCase);
            })
            .Where(t => !t.IsGenericTypeDefinition)
            .Where(t => BaseUtil.IsAssignableToOpenGeneric(t, typeof(BaseCacheService<>)))
            .ToArray();

        ValidateCacheServiceConfigs(cacheServiceTypes);

        foreach (var cacheServiceType in cacheServiceTypes)
        {
            builder.Services.AddSingleton(cacheServiceType);

            var interfaceType = FindInterface(cacheServiceType);
            if (interfaceType != null)
            {
                // add an alias for interface so we can resolve via cache service interface
                builder.Services.AddSingleton(interfaceType, sp => sp.GetRequiredService(cacheServiceType));
                Console.WriteLine($"Registered cache service: {interfaceType.Name} -> {cacheServiceType.Name}");
            }
            else
            {
                Console.WriteLine($"Registered cache service: {cacheServiceType.Name}");
            }

            // make it a hosted service and point to same instance
            builder.Services.AddSingleton(typeof(IHostedService), sp => (IHostedService)sp.GetRequiredService(cacheServiceType));
            Console.WriteLine($"Registered cache hosted service: {cacheServiceType.Name}");
        }
    }

    /// <summary>
    /// Ensures every cache service declares <see cref="CacheConfigAttribute"/> and that no two
    /// services share the same cache name (which would cause key prefix and tag collisions).
    /// </summary>
    private static void ValidateCacheServiceConfigs(IReadOnlyCollection<Type> cacheServiceTypes)
    {
        var seenNames = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var cacheServiceType in cacheServiceTypes)
        {
            var attribute = cacheServiceType.GetCustomAttribute<CacheConfigAttribute>(inherit: true);
            if (attribute == null)
            {
                throw new InvalidOperationException(
                    $"{cacheServiceType.FullName} inherits from BaseCacheService<> but is missing the required [CacheConfig(...)] attribute.");
            }

            if (string.IsNullOrWhiteSpace(attribute.Name))
            {
                throw new InvalidOperationException(
                    $"{cacheServiceType.FullName} has [CacheConfig] with an empty Name.");
            }

            if (seenNames.TryGetValue(attribute.Name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Cache name collision: '{attribute.Name}' is declared by both {existing.FullName} and {cacheServiceType.FullName}.");
            }

            seenNames.Add(attribute.Name, cacheServiceType);
        }
    }
}
