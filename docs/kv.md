# NATS KV

Part of [Data Persistence](./data-persistence.md).

NATS JetStream Key-Value is the **lowest-latency** document store in `cloops.microservices`. Use it for small values looked up by an exact key — not for relational data, search, or large blobs.

Access the default KV context from the injected NATS client (`CloopsNatsClient` / `ICloopsNatsClient` as `cnc`):

```csharp
using NATS.Client.KeyValueStore;

INatsKVStore bucket = await cnc.KvContext.GetStoreAsync("bucket-name");
```

> Prefer `cnc.KvContext` (the shared default context). On `ICloopsNatsClient`, `CreateKVContext()` returns an equivalent context if you need one explicitly.

## When to use NATS KV

| Choose NATS KV when…                                              | Prefer something else when…                                                |
| ----------------------------------------------------------------- | -------------------------------------------------------------------------- |
| You need the **lowest latency** get-by-id path (typically < 20ms) | Data has relations / multi-table writes → [SQL Server](./mssql.md)         |
| Values are **small** (keep each value **< 4KB**)                  | Values are large / object-backed → [NimbusDb](./nimbusdb.md)               |
| Access is **by exact key only**                                   | You need search / filters on many properties → [Manticore](./manticore.md) |
| You need **optimistic concurrency** (revision CAS)                | Ledger / money movement → [TigerBeetle](./tigerbeetle.md)                  |

**Good fits:** session-ish metadata, feature toggles, small config snapshots, counters/sequences, coordination state, and anything that is “one key → one tiny value” at high QPS.

**Not a fit:** ad-hoc queries, secondary indexes, multi-key transactions, or payloads that grow past a few KB.

## Core API

Resolve a bucket once, then call store methods:

```csharp
INatsKVStore bucket = await cnc.KvContext.GetStoreAsync("profiles");
```

| Method                                 | Behavior                                  |
| -------------------------------------- | ----------------------------------------- |
| `PutAsync(key, value)`                 | Unconditional write (last writer wins)    |
| `CreateAsync(key, value)`              | Create only if the key does **not** exist |
| `UpdateAsync(key, value, revision)`    | Write only if `revision` matches (CAS)    |
| `GetEntryAsync<T>(key)`                | Read value + current `Revision`           |
| `DeleteAsync(key)` / `PurgeAsync(key)` | Soft-delete / purge history               |

`Try*` variants (`TryCreateAsync`, `TryUpdateAsync`, `TryGetEntryAsync`, …) return a result instead of throwing.

### Tiny examples

**Create / put / get**

```csharp
await bucket.CreateAsync("user.42.color", "blue");
await bucket.PutAsync("user.42.color", "green"); // overwrite

var entry = await bucket.GetEntryAsync<string>("user.42.color");
// entry.Value == "green", entry.Revision is the CAS token
```

**Optimistic update (revision must match)**

```csharp
var entry = await bucket.GetEntryAsync<string>("user.42.color");
await bucket.UpdateAsync("user.42.color", "red", entry.Revision);
// throws NatsKVWrongLastRevisionException if another writer raced you
```

## Optimistic concurrency control

Every KV entry carries a monotonically increasing **revision**. `UpdateAsync` is compare-and-swap: the write succeeds only when the revision you pass still matches the server.

Use this whenever multiple instances may mutate the same key and “last writer wins” is unacceptable.

### Distributed locks

Do **not** hand-roll locks with raw KV. The SDK already ships a first-class API on top of NATS KV:

→ **[Distributed Locks](./distributed-locks.md)** (`AcquireDistributedLockAsync`)

That guide covers lease renewal, expired-lock stealing, and disposal.

### Sequence generator

A classic OCC pattern: store a counter, read its revision, then `UpdateAsync` the next value.

```csharp
const string key = "invoice.seq";
INatsKVStore bucket = await cnc.KvContext.GetStoreAsync("sequences");

// seed once
await bucket.TryCreateAsync(key, 0L);

while (true)
{
    var entry = await bucket.GetEntryAsync<long>(key);
    var next = entry.Value + 1;

    var result = await bucket.TryUpdateAsync(key, next, entry.Revision);
    if (result.Success)
        return next; // won the CAS — this is your sequence number

    // lost the race — retry with the latest revision
}
```

This gives a simple distributed sequence without an external coordinator. For globally unique, sortable IDs that do not need a central counter, prefer [Snowflake ID generation](./snowflake-id.md).

## Operational notes

- **Buckets must exist** before `GetStoreAsync`. Create them with `CreateStoreAsync` / `CreateOrUpdateStoreAsync` on `KvContext` (or via your ops bootstrap).
- **Key-only reads.** There is no query language — design keys so you can address values directly (`tenant.123.profile`, `seq.invoices`, …).
- **Keep values small (< 4KB).** Larger documents belong in [NimbusDb](./nimbusdb.md); searchable masters belong in [Manticore](./manticore.md).
- Reuse the injected client / `KvContext`; do not open a new NATS connection per call.

## Related

- [Data Persistence](./data-persistence.md) — choosing among SQL, KV, Manticore, NimbusDb, TigerBeetle, MinIO
- [Distributed Locks](./distributed-locks.md) — KV-backed locks with leases
- [Snowflake ID generation](./snowflake-id.md) — IDs without a shared counter
- [Upstream NATS.Net KV](https://nats-io.github.io/nats.net/documentation/jetstream/kvstore.html)
