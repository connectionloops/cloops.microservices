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
    [Fact]
    public async Task RefreshAllAsync_WhenLockServiceThrows_DegradesToAnUnlockedRefresh()
    {
        var service = CreateService(out var hydrated, lockFailure: new NatsKVException("Key contains invalid characters"));

        await service.RefreshAllAsync();

        Assert.Equal(1, hydrated.Count);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenLockServiceThrows_AndThrowOnFail_StillThrows()
    {
        var failure = new NatsKVException("Key contains invalid characters");
        var service = CreateService(out var hydrated, lockFailure: failure);

        var thrown = await Assert.ThrowsAsync<NatsKVException>(
            () => service.RefreshAllAsync(throwOnFail: true));

        Assert.Same(failure, thrown);
        Assert.Equal(0, hydrated.Count);
    }

    [Fact]
    public async Task StartAsync_WhenLockServiceThrows_DoesNotCrashHostStartup()
    {
        var service = CreateService(out var hydrated, lockFailure: new NatsKVException("Key contains invalid characters"));

        // RefreshOnStartup is true on the test cache; this is the exact path that used to take the
        // whole host down.
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

        return new TestCacheService(services.BuildServiceProvider());
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
}
