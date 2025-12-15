# Distributed Locks

Distributed locks provide a mechanism to ensure that only one instance of your service can execute a critical section of code at a time, even when running multiple instances in a distributed environment. This is essential for preventing race conditions, duplicate processing, and ensuring data consistency across your microservices. Since these microservices are essentially distributed systems to perform locking we need a centralized system to keep track of locks. We use JetStream streams for this.

## Overview

The distributed locking system in `cloops.microservices` is built on top of NATS JetStream Key-Value stores. It provides:

- **Automatic lock renewal**: Locks are automatically renewed in the background to prevent expiration during long-running operations
- **Automatic cleanup**: Locks are automatically released when the handle is disposed
- **Retry mechanism**: Built-in retry logic with jittered backoff when acquiring locks
- **Expired lock detection**: Automatically detects and steals expired locks from crashed instances

## Basic Usage

The simplest way to use distributed locks is to inject `ICloopsNatsClient` into your service and call `AcquireDistributedLockAsync`:

```cs
using CLOOPS.NATS;
using Microsoft.Extensions.Hosting;

namespace your.namespace.services.background;

public class ScheduledTaskService : BackgroundService
{
    private readonly ICloopsNatsClient _client;
    private readonly ILogger<ScheduledTaskService> _logger;

    public ScheduledTaskService(ICloopsNatsClient client, ILogger<ScheduledTaskService> logger)
    {
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handle = await _client.AcquireDistributedLockAsync(
                "my-resource-key",
                TimeSpan.FromMilliseconds(500),
                ct: stoppingToken
            );

            if (handle is null)
            {
                // Lock acquisition failed - another instance holds it
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            await using (handle)
            {
                try
                {
                    // Critical section - only this instance can execute this code
                    await PerformScheduledTask(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scheduled task execution");
                }
            }
            // Lock is automatically released here when handle is disposed
        }
    }

    private async Task PerformScheduledTask(CancellationToken ct)
    {
        // Your critical code here
        await Task.Delay(1000, ct);
    }
}
```

## API Reference

### AcquireDistributedLockAsync

```cs
Task<DistributedLockHandle?> AcquireDistributedLockAsync(
    string key,
    TimeSpan? timeout = null,
    string? ownerId = null,
    CancellationToken ct = default
)
```

#### Parameters

- **`key`** (required): A unique identifier for the lock. This should be descriptive and unique for the resource you're protecting (e.g., `"cljps.scheduling"`, `"database.migration"`).

- **`timeout`** (optional): Maximum time to spend trying to acquire the lock. Defaults to `1.5 * AcquireRetryMaxDelay` (approximately 750ms). If the lock cannot be acquired within this time, the method returns `null`.

- **`ownerId`** (optional): An identifier for the instance acquiring the lock. Defaults to `"{MachineName}:{ProcessId}"`. Useful for debugging and monitoring which instance holds a lock.

- **`ct`** (optional): Cancellation token to cancel the lock acquisition operation.

#### Return Value

- **`DistributedLockHandle?`**: Returns a `DistributedLockHandle` if the lock was successfully acquired, or `null` if the lock could not be acquired within the timeout period.

#### Important Notes

- The lock handle implements `IAsyncDisposable` and **must** be disposed using `await using` to ensure proper cleanup
- If you don't dispose the handle, the lock will eventually expire (default: 20 seconds), but it's best practice to always dispose it explicitly
- The lock is automatically renewed in the background while you hold the handle

## Common Patterns

### Pattern 1: Scheduled Background Tasks

Use distributed locks to ensure only one instance runs scheduled tasks (e.g., cron jobs):

```cs
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var cronExpression = Util.GetCronExpression("0 */5 * * * *"); // Every 5 minutes

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            // Wait until next scheduled time
            if (!await cronExpression.AwaitUntilNextOccurrenceAsync(
                _logger,
                "0 */5 * * * *",
                "ScheduledTaskService",
                stoppingToken))
            {
                continue;
            }

            // Try to acquire lock
            var handle = await _client.AcquireDistributedLockAsync(
                "scheduled.task.key",
                TimeSpan.FromMilliseconds(500),
                ct: stoppingToken
            );

            if (handle is null)
            {
                // Another instance is running the task
                _logger.LogDebug("Scheduled task already running on another instance");
                continue;
            }

            await using (handle)
            {
                try
                {
                    await RunScheduledTask(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled task");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break;
        }
    }
}
```

### Pattern 2: Retry on Lock Failure

If you need to retry acquiring a lock with exponential backoff:

```cs
private async Task<bool> TryAcquireLockWithRetry(string lockKey, CancellationToken ct)
{
    var maxRetries = 5;
    var baseDelay = TimeSpan.FromSeconds(1);

    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        var handle = await _client.AcquireDistributedLockAsync(
            lockKey,
            TimeSpan.FromMilliseconds(500),
            ct: ct
        );

        if (handle is not null)
        {
            // Store handle for later use or use it immediately
            return true;
        }

        // Exponential backoff: 1s, 2s, 4s, 8s, 16s
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        _logger.LogWarning("Failed to acquire lock {LockKey} on attempt {Attempt}, retrying in {Delay}ms",
            lockKey, attempt + 1, delay.TotalMilliseconds);

        await Task.Delay(delay, ct);
    }

    return false;
}
```

### Pattern 3: Long-Running Operations

For operations that may take a long time, the lock will automatically renew itself:

```cs
var handle = await _client.AcquireDistributedLockAsync(
    "long.running.operation",
    TimeSpan.FromSeconds(5),  // Longer timeout for initial acquisition
    ct: stoppingToken
);

if (handle is null)
{
    _logger.LogWarning("Could not acquire lock for long-running operation");
    return;
}

await using (handle)
{
    // This operation might take 10 minutes
    // The lock will automatically renew every 10 seconds
    // Lock lease duration is 20 seconds, so we have a 10-second safety margin
    await ProcessLargeDataset(stoppingToken);
}
```

### Pattern 4: Multiple Resource Locks

You can acquire multiple locks independently:

```cs
var lock1 = await _client.AcquireDistributedLockAsync("resource-a", ct: ct);
var lock2 = await _client.AcquireDistributedLockAsync("resource-b", ct: ct);

if (lock1 is null || lock2 is null)
{
    // Release any acquired locks
    if (lock1 is not null) await lock1.DisposeAsync();
    if (lock2 is not null) await lock2.DisposeAsync();

    _logger.LogWarning("Could not acquire all required locks");
    return;
}

await using (lock1)
await using (lock2)
{
    // Both resources are locked
    await ProcessWithMultipleResources(ct);
}
```

> ⚠️ **Warning**: Be careful with multiple locks to avoid deadlocks. Always acquire locks in the same order across your codebase, or use a timeout and release strategy.

## How It Works

### Lock Acquisition

1. The system attempts to create a lock entry in the NATS KV store
2. If the lock already exists, it checks if it's expired
3. If expired, it attempts to "steal" the lock using compare-and-swap (CAS) operations
4. If not expired, it retries with jittered backoff
5. The process continues until the lock is acquired or the timeout is reached

### Lock Renewal

- Once acquired, the lock handle automatically starts a background renewal loop
- The lock is renewed every 10 seconds (default `RenewInterval`)
- The lock lease duration is 20 seconds (default `LeaseDuration`)
- This provides a 10-second safety margin in case of network issues

### Lock Release

- Locks are automatically released when the `DistributedLockHandle` is disposed
- The release uses compare-and-swap to ensure you only release your own lock
- If the lock has already been stolen or expired, the release is treated as successful

### Expired Lock Detection

- Locks have a lease duration (default: 20 seconds)
- If an instance crashes while holding a lock, the lock will expire after the lease duration
- Other instances can then acquire the lock by detecting the expiration and stealing it

## Configuration

The distributed lock system uses the following default configuration (via `KvDistributedLockOptions`):

- **Bucket Name**: `"locks"` (NATS KV bucket name)
- **Lease Duration**: `20 seconds` (how long the lock is valid before expiration)
- **Renew Interval**: `10 seconds` (how often to renew the lock)
- **Acquire Retry Base Delay**: `50ms` (minimum delay between retry attempts)
- **Acquire Retry Max Delay**: `500ms` (maximum delay between retry attempts)
- **Owner ID**: `"{MachineName}:{ProcessId}"` (default identifier for the lock owner)

These defaults are suitable for most use cases. The automatic renewal ensures that long-running operations won't lose their locks, while the retry mechanism with jitter helps prevent thundering herd problems.

## Best Practices

1. **Always use `await using`**: Always dispose lock handles using `await using` to ensure proper cleanup:

   ```cs
   var handle = await _client.AcquireDistributedLockAsync("key", ct: ct);
   if (handle is null) return;

   await using (handle)
   {
       // Your critical code
   }
   ```

2. **Handle null returns**: Always check if the lock acquisition returned `null` and handle it appropriately:

   ```cs
   if (handle is null)
   {
       // Another instance has the lock
       // Decide: retry, skip, or fail
       return;
   }
   ```

3. **Use descriptive lock keys**: Use clear, hierarchical names for lock keys:

   ```cs
   // Good
   "cljps.scheduling"
   "database.migration.v1"
   "cache.warmup.users"

   // Bad
   "lock1"
   "temp"
   "x"
   ```

4. **Set appropriate timeouts**: Use longer timeouts for operations that may take time to acquire the lock:

   ```cs
   // Quick operation - short timeout
   var handle = await _client.AcquireDistributedLockAsync(
       "quick.task",
       TimeSpan.FromMilliseconds(500),
       ct: ct
   );

   // Important operation - longer timeout
   var handle = await _client.AcquireDistributedLockAsync(
       "critical.migration",
       TimeSpan.FromSeconds(5),
       ct: ct
   );
   ```

5. **Use owner IDs for debugging**: Specify custom owner IDs to make it easier to identify which instance holds a lock:

   ```cs
   var handle = await _client.AcquireDistributedLockAsync(
       "scheduled.task",
       ownerId: $"{Environment.MachineName}:scheduler-service",
       ct: ct
   );
   ```

6. **Respect cancellation tokens**: Always pass cancellation tokens to allow graceful shutdown:

   ```cs
   var handle = await _client.AcquireDistributedLockAsync(
       "task",
       ct: stoppingToken  // Allows service to shut down gracefully
   );
   ```

7. **Don't hold locks too long**: While locks auto-renew, try to keep critical sections as short as possible to reduce contention.

8. **Handle exceptions in critical sections**: Wrap your critical code in try-catch to ensure the lock is released even if an exception occurs:

   ```cs
   await using (handle)
   {
       try
       {
           await CriticalOperation(ct);
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Error in critical operation");
           // Lock will still be released by dispose
       }
   }
   ```

## Troubleshooting

### Lock Acquisition Always Returns Null

- **Check if another instance is holding the lock**: Another instance might be running and holding the lock
- **Check NATS connectivity**: Ensure your NATS connection is working properly
- **Increase timeout**: The default timeout might be too short for your use case
- **Check for expired locks**: If an instance crashed, wait for the lease duration (20 seconds) before the lock can be acquired

### Lock Expires Unexpectedly

- **Check network connectivity**: Network issues can prevent lock renewal
- **Check NATS server health**: Ensure the NATS server is running and accessible
- **Review lease duration**: The default 20-second lease might be too short for very long operations

### Multiple Instances Running the Same Task

- **Verify lock key uniqueness**: Ensure all instances use the same lock key
- **Check lock acquisition logic**: Ensure you're checking for `null` and handling it correctly
- **Review timeout values**: Very short timeouts might cause all instances to fail to acquire the lock

## Related Documentation

- [Services](./services.md) - Learn about background services where distributed locks are commonly used
- [Making API Calls](./api.calls.md) - See how `ICloopsNatsClient` is used in other contexts
- [NATS Client Documentation](../cloops.nats/docs/CurrentFunctionality.md) - Detailed technical documentation on the underlying NATS client
