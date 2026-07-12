# Data Persistence

`cloops.microservices` supports several persistence backends. Choose the store that matches your data shape and access pattern.

| Use case                                                                                           | Choose                              |
| -------------------------------------------------------------------------------------------------- | ----------------------------------- |
| **Business Process**                                                                               | \*\*\*                              |
| Data has business relations meaningful to app functionality; multiple tables are modified together | [Microsoft SQL Server](./mssql.md)  |
| Money, ledger, and accounting — balances, transfers, double-entry                                  | [TigerBeetle](./tigerbeetle.md)     |
| **Documents**                                                                                      | \*\*\*                              |
| Fast document retrieval by exact key; value < 4KB (lowest latency < 20ms); OCC / sequences         | [NATS KV](./kv.md)                  |
| Resource or entity masters searchable by many properties (low latency. < 50ms)                     | [Manticore](./manticore.md) _(WIP)_ |
| Massive-sized objects retrieved by ID (decent latency may be around 100ms)                         | [NimbusDb](./nimbusdb.md) _(WIP)_   |
| **Blob**                                                                                           | \*\*\*                              |
| MinIO                                                                                              | [MinIO](./min-io.md) _(WIP)_        |
