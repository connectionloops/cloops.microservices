# Background Jobs (cron based)

Background jobs (cron based) are long-running hosted services that start with your microservice and keep running until the host shuts down. Use them for scheduled cleanup, periodic sync, heartbeats, queue polling, cache maintenance, and other work that should happen outside a NATS request handler.

The SDK uses standard .NET hosted services. In most cases, create a class that inherits from `BackgroundService`, put it in a namespace ending with `Services.Background`, and let `cloops.microservices` register it automatically.

## Quick Start

Create the job under your service project, typically in `src/services/background/`:

```csharp
using CLOOPS.microservices.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace template.services.background;

public sealed class AliveNudge : BackgroundService
{
    private readonly ILogger<AliveNudge> logger;
    private readonly AppSettings appSettings;

    public AliveNudge(ILogger<AliveNudge> logger, AppSettings appSettings)
    {
        this.logger = logger;
        this.appSettings = appSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!appSettings.EnableAliveNudge)
        {
            logger.LogInformation("[AliveNudge]::service disabled by configuration");
            return;
        }

        var cronExpression = Util.GetCronExpression(appSettings.AliveNudgeCron);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await cronExpression.AwaitUntilNextOccurrenceAsync(
                    logger,
                    appSettings.AliveNudgeCron,
                    nameof(AliveNudge),
                    stoppingToken))
                {
                    continue;
                }
                // your logic
                logger.LogInformation("[AliveNudge]::service executing nudge");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("[AliveNudge]::service shutting down");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AliveNudge]::error executing loop");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}
```

The template includes this pattern in `template/src/services/background/alive.nudge.cs`.

## Registration Convention

Background jobs are discovered from the application assembly during `new App()` startup. A class is registered as a background job when:

- Its namespace ends with `Services.Background`, case-insensitive. For example, `my.service.services.background` works.
- It implements `IHostedService`. Inheriting from `BackgroundService` is the usual path.
- The class is concrete, non-abstract, non-nested, and part of the target app assembly.

You do not need to call `AddHostedService` yourself for convention-based jobs.

Hosted services start in registration order. User background jobs are registered after NATS, database migrations, TigerBeetle setup, and cache services. This means scheduled jobs start after migrations and startup cache hydration have had a chance to run.

## Configuration

Put job settings in your app's `AppSettings` class so they can be controlled by Doppler or environment variables.

```csharp
public class AppSettings : BaseAppSettings
{
    public bool EnableAliveNudge { get; init; } =
        Convert.ToBoolean(Environment.GetEnvironmentVariable("ENABLE_ALIVE_NUDGE") ?? "True");

    public string AliveNudgeCron { get; init; } =
        Environment.GetEnvironmentVariable("ALIVE_NUDGE_CRON") ?? "*/30 * * * * *";
}
```

Recommended settings for each job:

- An enable flag, such as `ENABLE_ALIVE_NUDGE`, so the job can be disabled without a code change.
- A cron expression, such as `ALIVE_NUDGE_CRON`, for schedule changes without a deploy.
- Any timeout, batch size, or retention values needed by the job.

`Util.GetCronExpression` accepts both five-field cron expressions and six-field cron expressions with seconds. For example, `*/30 * * * * *` runs every 30 seconds, and `0 */5 * * * *` runs every five minutes.

## Scheduling Pattern

Use `Util.GetCronExpression(...)` once before the loop, then call `AwaitUntilNextOccurrenceAsync(...)` inside the loop. The extension computes the next local-time occurrence and delays with the provided cancellation token.

Keep the `CancellationToken` flowing through every async call. During shutdown, `Task.Delay`, database calls, HTTP calls, and NATS calls should all be able to stop promptly.

## Multi-Replica Jobs

Every pod starts its own hosted service. If a job must run only once across replicas, protect the critical section with a NATS distributed lock:

```csharp
await using var handle = await natsClient.AcquireDistributedLockAsync(
    "my-service:nightly-cleanup",
    TimeSpan.FromMilliseconds(500),
    ct: stoppingToken);

if (handle is null)
{
    logger.LogDebug("[NightlyCleanup]::another instance owns the lock");
    return;
}

await CleanupAsync(stoppingToken);
```

Use a stable, descriptive lock key. Include the service name and job name so unrelated jobs do not block each other. See [Distributed Locks](./distributed-locks.md) for details.

Jobs that are safe to run on every replica, such as a local heartbeat log, do not need a distributed lock.

## Best Practices

- Keep `ExecuteAsync` as lifecycle and scheduling code. Put the actual work in a private method or injected service.
- Log startup, disabled state, each meaningful execution, and failures with the job name in the message.
- Catch `OperationCanceledException` only when `stoppingToken.IsCancellationRequested`; other cancellation may indicate a real timeout.
- Catch unexpected exceptions inside the loop so one failed run does not permanently stop the background job.
- Add a short backoff after unexpected failures to avoid a tight error loop.
- Make job work idempotent whenever possible. Cron retries, restarts, and lock timeouts are normal in distributed systems.
- Avoid long blocking startup work in `ExecuteAsync`; let the host start and do scheduled work asynchronously unless the app truly must block readiness.

## When Not To Use A Background Job

Do not put inbound NATS message handling in a background job. Use a controller with `[NatsConsumer]` for request/reply or pub/sub handling, and let that controller call services for business logic.

Do not use a general background job for cache refresh if the work fits `BaseCacheService<TValue>` with `RefreshCron`. The cache abstraction already handles hosted-service registration, scheduling, optional startup refresh, and distributed refresh locking.

---

[Back to documentation index](./README.md)
