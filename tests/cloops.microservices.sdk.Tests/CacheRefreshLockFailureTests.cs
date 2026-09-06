using CLOOPS.microservices;
using CLOOPS.NATS;
using CLOOPS.NATS.Locking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Covers what happens when the distributed lock service itself fails (invalid key, missing KV
/// bucket, NATS error) during a bulk cache refresh. Before this fix the exception propagated out
/// of <c>RefreshAllAsync</c> through <c>StartAsync</c> and crashed host startup.
/// </summary>
public class CacheRefreshLockFailureTests
{
    // ---------------------------------------------------------------------------------------
    // TRANSIENT lock-service failure (NATS outage, missing bucket, timeout).
    // Correlated across the whole fleet, so the refresh is SKIPPED without hydrating: refreshing
    // unlocked here would turn a NATS outage into a thundering herd of full hydrations across
    // every pod on every cron tick.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_WhenLockServiceThrows_SkipsTheRefreshWithoutHydrating()
    {
        var service = CreateService(out var hydrated, lockFailure: new NatsKVException("NATS is unreachable"));

        await service.RefreshAllAsync();

        Assert.Equal(0, hydrated.Count);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenLockServiceThrows_AndThrowOnFail_StillThrows()
    {
        var failure = new NatsKVException("NATS is unreachable");
        var service = CreateService(out var hydrated, lockFailure: failure);

        var thrown = await Assert.ThrowsAsync<NatsKVException>(
            () => service.RefreshAllAsync(throwOnFail: true));

        Assert.Same(failure, thrown);
        Assert.Equal(0, hydrated.Count);
    }

    [Fact]
    public async Task StartAsync_WhenLockServiceThrows_DoesNotCrashHostStartup()
    {
        var service = CreateService(out var hydrated, lockFailure: new NatsKVException("NATS is unreachable"));

        // RefreshOnStartup is true on the test cache; this is the exact path that used to take the
        // whole host down. Startup now proceeds; entries hydrate on demand via the single-key
        // read-through path instead.
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, hydrated.Count);
    }

    // ---------------------------------------------------------------------------------------
    // DETERMINISTIC key failure: the cache name yields a key NATS can never accept.
    // ValidateConfig only rejects ':' in a cache name, so a name containing a space still
    // produces an invalid lock key. A malformed key fails identically forever, so blocking
    // refresh permanently is pointless - degrade to an unlocked refresh and log loudly.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_WhenCacheNameYieldsAnInvalidLockKey_DegradesToAnUnlockedRefresh()
    {
        var service = CreateInvalidKeyService(out var hydrated, out var acquiredKeys);

        await service.RefreshAllAsync();

        Assert.Equal(1, hydrated.Count);
        // The invalid key is rejected before it ever reaches NATS.
        Assert.Empty(acquiredKeys);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenCacheNameYieldsAnInvalidLockKey_AndThrowOnFail_Throws()
    {
        var service = CreateInvalidKeyService(out var hydrated, out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RefreshAllAsync(throwOnFail: true));

        Assert.Contains("cache-refresh.bad cache name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("' '", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, hydrated.Count);
    }

    [Fact]
    public async Task StartAsync_WhenCacheNameYieldsAnInvalidLockKey_DoesNotCrashHostStartup()
    {
        var service = CreateInvalidKeyService(out var hydrated, out _);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, hydrated.Count);
    }

    [Fact]
    public async Task RefreshAllAsync_UsesADotSeparatedLockKey()
    {
        string? observedKey = null;
        var service = CreateService(
            out _,
            lockFailure: null,
            onAcquire: key => observedKey = key);

        await service.RefreshAllAsync();

        Assert.Equal("cache-refresh.test-cache", observedKey);
        Assert.True(NatsKvKey.IsValid(observedKey));
    }

    [Fact]
    public async Task RefreshAllAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = CreateService(out _, lockFailure: new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAllAsync(ct: cts.Token));
    }

    private static TestCacheService CreateService(
        out HydrationCounter hydrated,
        Exception? lockFailure,
        Action<string>? onAcquire = null)
        => new(BuildProvider(out hydrated, lockFailure, onAcquire));

    private static InvalidKeyCacheService CreateInvalidKeyService(
        out HydrationCounter hydrated,
        out List<string> acquiredKeys)
    {
        var keys = new List<string>();
        acquiredKeys = keys;
        return new InvalidKeyCacheService(BuildProvider(out hydrated, lockFailure: null, onAcquire: keys.Add));
    }

    private static ServiceProvider BuildProvider(
        out HydrationCounter hydrated,
        Exception? lockFailure,
        Action<string>? onAcquire = null)
    {
        var connection = new Mock<INatsConnection>();
        connection.SetupGet(c => c.ConnectionState).Returns(NatsConnectionState.Open);

        var natsClient = new Mock<ICloopsNatsClient>();
        natsClient.SetupGet(c => c.Connection).Returns(connection.Object);
        natsClient
            .Setup(c => c.AcquireDistributedLockAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, TimeSpan? _, string? _, CancellationToken _) =>
            {
                onAcquire?.Invoke(key);
                return lockFailure != null
                    ? Task.FromException<DistributedLockHandle?>(lockFailure)
                    // A null handle means "another instance holds the lock"; returning it here keeps
                    // the happy path out of the real NATS KV store while still exercising the key.
                    : Task.FromResult<DistributedLockHandle?>(null);
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton(natsClient.Object);

        var counter = new HydrationCounter();
        services.AddSingleton(counter);
        hydrated = counter;

        return services.BuildServiceProvider();
    }

    private sealed class HydrationCounter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);
    }

    [CacheConfig("test-cache", "00:05:00", "01:00:00", RefreshOnStartup = true)]
    private sealed class TestCacheService(IServiceProvider serviceProvider)
        : BaseCacheService<string>(serviceProvider)
    {
        private readonly HydrationCounter counter = serviceProvider.GetRequiredService<HydrationCounter>();

        protected override Task<string?> HydrateSingleAsync(string key, CancellationToken ct)
            => Task.FromResult<string?>(key);

        protected override Task<IReadOnlyDictionary<string, string>> HydrateAllAsync(CancellationToken ct)
        {
            counter.Increment();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { ["a"] = "1" });
        }
    }

    /// <summary>
    /// A cache name containing a space. <c>ValidateConfig</c> only rejects ':', so this is accepted
    /// at construction but yields the NATS-invalid lock key "cache-refresh.bad cache name".
    /// </summary>
    [CacheConfig("bad cache name", "00:05:00", "01:00:00", RefreshOnStartup = true)]
    private sealed class InvalidKeyCacheService(IServiceProvider serviceProvider)
        : BaseCacheService<string>(serviceProvider)
    {
        private readonly HydrationCounter counter = serviceProvider.GetRequiredService<HydrationCounter>();

        protected override Task<string?> HydrateSingleAsync(string key, CancellationToken ct)
            => Task.FromResult<string?>(key);

        protected override Task<IReadOnlyDictionary<string, string>> HydrateAllAsync(CancellationToken ct)
        {
            counter.Increment();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { ["a"] = "1" });
        }
    }
}
