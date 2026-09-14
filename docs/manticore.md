# Manticore

Part of [Data Persistence](./data-persistence.md).

[Manticore Search](https://manticoresearch.com/) is the **searchable-documents** store in `cloops.microservices`. Use it for resource or entity masters that need search or filtering across many properties at low latency (< 50ms) — the workloads that outgrow [NATS KV](./kv.md)'s exact-key access but don't belong in relational [SQL Server](./mssql.md).

The SDK ships `BaseManticoreService` (transport + failure classification) and `ManticoreSql` (escaping — the injection boundary). Your service owns its schema, its queries, and its mapping.

## When to use Manticore

| Choose Manticore when…                                      | Prefer something else when…                                                          |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| Entity masters **searchable/filterable by many properties** | Access is by exact key only → [NATS KV](./kv.md)                                     |
| **Full-text search** with relevance ranking                 | Data has relations / multi-table writes → [SQL Server](./mssql.md)                   |
| A **rebuildable projection** of a system of record          | It would be the only copy of the data — Manticore is an index, not a source of truth |

**Model it as a projection.** Keep the source of truth elsewhere (usually SQL Server) and treat the Manticore index as rebuildable: your service should be able to re-fill it from the source. That assumption is baked into the SDK's failure classification (below).

## Transport: HTTP, not MySQL — settled

Manticore listens on **9308 (HTTP JSON API)** and 9306 (MySQL wire protocol). The SDK uses **HTTP only**: `POST /sql?mode=raw` with the statement form-encoded as `query=<sql>`. This decision was evaluated and is settled — a MySQL client buys a connection pool, a second SQL dialect in the dependency graph and a class of "connection reset" failures, in exchange for saving a form-encode. Do not add a MySQL-protocol client for Manticore.

`mode=raw` is what makes the endpoint accept arbitrary SQL rather than only SELECTs. A JSON body goes to a *different* endpoint with a different dialect — mixing them up produces a confident 200 with an error inside.

## Deriving a service

```csharp
using CLOOPS.microservices;

public class ProductSearchService : BaseManticoreService
{
    private readonly string _index;

    public ProductSearchService(
        IHttpClientFactory httpClientFactory,
        ILogger<ProductSearchService> logger,
        AppSettings settings)
        : base(httpClientFactory, settings.ManticoreUrl, logger)
    {
        // Config-side injection guard: a malformed configured name logs loudly and
        // falls back, instead of either injecting or crash-looping the pod.
        _index = ManticoreSql.SafeIndexName(settings.ManticoreIndex, "products", logger);
    }

    public async Task<IReadOnlyList<Product>> SearchAsync(string userQuery, CancellationToken ct)
    {
        // Escape order is load-bearing: EscapeMatch first (full-text operators),
        // EscapeLiteral second (SQL string literal). Never the reverse.
        var terms = ManticoreSql.EscapeMatch(userQuery);
        var match = ManticoreSql.EscapeLiteral($"@(name,description) \"{terms}\"/0.3");

        var result = await ExecuteAsync(
            $"SELECT id, name, price FROM {_index} WHERE MATCH('{match}') LIMIT 20", ct);

        return DataRows(result)
            .Select(row => new Product(ReadInt64(row, "id"), ReadString(row, "name") ?? "", ReadInt64(row, "price")))
            .ToList();
    }
}
```

`BaseManticoreService` gives you:

| Member                   | What it does                                                                                        |
| ------------------------ | --------------------------------------------------------------------------------------------------- |
| `ExecuteAsync(sql, ct)`  | Runs one statement against `POST sql?mode=raw`; returns the result object as `JsonElement`           |
| `DataRows(result)`       | The object rows of the result's `data` array (missing/non-array yields nothing — normal for writes)  |
| `ReadString / ReadInt64` | Lenient cell readers (Manticore sometimes stringifies numbers; unwritten columns come back null)     |
| `Timeout` (virtual)      | Per-statement timeout, default **15s** — see below before overriding                                 |

`ManticoreSql` gives you `EscapeLiteral`, `EscapeMatch`, and `SafeIndexName` — see [Escaping](#escaping-the-injection-boundary).

## The trap: HTTP 200 is not success

Manticore answers a **rejected statement with HTTP 200** and a non-empty `error` member in the JSON body. Code that only checks the status code reports every syntax error, missing index and type mismatch as a successful write of zero rows — silently, until someone notices search returns nothing.

`ExecuteAsync` checks the body on every statement, which is why **all statements must go through it**. It also handles both response shapes in the wild: an *array* of result objects (recent builds) and a *bare object* (older builds and some statements).

## Failure classification

| Exception                     | Meaning                                                              | What to do                                                                                             |
| ----------------------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `ManticoreTransientException` | Unreachable, timeout, non-2xx, or a rejected statement (200 + error) | Retry / nak. The index is a rebuildable projection; a retry or reconcile repairs drift.                 |
| `ManticoreContractException`  | Body is not JSON, or not a Manticore result shape                     | Don't retry — the URL points at something that isn't Manticore's SQL endpoint. Surface as config error. |

Translate these into your platform's transient/permanent taxonomy at the service boundary (e.g. nak-with-delay for transient, terminal for contract).

Caller-initiated cancellation propagates as `OperationCanceledException` untranslated; only the *timeout* (which also surfaces as `TaskCanceledException` under the hood) is classified transient.

## Escaping: the injection boundary

**There are no bound parameters on this API.** Every value is interpolated into a SQL string, so `ManticoreSql.EscapeLiteral` and `ManticoreSql.EscapeMatch` are the security boundary — always use the SDK's copy, never hand-roll or copy-paste them into your service.

Two rules, both learned in production:

1. **Manticore escapes quotes with a backslash. The ANSI `''` form is a parse error.** `WHERE x = 'won''t'` fails with `P01: syntax error`; `WHERE x = 'won\'t'` parses. An implementation that doubled quotes took every statement containing an apostrophe down on the read path and silently failed to index documents on the write path. `EscapeLiteral` does this correctly (plus backslash doubling and control-character folding).
2. **Two nested languages, two passes, fixed order.** For user text bound into a `MATCH()` expression: `EscapeMatch` **first** (backslash-escapes the full-text operators `\ ( ) | - ! @ ~ " & / ^ $ = < > *`, collapses whitespace), then `EscapeLiteral` on the whole assembled expression (doubles those backslashes, escapes apostrophes). Reversing the order silently turns every operator back into an operator.

The index name is the one value interpolated without escaping — guard it with `ManticoreSql.SafeIndexName(configured, fallback, logger)`, which falls back loudly on anything that isn't a bare identifier.

## Protocol gotchas

- **One `MATCH()` per statement.** A second MATCH clause parses and then fails at execution. Fold additional full-text conditions into the same expression with field-limit operators (`@field term`).
- **The default `LIMIT` is 20**, not "all rows". Always emit an explicit `LIMIT`; a pagination sweep without one still terminates but reads twenty rows per round trip.
- **`LIMIT` above `max_matches` (default 1 000) is silently truncated** to `max_matches`. A page size raised past it makes a keyset sweep walk a prefix of the index forever while reporting success. Keep page sizes ≤ 1 000, or deliberately emit `OPTION max_matches=…` and accept the server-side memory cost.
- **Quorum thresholds: integer means absolute, fraction means proportion.** `"a b c"/2` demands two terms; `"a b c"/0.5` demands half. The edge case bites: `/1` means "any one term" (loosest) while `/1.0` means "all terms" (strictest) — so when formatting a ratio, force a decimal digit (`0.0##`) and always use `CultureInfo.InvariantCulture` (a comma-culture pod would otherwise emit `/0,3`, a parse error that arrives as HTTP 200 + `error`).
- **Statements travel URL-encoded in a form body.** Keep them bounded: batch `VALUES` tuples and `IN (…)` lists to ~100–200 entries per statement rather than one multi-megabyte line that some proxy truncates at an undocumented size.
- **A trailing slash on the endpoint URL is load-bearing** when the URL carries a path prefix (behind an ingress). `BaseManticoreService` normalises this for you; it's why the URL is a constructor parameter rather than something you set in `ConfigureClient`.

## Timeout: 15 seconds, on purpose

The default per-statement timeout is **15s**, not `HttpClient`'s 100s. Manticore statements typically run inside a request/reply whose caller (a NATS request, an HTTP thread) gives up long before 100s, and a hung statement pins that caller — e.g. a NATS consumer slot — for the whole wait. This timeout is the only guard against that. Failing fast and letting a reconcile repair drift beats holding a thread for a minute and a half. Override `Timeout` only as a deliberate, documented act.

## Related

- [Data Persistence](./data-persistence.md) — choosing among SQL, KV, Manticore, NimbusDb, TigerBeetle, MinIO
- [NATS KV](./kv.md) — exact-key lookups that don't need search
- [Services](./services.md) — where a Manticore-backed service fits in a microservice
