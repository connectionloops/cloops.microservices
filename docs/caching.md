# Multi-Level Caching

`cloops.microservices` provides a cache service abstraction for strongly typed read-through caches. Cache services use .NET `HybridCache`, so every cache gets an in-process L1 cache and can use Redis as an L2 distributed cache when Redis is configured.

The application defines what to cache and how to hydrate it. The framework handles service registration, lookup order, cache population, optional startup refresh, scheduled refresh, distributed refresh locking, and readiness reporting.

In practice, this gives service teams production-grade caching without repetitive plumbing:

- Strongly typed cache services with automatic DI registration and optional interface aliases.
- Fast L1 reads by default, Redis-backed L2 sharing when configured, and safe per-key read-through hydration.
- Built-in TTL configuration, null-result caching, startup warmup, cron refresh, and distributed refresh locking.
- Developer-friendly defaults that keep cache code focused on source-of-truth hydration instead of wiring, coordination, and lifecycle concerns.

## Concepts

| Term        | Meaning                                                                                                  |
| ----------- | -------------------------------------------------------------------------------------------------------- |
| `L1 cache`  | In-process memory cache owned by the current service instance. Fastest lookup, but local to one process. |
| `L2 cache`  | Distributed cache shared across service instances. Redis is the intended backing store.                  |
| Hydration   | Loading cache values from the persistent source of truth, such as SQL, another service, or API.          |
| Cache key   | A stable key that identifies one cache entry within a cache service.                                     |
| Cache value | The strongly typed object stored by a cache service. Each cache service owns one value type.             |

The SDK registers `HybridCache` for all apps. Redis integration uses `Microsoft.Extensions.Caching.StackExchangeRedis`; when Redis is not configured, `HybridCache` still provides L1 caching and stampede protection.

## Configuration

Redis is configured from environment variables:

| Variable                  | Required | Meaning                                                                                                                              |
| ------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `REDIS_CONNECTION_STRING` | No       | Enables Redis as HybridCache's distributed L2 cache. When not set, the cache runs in L1-only mode.                                   |
| `REDIS_INSTANCE_NAME`     | No       | Redis key prefix. Defaults to `{AssemblyName}:` when not set, so multiple apps can share a Redis instance without colliding on keys. |

If `REDIS_CONNECTION_STRING` is empty, cache services run in L1-only mode. If Redis is configured, the SDK registers `IDistributedCache` with StackExchange.Redis and `HybridCache` uses it as the secondary cache.

Per-cache L2 opt-out is also available — see `EnableL2` in the [`[CacheConfig]` Attribute](#cacheconfig-attribute) section. The distributed L2 cache is only effectively used when **both** `EnableL2` is `true` (the default) AND `REDIS_CONNECTION_STRING` is set.

## Registering Cache Services

Create cache service classes in a namespace ending with `.Cache`, inherit from `BaseCacheService<TValue>`, and decorate with `[CacheConfig(...)]`. The SDK scans the application assembly for these types and registers each one as:

1. The concrete cache service type (singleton).
2. A same-namespace interface, if present (alias to the singleton).
3. An `IHostedService` (so cron-based and optional startup refresh run as part of the host lifecycle).

```csharp
using CLOOPS.microservices;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace my.app.Cache;

public interface IPatientCache
{
    Task<Patient?> GetAsync(string patientId, CancellationToken ct = default);
}

[CacheConfig(
    name: "patient-cache",
    l1Ttl: "00:05:00",
    l2Ttl: "01:00:00",
    RefreshCron = "0 */15 * * * *")]
public class PatientCacheService : BaseCacheService<Patient>, IPatientCache
{
    // source of truth. may be some db for patients
    private readonly PatientRepository patients;

    public PatientCacheService(IServiceProvider serviceProvider, PatientRepository patients)
        : base(serviceProvider)
    {
        this.patients = patients;
    }

    protected override Task<Patient?> HydrateSingleAsync(string key, CancellationToken ct)
    {
        // Return null when the patient does not exist in the source of truth.
        // Throw only for system errors (network, DB) — thrown exceptions are NOT cached.
        return patients.GetPatientAsync(key, ct);
    }

    protected override async Task<IReadOnlyDictionary<string, Patient>> HydrateAllAsync(CancellationToken ct)
    {
        var allPatients = await patients.GetAllPatientsAsync(ct);
        return allPatients.ToDictionary(patient => patient.Id);
    }
}
```

Application code consumes cache services through dependency injection:

```csharp
public class PatientConsumer
{
    private readonly IPatientCache patients;

    public PatientConsumer(IPatientCache patients)
    {
        this.patients = patients;
    }

    public async Task HandleAsync(string patientId, CancellationToken ct)
    {
        var patient = await patients.GetAsync(patientId, ct);
        if (patient is null)
        {
            // Caller decides what "not found" means.
            return;
        }
        // Use the hydrated patient in business logic.
    }
}
```

### Testing Cache Consumers

For unit tests, depend on the cache-specific interface (for example, `IPatientCache`) and mock or fake that interface. Avoid constructing `BaseCacheService<TValue>` in consumer tests; that pulls in `HybridCache`, TTL configuration, optional Redis/NATS behavior, and hosted-service lifecycle concerns that belong in integration tests.

## `[CacheConfig]` Attribute

`CacheConfigAttribute` is **required** on every cache service. It carries all cache-level configuration:

| Property           | Required | Meaning                                                                                                                                                                                    |
| ------------------ | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `name` (ctor arg)  | Yes      | Stable cache name used as the key prefix and HybridCache tag. Cannot contain `:`. Must be unique across the application.                                                                   |
| `l1Ttl` (ctor arg) | Yes      | Local in-process TTL, as a `TimeSpan` string (e.g. `"00:05:00"` for 5 minutes).                                                                                                            |
| `l2Ttl` (ctor arg) | Yes\*    | Distributed TTL, as a `TimeSpan` string (e.g. `"01:00:00"` for 1 hour). Omit when using the L1-only constructor `[CacheConfig(name, l1Ttl)]`, which automatically sets `EnableL2 = false`. |
| `EnableL2`         | No       | Defaults to `true`. When `false`, the cache service never writes to or reads from the distributed L2 cache, even if Redis is configured. The L1-only constructor sets this automatically.  |
| `RefreshCron`      | No       | Cron expression that periodically triggers `HydrateAllAsync`. Requires the cache service to override `HydrateAllAsync`.                                                                    |
| `RefreshOnStartup` | No       | Defaults to `false`. When `true`, one best-effort bulk refresh runs during host startup. See _Startup behavior_ below.                                                                     |

Cache name uniqueness is validated at startup; two cache services sharing a name throws `InvalidOperationException` before the host runs.

### Distributed refresh lock

Bulk refresh (`HydrateAllAsync` invoked by `RefreshCron`, `RefreshOnStartup`, or an explicit `RefreshAllAsync`) is coordinated with a NATS-backed distributed lock so only one pod hydrates per refresh cycle. This coordination is **derived from `EnableL2`**:

- `EnableL2 = true` (default): refresh updates shared distributed state in Redis, so only one pod should hydrate per cycle. The distributed lock is used. If NATS is not configured, the SDK logs a warning and proceeds without a lock.
- `EnableL2 = false`: refresh updates per-pod L1 state only, so every pod must hydrate independently on every cron tick. The distributed lock is not used.

### L1-only caches

When a cache should never use the distributed L2 — for example a per-pod readiness probe, or a workload-local cache that you specifically don't want shared via Redis — use the L1-only constructor and the framework will skip Redis even if `REDIS_CONNECTION_STRING` is set, and every pod will refresh independently on every cron tick:

```csharp
[CacheConfig(
    name: "tigerbeetle-readiness",
    l1Ttl: "00:05:00",
    RefreshCron = "0 */4 * * * *")]
internal sealed class TigerBeetleReadinessCacheService : BaseCacheService<bool> { ... }
```

The two-argument constructor sets `EnableL2 = false` automatically. You can also use the three-argument constructor and set `EnableL2 = false` explicitly if you want to keep `l2Ttl` for documentation.

## Cache Service API

Each cache service must:

1. Apply `[CacheConfig(...)]` with `name`, `l1Ttl`, `l2Ttl`.
2. Override `HydrateSingleAsync(string key, CancellationToken ct)` — single-key source-of-truth lookup.
3. Optionally override `HydrateAllAsync(CancellationToken ct)` — bulk source-of-truth lookup. Required for `RefreshCron` / `RefreshOnStartup` / explicit `RefreshAllAsync` calls.

`BaseCacheService<TValue>` exposes:

| Method                                      | Behavior                                                                                                                                                                                                                        |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GetAsync(key, ct)`                         | Reads L1 → L2 → `HydrateSingleAsync`. Returns `null` when the source of truth has no value (and caches that absence per `L2Ttl`).                                                                                               |
| `GetAsync(key, CacheGetOptions, ct)`        | Same as above, but accepts per-call TTL overrides (`L1Ttl`, `L2Ttl`) when the key is populated on a cache miss. See [Per-call TTL overrides](#per-call-ttl-overrides) for when to use this instead of changing `[CacheConfig]`. |
| `GetRequiredAsync(key, ct)`                 | Returns the value or throws `KeyNotFoundException` if the source reports it as absent. Use only when absence is an error.                                                                                                       |
| `SetAsync(key, value, ct)`                  | Writes a value to L1 and L2. Useful when you want to overwrite / update a value forcefully.                                                                                                                                     |
| `RemoveAsync(key, ct)`                      | Removes one value from L1 and L2.                                                                                                                                                                                               |
| `RefreshAllAsync(retries, throwOnFail, ct)` | Bulk-hydrates from the source of truth under a NATS distributed lock. See _Startup and refresh flow_ below.                                                                                                                     |

### Nullability and "not found"

`GetAsync` returns `Task<TValue?>`. The contract is intentionally explicit:

- `HydrateSingleAsync` returns `null` when the entry does not exist in the source of truth. `HybridCache` caches the absence like any other value, so subsequent reads stay fast and don't hammer the source.
- `HydrateSingleAsync` throws only for _system errors_ — network failures, DB connectivity, malformed responses, etc. Thrown exceptions are NOT cached; every subsequent lookup of that key will retry.
- Callers that want "fail loud if missing" semantics can use `GetRequiredAsync`, which throws `KeyNotFoundException` on a `null` result.

Avoid throwing for "not found" inside `HydrateSingleAsync` — exception construction and unwinding is dramatically more expensive than returning `null`, and `HybridCache` won't cache the throw, which defeats the cache's purpose.

Prefer reference types or nullable value types for cache values that need an explicit "not found" state. `GetRequiredAsync` only treats `null` as missing; `default(int)`, `default(Guid)`, and other non-null value defaults are returned as ordinary cached values.

### Per-call TTL overrides

Use `GetAsync(key, CacheGetOptions, ct)` when **one cache service** holds entries with **different freshness requirements**, and you want the caller (or hydration path) to pick TTL per key without splitting into multiple cache types or changing the global `[CacheConfig]` defaults.

Overrides apply only when HybridCache **stores** the entry — on an L1/L2 miss that runs `HydrateSingleAsync`. If the key is already cached (including under the service's default TTLs from an earlier `GetAsync(key, ct)`), a later call with custom options returns the existing value until that entry expires naturally. Custom TTLs do **not** retroactively shorten or extend an entry that is already warm.

You can override `L1Ttl`, `L2Ttl`, or both. Any property left `null` falls back to the cache service's configured default for that layer.

**Good fits**

| Scenario                            | Why per-call TTL helps                                                                                                                                                            |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Mixed volatility in one dataset** | Most patients are stable (keep default 5m/1h), but in-flight edits need a shorter window — e.g. `pending` orders cached for 15s L1 / 1m L2 while `shipped` orders use defaults.   |
| **Caller-specific freshness**       | A live status widget calls with short TTLs; a batch export on the same cache interface calls with defaults because minute-level staleness is acceptable.                          |
| **Expensive hydration, rare reads** | A key is read infrequently and hydration is costly — optionally pass _longer_ TTLs for that one lookup so the next read within hours stays in L2 without hitting SQL.             |
| **Probing / existence checks**      | A "does this id exist yet?" poll can use a very short L1 TTL so repeated polls on one pod don't hammer the source, without tightening TTLs for every other consumer of the cache. |

**Prefer something else when**

- **Every key** in the cache should be fresher → lower `l1Ttl` / `l2Ttl` on `[CacheConfig]`, or tighten `RefreshCron`, instead of passing options on every call.
- **You know the source changed** (update, delete, webhook) → call `RemoveAsync(key, ct)` (or `SetAsync`) so all pods see the change promptly. Shorter TTL is a _probabilistic_ freshness bound, not invalidation.
- **The same key is read with conflicting TTLs** from different code paths → whichever population wins first sets TTL until expiry; the other path does not re-apply its options on a hit. Split caches, use explicit invalidation, or route volatile keys through a dedicated code path that always uses the stricter TTL.

Example: shorten TTL only when the caller knows the entity is in a volatile state (often after a lightweight status read or message metadata):

```csharp
var order = await orders.GetAsync(orderId, ct);
if (order?.Status == OrderStatus.Pending)
{
    // Re-fetch with tighter bounds on miss; if still within a prior default-TTL entry, RemoveAsync first.
    order = await orders.GetAsync(
        orderId,
        new CacheGetOptions { L1Ttl = TimeSpan.FromSeconds(15), L2Ttl = TimeSpan.FromMinutes(1) },
        ct);
}
```

When `CacheGetOptions` is `null` (or both TTLs are `null`), the cache service's default TTLs are used and no per-call options object is allocated.

## Lookup Flow

Cache reads are read-through. Callers ask the cache service for a key and do not need to know whether the value came from L1, L2, or the persistent store.

```mermaid
sequenceDiagram
    autonumber
    participant Caller as Consumer or Service
    participant Cache as Cache Service
    participant Hybrid as HybridCache
    participant L1 as L1 Memory Cache
    participant L2 as L2 Redis Cache
    participant Store as Persistent Store

    Caller->>Cache: GetAsync(key)
    Cache->>Hybrid: GetOrCreateAsync(cacheName:key)
    Hybrid->>L1: TryGet(key)
    alt L1 hit
        L1-->>Hybrid: value
        Hybrid-->>Cache: value
    else L1 miss
        Hybrid->>L2: TryGet(key)
        alt L2 hit
            L2-->>Hybrid: value (deserialize)
            Hybrid->>L1: Set(key, value, L1 TTL)
            Hybrid-->>Cache: value
        else L2 miss
            Hybrid->>Store: HydrateSingleAsync(key)
            Store-->>Hybrid: value or null
            Hybrid->>L2: Set(key, value, L2 TTL)
            Hybrid->>L1: Set(key, value, L1 TTL)
            Hybrid-->>Cache: value
        end
    end
    Cache-->>Caller: value
```

`HybridCache` ensures only one concurrent caller for a **given key** calls the hydration callback; other concurrent callers wait for the same result. For bulk updates, we have our own distributed lock gate to protect against multiple pods trying to update L2 cache at the same time.

## Startup and Refresh Flow

Bulk hydration is optional. It is useful for small reference datasets, heavily read data, or systems where cold-start latency matters.

By default, startup does not refresh L2 from the source of truth. This avoids a rolling restart of many pods causing repeated database hydration when Redis already has healthy L2 data.

Startup also does not eagerly warm L1 from L2. After a pod restart, L1 is repopulated on demand: the first `GetAsync` for a key checks Redis L2 and promotes that value into local L1 when present. If L2 is missing or expired, the normal single-key hydration path loads from the source of truth.

### `RefreshOnStartup = true`

`RefreshOnStartup` is available for rare caches where a best-effort startup fill is useful — first deploys, Redis flush recovery, or small reference datasets that should be ready before traffic. When enabled:

- The cache service must override `HydrateAllAsync` (validated at startup).
- The cache service **blocks** its `StartAsync` until hydration completes (or returns early because another instance holds the refresh lock).
- The SDK waits up to 60 seconds for a configured NATS client to connect before attempting the distributed lock. If no NATS client is registered, refresh runs without a distributed lock and logs a warning.

> **Warning — multi-pod deployments:** Every pod runs startup refresh during `StartAsync`. With L2 enabled and NATS configured, only one pod hydrates at a time (others skip when the distributed lock is held), but during a **rolling restart** pods that start sequentially can each acquire the lock in turn — potentially one `HydrateAllAsync` per pod. Without NATS, or with L1-only caches (`EnableL2 = false`), **every pod hydrates independently** on every startup. Be cautious enabling this for apps that run multiple replicas; prefer the default (`RefreshOnStartup = false`) and rely on L2 + on-demand L1 promotion unless you have a specific cold-start need.

ASP.NET Core arranges `IHostedService` startup so that **all user-registered hosted services run before `GenericWebHostService` (Kestrel) binds to its port** (see [PR dotnet/aspnetcore#36122](https://github.com/dotnet/aspnetcore/pull/36122)). The practical consequence: while a cache is hydrating during `StartAsync`, Kestrel is not yet listening, so external probes get TCP "connection refused" and Kubernetes will not route traffic to the pod. The SDK does not perform an explicit cache-readiness check in `/readyz`; cache startup readiness happens out of the box through hosted-service startup ordering.

Use explicit `RefreshAllAsync(retries: ..., throwOnFail: true, ...)` calls when an operation needs stronger guarantees and should fail loudly if the refresh cannot complete.

Scheduled refresh requires both `HydrateAllAsync` and `RefreshCron`. If `RefreshCron` is configured without bulk hydration, startup fails with a configuration error.

```mermaid
sequenceDiagram
    autonumber
    participant Startup as Optional Startup Refresh
    participant Timer as Refresh Schedule
    participant Cache as Cache Service
    participant Lock as Distributed Lock
    participant Store as Persistent Store
    participant Hybrid as HybridCache

    Startup->>Cache: RefreshAllAsync(retries: 0)
    Cache->>Lock: Wait for NATS, then try-acquire cache-refresh.{CacheName}
    alt Startup lock acquired
        Cache->>Store: HydrateAllAsync()
        Store-->>Cache: all values
        Cache->>Hybrid: SetAsync per entry (overwrites in-place)
    else Another instance is refreshing
        Cache-->>Startup: Skip startup refresh
    end

    Timer->>Cache: RefreshAllAsync()
    Cache->>Lock: Acquire cache-refresh.{CacheName}
    alt Lock acquired
        Cache->>Store: HydrateAllAsync()
        Store-->>Cache: all values
        Cache->>Hybrid: SetAsync per entry (overwrites in-place)
        Cache->>Lock: Release lock
    else Another instance is refreshing
        Cache-->>Timer: Skip refresh
    end
```

Distributed locks use the existing NATS JetStream lock support. If NATS is not configured, the cache service logs a warning and performs refresh without a distributed lock.

Two other lock failures are handled differently on purpose. Neither one fails host startup:

| Failure | Behaviour | Why |
| --- | --- | --- |
| **Invalid lock key** — the cache name contains a character NATS KV rejects (e.g. a space) | Log an error, refresh **without** a lock | Deterministic: the key fails identically on every pod, forever, so blocking refresh permanently achieves nothing. Fix the cache name. |
| **Lock service failure** — NATS outage, missing KV bucket, timeout | Log an error, **skip** this refresh until the next scheduled run | Correlated across the fleet: every pod fails at the same moment. Refreshing unlocked here would put every pod through a full `HydrateAllAsync` on every cron tick, with matching L2 write amplification, precisely during an outage. |

Callers that pass `throwOnFail: true` still get the exception in both cases.

> ⚠️ The refresh lock key is `cache-refresh.{CacheName}` — dot-separated, because it is a NATS KV key and **NATS KV keys may not contain `:`**. This is deliberately different from the Redis cache key below. See [Lock key rules](./distributed-locks.md#lock-key-rules).

## Key Structure

Cache keys include the cache service name and entry key:

```text
{cache-service-name}:{key-name}
```

Examples:

```text
patient-cache:12345
bills-cache:invoice-987
```

These are **`HybridCache` / Redis keys**, where `:` is the correct separator. Do not confuse them with the NATS **distributed-lock** key (`cache-refresh.{CacheName}`), which is dot-separated because [NATS KV keys may not contain `:`](./distributed-locks.md#lock-key-rules).

Cache names and entry keys cannot contain `:`. Because the cache name is also embedded in the refresh lock key, prefer names limited to letters, digits and `-`, `_`, `=`, `/`, `.`; other characters (spaces, `@`, …) are rejected by NATS when the refresh lock is acquired, and the SDK will then log an error and refresh without a lock. Each entry also receives a `HybridCache` tag equal to `name`. The framework reserves the tag for future use; the bulk refresh path intentionally does _not_ call `RemoveByTagAsync` — see _Design Notes_ for why.

## Design Notes

### TTL sizing

For scheduled bulk caches, choose TTLs so `L1Ttl < refresh cron interval < L2Ttl`. Short L1 lets local entries turn over between refreshes (which matters because of cross-pod L1 staleness — see below); long L2 keeps shared data available for pod restarts and missed refresh cycles.

### Cross-pod L1 staleness

`HybridCache` only invalidates the L1 cache on the pod that performed the operation. Per the [official documentation](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid):

> When invalidating cache entries by key or by tags, they're invalidated in the current server and in the secondary out-of-process storage. However, the in-memory cache in other servers isn't affected.

The practical consequences for cloops caches:

- When one pod runs `RefreshAllAsync`, it writes fresh values to L2 and refreshes its own L1. Other pods continue serving stale L1 data until their `L1Ttl` expires for each key.
- This is a fundamental property of HybridCache without a backplane. Pick a small enough `L1Ttl` that per-pod L1 staleness is acceptable for your workload.

### Why bulk refresh does not `RemoveByTagAsync`

An earlier design called `RemoveByTagAsync` before writing the new values. This created a race window: between the tag invalidation and each subsequent `SetAsync`, reads for not-yet-rewritten keys fell through to `HydrateSingleAsync`. Under heavy read traffic this caused stampedes against the source of truth.

The current implementation simply iterates and calls `SetAsync` for each refreshed key. Reads remain consistent throughout the refresh (they see either the old value or the new value, never a forced miss). The trade-off:

- **Entries that exist in both the old and new dataset:** overwritten in place. No race, no stampede.
- **Entries that exist only in the _new_ dataset:** added by `SetAsync`. No race.
- **Entries that exist only in the _old_ dataset** (i.e. removed from the source of truth): remain in L2 until their natural `L2Ttl` expiration. Pods that have them in L1 will continue serving them until `L1Ttl` expires too.

For most read-through caches this is acceptable. If your domain absolutely cannot tolerate serving removed entries, use `RemoveAsync(key, ct)` explicitly when the upstream change event fires, or shorten `L2Ttl`. There is also a known [HybridCache local tag-invalidation caching limitation](https://github.com/dotnet/extensions/issues/7411) that would have caused the same staleness even with `RemoveByTagAsync`, so the simpler write-only path is the better default.

### L2 hit deserialization cost

An L2 hit is **not** free. Each L2 hit deserializes the value (JSON by default) and copies bytes off the Redis socket. For large or deeply nested objects this dominates the cost, and is one of the practical reasons to keep `L1Ttl` non-trivial for hot keys — repeat reads of a hot key under L1 cost almost nothing.

### Type ownership

A cache service should own exactly one value type to keep serialization, TTL, and hydration behavior predictable.

### Hydration semantics

Hydration callbacks should treat the persistent store as the source of truth. `HydrateSingleAsync` should return `null` for "not present" and throw only for system errors. Throw-on-not-found defeats the cache's purpose because exceptions are not cached.

### Refresh philosophy

Cron refresh is the normal path that hydrates L2 from the source of truth. Startup should normally avoid bulk hydration; L1 fills from L2 on demand as keys are read. Reach for `RefreshOnStartup` only when cold-cache latency is genuinely unacceptable for the first traffic after a deploy.
