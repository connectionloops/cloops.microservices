# Snowflake ID Generation

`cloops.microservices` provides built-in support for Snowflake-style distributed ID generation via [IdGen](https://github.com/RobThree/IdGen). This is the **preferred way to generate unique IDs** across the platform's distributed services.

## What it is

A Snowflake ID is a 64-bit (effectively 63-bit) integer composed of three parts:

```
| timestamp | generator-id | sequence |
```

- **Timestamp** — milliseconds since a fixed epoch (default `2015-01-01 UTC`). Gives rough time ordering.
- **Generator-id** (a.k.a. node/worker id) — the part *you* configure. It must be **unique per replica** so that two pods never produce the same ID.
- **Sequence** — incremented within the same millisecond and reset on each new tick, so a single generator can mint many IDs per ms without collisions.

The result is a low-latency, uncoordinated, roughly time-ordered, compact, and highly available ID — no database round-trip, no central coordinator, no single point of failure.

## When to use it

Use Snowflake IDs anywhere you need a globally unique identifier without a central authority:

- Primary keys for entities created in distributed services
- Correlation / trace IDs that span services
- Idempotency keys for retries across NATS consumers
- Event / message IDs
- Any place you currently rely on `Guid.NewGuid()` but want sortable, smaller, integer IDs

Prefer Snowflake over database-generated sequential IDs or `Guid` when you need scale, low latency, and rough time ordering without coordinating between services.

## Configuration

Snowflake ID generation is **opt-in**. It is gated by an environment variable because it requires a unique, stable per-node generator id, and not every service wants that operational responsibility.

| Variable                  | Description                                                                                                                                                                              | Default | Required                          |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- | --------------------------------- |
| `ENABLE_SNOWFLAKE_ID`     | When `True`, the SDK registers the IdGen singleton(s) in DI. When unset/`False`, no ID generator is registered.                                                                          | `False` | No                                |
| `SNOWFLAKE_GENERATOR_ID`  | Integer generator (node) id passed to `IdGen`. Must be **unique per replica and stable across restarts**. If `ENABLE_SNOWFLAKE_ID=True` and this is missing or invalid, the app **errors out at startup**. | None    | Yes, when `ENABLE_SNOWFLAKE_ID=True` |

Example:

```bash
export ENABLE_SNOWFLAKE_ID=True
export SNOWFLAKE_GENERATOR_ID=3
```

If `ENABLE_SNOWFLAKE_ID` is set but `SNOWFLAKE_GENERATOR_ID` is missing or not an integer, `App` throws `InvalidOperationException` at startup and the service refuses to boot. This fails fast rather than silently producing colliding IDs.

## Dependency Injection

When enabled and configured, `App` calls the upstream IdGen DI extension:

```csharp
builder.Services.AddIdGen(appSettings.SnowflakeGeneratorId);
```

This registers **both** `IdGen.IdGenerator` and `IIdGenerator<long>` as singletons pointing at the same generator instance. Inject whichever type you prefer.

## Using the generator

Inject the generator and call `CreateId()`:

```csharp
using IdGen;

namespace yourapp.services;

public class OrderService
{
    private readonly IIdGenerator<long> _idGen;

    public OrderService(IIdGenerator<long> idGen)
    {
        _idGen = idGen;
    }

    public long CreateOrder()
    {
        long id = _idGen.CreateId();
        // persist and return the id...
        return id;
    }
}
```

To mint many IDs at once, prefer `Take(n)` over calling `CreateId()` in a loop — `IdGenerator` implements `IEnumerable<long>` as a never-ending stream of IDs:

```csharp
var ids = _idGen.Take(100).ToArray();
```

The injected generator is a singleton; share it across services and concurrent tasks. Do not dispose it — the host owns the singleton.

## Deployment: getting a reliable generator-id

The generator-id is the only piece that must be unique and stable. Two replicas with the same id will produce **colliding IDs**. The most reliable way to guarantee uniqueness in Kubernetes is to run the service as a **StatefulSet** and derive the generator-id from the stable pod ordinal.

### StatefulSet (recommended)

A StatefulSet gives each pod a stable, predictable ordinal index (`0`, `1`, `2`, ...). Expose it to the app via an env var and use it directly as the generator-id:

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: my-service
spec:
  serviceName: my-service
  replicas: 3
  template:
    spec:
      containers:
        - name: my-service
          env:
            - name: ENABLE_SNOWFLAKE_ID
              value: "True"
            - name: SNOWFLAKE_GENERATOR_ID
              valueFrom:
                fieldRef:
                  fieldPath: metadata.annotations['apps.kubernetes.io/pod-index']
```

> The `apps.kubernetes.io/pod-index` annotation is available on Kubernetes 1.31+ (beta) and gives the pod's ordinal. On older clusters, derive the ordinal from the pod name (`<statefulset-name>-<ordinal>`) in an init container or a small startup script and export it as `SNOWFLAKE_GENERATOR_ID`.

Because StatefulSet pod names are stable across restarts, the same ordinal maps to the same replica, so IDs stay collision-free even after reschedules.

### Other schemes

Any scheme that guarantees **unique and stable** per-replica ids works:

- A coordination service that hands out ids on startup (more moving parts — only worth it if you can't use StatefulSet)
- A config map keyed by hostname (stable host ⇒ stable id)
- A small fixed pool where each deployment is explicitly assigned an id

What does **not** work safely: random ids per boot, `hostname` hashing without checking for collisions, or sharing one id across multiple replicas.

### Operational notes

- **Keep system clocks accurate.** Use NTP/chrony. IdGen guards against non-monotonic clocks but accurate time prevents duplicate IDs across restarts.
- **Don't change the `IdStructure` once in production.** The default structure (timestamp/generator/sequence bit split) is fine for most workloads; changing it after you've shipped IDs can cause collisions. Commit to a structure up front.
- **Keep the generator a singleton.** IdGen is designed as one generator per process per node id. Don't spin up extra `IdGenerator` instances with the same node id.
- **JavaScript clients:** IDs are 63-bit and exceed `Number.MAX_SAFE_INTEGER`. If ids flow to a browser/Node client, serialize them as **strings**, not numbers, to avoid silent precision loss.

## Upstream documentation

The cloops SDK wires IdGen into dependency injection; the generator API is the upstream IdGen .NET library:

- [IdGen on GitHub](https://github.com/RobThree/IdGen)
- [IdGen NuGet](https://www.nuget.org/packages/IdGen)
- [IdGen.DependencyInjection NuGet](https://www.nuget.org/packages/IdGen.DependencyInjection)

---

[Back to documentation index](./README.md)
