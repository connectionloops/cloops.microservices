# Microsoft SQL Server

Use SQL Server for relational application data: flexible queries, joins, reporting, and general persistence. The SDK exposes a lean `IDB` helper for raw SQL and runs [DbUp](https://github.com/DbUp/DbUp) migrations at startup.

For ledger-shaped workloads such as balances, credits, debits, and money movement, use [TigerBeetle](./tigerbeetle.md) instead.

## Guides

- [Making SQL Database Calls](./db.md) — query execution, streaming, transactions, and result mapping
- [Database Migrations](./migrations.md) — versioned `.sql` scripts applied at startup with distributed locking
