using CLOOPS.microservices.Extensions;
using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

public abstract partial class BaseCacheService<TValue>
{
    /// <summary>
    /// Refreshes the entire cache from the source of truth via <see cref="HydrateAllAsync"/>.
    /// Acquires a distributed lock so only one pod hydrates at a time when NATS is configured.
    /// </summary>
    /// <param name="retries">Number of retry attempts when the distributed lock is held by another instance. Defaults to 0.</param>
    /// <param name="throwOnFail">When <c>true</c>, throws if the lock cannot be acquired. Defaults to <c>false</c>.</param>
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
        var lockKey = $"cache-refresh:{CacheName}";
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await using var handle = await natsClient.AcquireDistributedLockAsync(
                lockKey,
                TimeSpan.FromMilliseconds(500),
                ct: ct);

            if (handle != null)
            {
                await ReplaceAllAsync(ct);
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
