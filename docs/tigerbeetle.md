# TigerBeetle

Part of [Data Persistence](./data-persistence.md).

TigerBeetle is available in `cloops.microservices` for services that need a high-performance financial accounting database. The SDK registers the upstream .NET `TigerBeetle.Client` in dependency injection when TigerBeetle configuration is provided.

Use TigerBeetle for ledger-shaped workloads: account balances, money movement, credits and debits, double-entry accounting, transfers, pending transfers, idempotent transaction submission, and audit-friendly financial event histories. For general relational queries, reporting, joins, and application data that does not need ledger semantics, use the SQL `IDB` helper described in [Making Database Calls](./db.md).

It is highly recommended to read [TigerBeetle docs](https://docs.tigerbeetle.com/)

## Table of Contents

- [Configuration](#configuration)
- [Dependency Injection](#dependency-injection)
- [Using the Client](#using-the-client)
- [Accounts](#accounts)
- [Transfers](#transfers)
- [Batching](#batching)
- [Operational Notes](#operational-notes)
- [Upstream Documentation](#upstream-documentation)

## Configuration

Configure TigerBeetle with environment variables:

| Variable        | Description                                                                                | Default |
| --------------- | ------------------------------------------------------------------------------------------ | ------- |
| `TB_ADDRESSES`  | Comma-separated TigerBeetle replica addresses, for example `127.0.0.1:3000,127.0.0.1:3001` | None    |
| `TB_CLUSTER_ID` | TigerBeetle cluster ID                                                                     | `0`     |

Example:

```bash
export TB_ADDRESSES="127.0.0.1:3000"
export TB_CLUSTER_ID=0
```

If `TB_ADDRESSES` is empty, the SDK does not register a TigerBeetle client.

## Dependency Injection

When `TB_ADDRESSES` contains at least one valid address, `App` registers `TigerBeetle.Client` as a singleton:

```csharp
builder.Services.AddSingleton<TigerBeetle.Client>(_ =>
    new TigerBeetle.Client(appSettings.TigerBeetleClusterId, addresses));
```

Inject the client directly into your services:

```csharp
using TigerBeetle;

namespace yourapp.services;

public class LedgerService
{
    private readonly Client _client;

    public LedgerService(Client client)
    {
        _client = client;
    }
}
```

Do not dispose the injected client in your service. The host owns the singleton and will dispose it when the application shuts down.

## Using the Client

The TigerBeetle client is thread-safe. Prefer sharing the injected singleton across services and concurrent tasks instead of creating a new client per operation. This lets the client batch concurrent work efficiently.

TigerBeetle APIs are batch-oriented. Most operations accept arrays and return per-item results. Always inspect result statuses and decide whether each result is successful, retryable, already applied, or invalid for your domain.

## Accounts

Create accounts with `CreateAccounts`. Account IDs, ledgers, codes, and flags are part of your domain model, so keep them stable and documented.

```csharp
using TigerBeetle;

var accounts = new[]
{
    new Account
    {
        Id = ID.Create(),
        Ledger = 1,
        Code = 100,
        Flags = AccountFlags.None,
    }
};

var results = _client.CreateAccounts(accounts);

foreach (var result in results)
{
    if (result.Status != CreateAccountStatus.Created &&
        result.Status != CreateAccountStatus.Exists)
    {
        throw new InvalidOperationException($"Account creation failed: {result.Status}");
    }
}
```

Useful account flags include:

- `AccountFlags.DebitsMustNotExceedCredits` for accounts that cannot overdraft on the debit side.
- `AccountFlags.CreditsMustNotExceedDebits` for accounts that cannot overdraft on the credit side.
- `AccountFlags.History` when you need historical balance queries for the account.

## Transfers

Create transfers with `CreateTransfers`. A transfer is the journal entry that moves value between two accounts.

```csharp
using TigerBeetle;

var transfers = new[]
{
    new Transfer
    {
        Id = ID.Create(),
        DebitAccountId = debitAccountId,
        CreditAccountId = creditAccountId,
        Amount = 10,
        Ledger = 1,
        Code = 200,
        Flags = TransferFlags.None,
    }
};

var results = _client.CreateTransfers(transfers);

foreach (var result in results)
{
    if (result.Status != CreateTransferStatus.Created &&
        result.Status != CreateTransferStatus.Exists)
    {
        throw new InvalidOperationException($"Transfer failed: {result.Status}");
    }
}
```

Use stable transfer IDs for idempotency. If your service retries after a timeout or process restart, submitting the same transfer ID lets TigerBeetle identify an already-applied transfer instead of creating a duplicate movement.

TigerBeetle also supports pending transfers for two-phase flows. Use `TransferFlags.Pending` to reserve funds, then submit another transfer with `TransferFlags.PostPendingTransfer` or `TransferFlags.VoidPendingTransfer`.

## Batching

TigerBeetle performance depends heavily on batching. Prefer sending many accounts or transfers in a single call when your workflow allows it. For queue or NATS consumer workloads, consider collecting multiple messages before writing to TigerBeetle instead of writing one transfer at a time.

The upstream client can automatically batch concurrent requests from the shared client, but explicit application-level batching is still the best path for high throughput.

## Operational Notes

- TigerBeetle is not a general SQL database. Model data as accounts and transfers.
- Keep `Ledger` and `Code` values consistent across services and environments.
- Treat result handling as part of the domain logic. A partial batch failure should be handled deliberately.
- The client retries indefinitely and does not impose per-request timeouts. Use cancellation and idempotent IDs for reliable submission flows.
- Use one singleton client per TigerBeetle cluster. Multiple clients are only needed when a service talks to multiple clusters.

## Upstream Documentation

The cloops SDK wires the client into dependency injection, but the client API is the upstream TigerBeetle .NET client. Refer to the official docs for the full API surface, field semantics, flags, query APIs, and samples:

- [TigerBeetle .NET client documentation](https://github.com/tigerbeetle/tigerbeetle/tree/main/src/clients/dotnet)
- [TigerBeetle project](https://github.com/tigerbeetle/tigerbeetle)

---

[Back to documentation index](./README.md)
