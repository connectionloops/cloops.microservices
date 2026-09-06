# Change Log

Tracking what has changed since v1.1.15

| Date         | Feature                                                              | Introduced in |
| ------------ | -------------------------------------------------------------------- | ------------- |
| May 7, 2026  | dotnet 10                                                            | 1.1.15        |
| May 7, 2026  | [tigerbeetle](/docs/tigerbeetle.md)                                  | 1.1.16        |
| May 8, 2026  | [caching](/docs/caching.md)                                          | 1.1.17        |
| May 9, 2026  | [REST endpoints](/docs/rest.md)                                      | 1.1.17        |
| May 11, 2026 | [database migrations](/docs/migrations.md)                           | 1.1.17        |
| July 2, 2026 | [Agent Skill](/skills/cloops-microservices/README.md)                | 1.1.18        |
| July 8, 2026 | [NATS Consumer Interceptor](/docs/consumer.md#consumer-interceptors) | 1.1.19        |
| July 11, 2026 | [Snowflake ID generation (IdGen)](/docs/snowflake-id.md)             | 1.1.20        |
| July 19, 2026 | [NATS Consumer Exception Handler](/docs/consumer.md#consumer-exception-handlers) | 1.1.21 (requires `cloops.nats` with exception-handler support) |
| July 26, 2026 | [`RunInTransaction<T>` automatic SQL transaction management](/docs/db.md#automatic-transaction-management) | 1.1.22 |
| July 27, 2026 | Fix: migration distributed-lock key uses a valid NATS KV separator; migrations skip gracefully when the lock cannot be acquired | 1.1.25 |
| September 6, 2026 | Fix: cache refresh distributed-lock key uses a valid NATS KV separator (`cache-refresh.{CacheName}`), an invalid lock key or a failing lock service no longer crashes host startup, and new [`NatsKvKey`](/docs/distributed-locks.md#lock-key-rules) validator rejects invalid KV / lock keys with an actionable message | 1.1.26 |
