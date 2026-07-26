---
name: cloops-microservices
description: Use the cloops.microservices SDK documentation when building, configuring, testing, or explaining C# / .NET microservices that use NATS consumers, consumer interceptors, consumer exception handlers, persistence backends (SQL Server, TigerBeetle, Manticore, NimbusDb, NATS KV, MinIO), REST endpoints, caching, background processing, distributed locks, observability, Snowflake ID generation, or the cloops.microservices conventions.
---

# cloops.microservices

Opinionated, production-ready SDK for building lean, highly-available cloud-native microservices in C# using [NATS](https://nats.io/) as the primary communication layer.

## Instructions

Use this skill when the user works with the `cloops.microservices` SDK or asks about its setup, patterns, APIs, persistence options, consumers, consumer interceptors, consumer exception handlers, controllers, services, caching, migrations, background processing, or operational features.

Before working on an area powered by cloops.microservices SDK, use below documentation index to get important usage guide for area of your interest. Once you read the respective docs then only start working on it.

1. Open the relevant guide from the Documentation Index below.
2. Follow the SDK's documented conventions rather than generic .NET or NATS patterns.
3. If a page is marked WIP, say so and use the nearest stable guide for context.
4. Keep implementation advice aligned with the linked docs.

> The links below are absolute URLs into the source repository
> ([connectionloops/cloops.microservices](https://github.com/connectionloops/cloops.microservices))
> so they resolve after this skill is installed onto any machine. Fetch a page's
> raw content by replacing `github.com/<owner>/<repo>/blob/` with
> `raw.githubusercontent.com/<owner>/<repo>/`.

## Documentation Index

- **0. Introduction to microservices** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/microservices.md
- **1. Getting started** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/installation.md
- **2. Automated DI Setup** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/di.md
- **3. Application Config** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/config.md
- **4. Registering your first NATS consumer, consumer interceptors, and consumer exception handlers** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/consumer.md
- **5. Strong Schema Architecture** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/schema.md
- **6. Utility Functions** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/util.md
- **7. Controllers** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/controllers.md
- **8. Services** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/services.md
- **9. Data Persistence** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/data-persistence.md
  - **9.1. Microsoft SQL Server (relational)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/mssql.md
    - **9.1.1. Making SQL Database Calls** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/db.md
    - **9.1.2. Database Migrations** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/migrations.md
  - **9.2. Manticore (searchable documents) (WIP)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/manticore.md
  - **9.3. NimbusDb (object store backed) (WIP)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/nimbusdb.md
  - **9.4. TigerBeetle (transactional workloads)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/tigerbeetle.md
  - **9.5. NATS KV** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/kv.md
  - **9.6. MinIO (WIP)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/min-io.md
- **10. Invoking other services** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/api.calls.md
- **11. Lightweight REST Endpoints** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/rest.md
- **12. Manually Testing Microservices on local** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/testing.md
- **13. Observability**
  - **13.1. Logging** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/logging.md
  - **13.2. Metrics** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/metrics.md
  - **13.3. Tracing (WIP)**
- **14. Feature Flagging (WIP)**
- **15. Background Processing** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/background-processing.md
  - **15.1. Background Jobs (cron based)** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/background-jobs.md
  - **15.2. Work Queue (WIP)**
- **16. Distributed Locks** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/distributed-locks.md
- **17. Decentralized JWT Auth for UI (WIP)**
- **18. Caching** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/caching.md
- **19. Additional Setup** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/additional-setup.md
- **20. Snowflake ID Generation** — https://github.com/connectionloops/cloops.microservices/blob/main/docs/snowflake-id.md

## Choosing a Persistence Backend

| Use case                                                         | Choose               |
| ---------------------------------------------------------------- | -------------------- |
| Globally unique, sortable, low-latency IDs without a coordinator | Snowflake (IdGen)    |
| Business data with relations; multiple tables modified together  | Microsoft SQL Server |
| Money, ledger, accounting — balances, transfers, double-entry    | TigerBeetle          |
| Fast get-by-key; value < 4KB; lowest latency; OCC / sequences    | NATS KV              |
| Entity masters searchable by many properties (< 50ms)            | Manticore (WIP)      |
| Massive objects retrieved by ID (~100ms)                         | NimbusDb (WIP)       |
| Blob storage                                                     | MinIO (WIP)          |

## Install

```bash
npx skills add connectionloops/cloops.microservices --skill cloops-microservices
```

Add `-g` to install globally across all projects.
