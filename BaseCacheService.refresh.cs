using CLOOPS.microservices.Extensions;
using CLOOPS.NATS.Locking;
using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

public abstract partial class BaseCacheService<TValue>
{
    /// <summary>
    /// Refreshes the entire cache from the source of truth via <see cref="HydrateAllAsync"/>.
    /// Acquires a distributed lock so only one pod hydrates at a time when NATS is configured.
    /// </summary>
    /// <param name="retries">Number of retry attempts when the distributed lock is held by another instance. Defaults to 0.</param>
    /// <param name="throwOnFail">
    /// When <c>true</c>, throws if the lock cannot be acquired - when another instance holds it, when
    /// the lock key is invalid, and when the lock service itself fails. When <c>false</c> (the default):
    /// an invalid lock key is logged as an error and the refresh proceeds <i>without</i> a distributed
    /// lock (the key can never work, so blocking forever is pointless), while a failing lock service
    /// is logged as an error and the refresh is <i>skipped</i> until the next scheduled run (the
    /// failure is correlated across pods, so refreshing unlocked would cause a thundering herd).
    /// Neither case fails host startup.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshAllAsync(int retries = 0, bool throwOnFail = false, CancellationToken ct = default)
    {
        EnsureBulkHydrationSupported();

        if (retries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Retries cannot be negative.");
        }

        if (!config.UseDistributedRefreshLock)
        {
            // Per-pod refresh: every instance hydrates independently. Used for caches
            // whose value is intrinsically per-pod (e.g. an L1-only readiness probe).
            await ReplaceAllAsync(ct);
            return;
        }

        if (natsClient == null)
        {
            logger.LogWarning("[{CacheName}]::NATS client is not configured; running cache refresh without a distributed lock", CacheName);
            await ReplaceAllAsync(ct);
            return;
        }

        var attempts = retries + 1;
        var lockKey = GetRefreshLockKey(CacheName);

        // DETERMINISTIC failure: the key is structurally invalid (e.g. the cache name contains a
        // space or ':'), so NATS will reject it on every attempt, forever, on every pod. Blocking
        // refresh permanently over a key that can never work is worse than refreshing unlocked, and
        // crashing the host over a cache refresh is worse still - so log loudly and degrade.
        // This is checked up front, OUTSIDE the acquisition try/catch, precisely so that it cannot
        // be confused with a transient lock failure. Do not merge the two: see the catch below.
        var lockKeyError = NatsKvKey.GetValidationError(lockKey);
        if (lockKeyError != null)
        {
            if (throwOnFail)
            {
                throw new InvalidOperationException($"[{CacheName}]::{lockKeyError}");
            }

            logger.LogError(
                "[{CacheName}]::{CacheRefreshLockKeyError} Running cache refresh WITHOUT a distributed lock; fix the cache name so the lock key only contains characters NATS KV accepts.",
                CacheName,
                lockKeyError);
            await ReplaceAllAsync(ct);
            return;
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            DistributedLockHandle? handle;
            try
            {
                handle = await natsClient.AcquireDistributedLockAsync(
                    lockKey,
                    TimeSpan.FromMilliseconds(500),
                    ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // TRANSIENT failure: the key is known-good, so this is the lock *service* failing -
                // a NATS outage, a missing KV bucket, a timeout. Unlike the deterministic case above
                // this is a CORRELATED failure across the whole fleet: every pod fails at the same
                // moment. Refreshing unlocked here would turn a NATS outage into a thundering herd -
                // every pod hydrating from the source of truth on every cron tick, with matching L2
                // write amplification, exactly when the system is least able to absorb it.
                //
                // So: skip this refresh without hydrating and let the next cron tick retry. Startup
                // still proceeds (entries hydrate on demand through the single-key read-through
                // path) rather than taking the host down over a cache refresh.
                if (throwOnFail)
                {
                    throw;
                }

                logger.LogError(ex, "[{CacheName}]::Could not acquire the cache refresh lock '{CacheRefreshLockKey}'; skipping this refresh", CacheName, lockKey);
                return;
            }

            if (handle != null)
            {
                await using (handle)
                {
                    await ReplaceAllAsync(ct);
                }

                return;
            }

            if (attempt < attempts)
            {
                var retryDelay = GetRefreshRetryDelay();
                logger.LogDebug("[{CacheName}]::Cache refresh lock attempt {RefreshLockAttempt}/{RefreshLockAttempts} failed; retrying in {RefreshLockRetryDelay}", CacheName, attempt, attempts, retryDelay);
                await Task.Delay(retryDelay, ct);
            }
        }

        var message = $"[{CacheName}]::Cache refresh skipped because another instance holds the refresh lock";
        if (throwOnFail)
        {
            throw new InvalidOperationException(message);
        }

        logger.LogDebug("{CacheRefreshSkipReason}", message);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateBulkHydrationRequirements();

        if (config.RefreshOnStartup)
        {
            if (config.UseDistributedRefreshLock)
            {
                if (natsClient != null && !BaseUtil.IsNatsConnected(natsClient))
                {
                    logger.LogInformation("[{CacheName}]::Waiting up to {NatsWaitTimeout} for NATS to connect before startup refresh", CacheName, BaseUtil.NatsConnectionWaitTimeout);
                    if (await BaseUtil.WaitForNatsConnectionAsync(natsClient, cancellationToken).ConfigureAwait(false))
                    {
                        logger.LogInformation("[{CacheName}]::NATS connection established; proceeding with startup refresh", CacheName);
                    }
                    else
                    {
                        logger.LogWarning("[{CacheName}]::Timed out waiting for NATS after {NatsWaitTimeout}; startup refresh will run without a distributed lock", CacheName, BaseUtil.NatsConnectionWaitTimeout);
                    }
                }
            }
            await RefreshAllAsync(retries: 0, throwOnFail: false, ct: cancellationToken);
        }

        var refreshCron = config.RefreshCron;
        if (string.IsNullOrWhiteSpace(refreshCron))
        {
            logger.LogInformation("[{CacheName}]::Refresh cron is not specified: cache will be refreshed on individual keys basis as they expire per their TTL", CacheName);
            return;
        }

        var cronExpression = BaseUtil.GetCronExpression(refreshCron);
        scheduledRefreshTask = RunScheduledRefreshAsync(cronExpression, refreshCron, stoppingCts.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stoppingCts.CancelAsync();

        if (scheduledRefreshTask == null)
        {
            return;
        }

        try
        {
            await scheduledRefreshTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        logger.LogInformation("Stopping {name} cache", config.Name);
    }

    private async Task RunScheduledRefreshAsync(Cronos.CronExpression cronExpression, string refreshCron, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await cronExpression.AwaitUntilNextOccurrenceAsync(logger, refreshCron, GetType().Name, stoppingToken))
                {
                    continue;
                }

                await RefreshAllAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{CacheName}]::Error refreshing cache", CacheName);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task ReplaceAllAsync(CancellationToken ct)
    {
        var values = await HydrateAllAsync(ct);

        // Note: we intentionally do NOT call cache.RemoveByTagAsync here. SetAsync
        // overwrites existing entries by key, so reads remain consistent throughout the
        // refresh. Entries whose keys are no longer in the source of truth will linger
        // until L2 TTL expires - this is an accepted trade-off for read consistency.
        // See docs/caching.md > Design Notes for the rationale.
        foreach (var entry in values)
        {
            ValidateEntryKey(entry.Key);
            await cache.SetAsync(GetCacheKey(entry.Key), entry.Value, defaultEntryOptions, entryTags, ct);
        }

        logger.LogDebug("[{CacheName}]::Refreshed {CacheEntryCount} cache entries", CacheName, values.Count);
    }

    private static TimeSpan GetRefreshRetryDelay()
    {
        return TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
    }
}
