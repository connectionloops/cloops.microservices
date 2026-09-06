using CLOOPS.microservices;
using NATS.Client.KeyValueStore;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Regression tests for the cache-refresh distributed-lock key. The key used to be built as
/// <c>cache-refresh:{CacheName}</c>; ':' is not a legal NATS KV key character, so acquiring the
/// lock threw <c>NatsKVException</c> and crashed host startup.
/// </summary>
public class CacheRefreshLockKeyTests
{
    [Theory]
    [InlineData("patients", "cache-refresh.patients")]
    [InlineData("patient-cache", "cache-refresh.patient-cache")]
    [InlineData("tigerbeetle-readiness", "cache-refresh.tigerbeetle-readiness")]
    public void GetRefreshLockKey_JoinsWithAPeriod(string cacheName, string expected)
    {
        Assert.Equal(expected, BaseCacheService<string>.GetRefreshLockKey(cacheName));
    }

    [Theory]
    [InlineData("patients")]
    [InlineData("patient-cache")]
    [InlineData("tigerbeetle-readiness")]
    [InlineData("some_cache=v2")]
    public void GetRefreshLockKey_ProducesAValidNatsKvKey(string cacheName)
    {
        var key = BaseCacheService<string>.GetRefreshLockKey(cacheName);

        Assert.DoesNotContain(':', key);
        Assert.True(NatsKvKey.IsValid(key), NatsKvKey.GetValidationError(key));
        Assert.True(NatsKVStore.IsValidKey(key).Success);
    }
}
