# 📚 Documentation - cloops.microservices

Welcome to the **cloops.microservices** SDK documentation! 🎉 This SDK provides an opinionated, production-ready framework for building highly available, lean, and scalable cloud-native microservices using [NATS](https://nats.io/) as the primary communication layer.

## 📖 About This Documentation

This documentation is designed to guide you through building microservices with the cloops.microservices SDK. Whether you're just getting started or looking to implement advanced features, you'll find detailed guides covering everything from installation to distributed locking.

### What You'll Learn

- 🚀 **Getting Started**: Installation and setup instructions
- 🔧 **Core Concepts**: Dependency injection, configuration, controller and service registration
- 📡 **NATS Integration**: Building controllers with consumers and implementing request-reply or publish-subscribe patterns
- 💾 **Data Persistence**: SQL Server, TigerBeetle, and other storage backends for relational, searchable, and transactional workloads
- 🌐 **Communication**: Inter-service calls and lightweight REST endpoints
- ⚡ **Advanced Features**: Distributed locking, observability, and more

### Prerequisites

Before diving in, it's recommended that you:

- 💻 Understand C# and .NET development, but its cool if you don't. C# is very easy to learn.
- 🏗️ Have basic knowledge of microservices architecture

> 💡 **Note**: This SDK uses NATS as the primary communication mechanism between services. Lightweight REST support is included for Kubernetes probes and webhook ingress. If you're new to NATS, check out the [main README](../README.md) for learning resources.

## 📑 Index

- **0. 🏗️ [Introduction to microservices](./microservices.md)**
- **1. 🚀 [Getting started](./installation.md)**
- **2. 🔌 [Automated DI Setup](./di.md)**
- **3. ⚙️ [Application Config](./config.md)**
- **4. 📨 [Registering your first NATS consumer (interceptors + exception handlers)](./consumer.md)**
- **5. 📋 [Strong Schema Architecture](./schema.md)**
- **6. 🛠️ [Utility Functions](./util.md)**
- **7. 🎮 [Controllers](./controllers.md)**
- **8. 🔧 [Services](./services.md)**
- **9. 💾 [Data Persistence](./data-persistence.md)**
  - **9.1. 🗃️ [Microsoft SQL Server (relational)](./mssql.md)**
    - **9.1.1. [Making SQL Database Calls](./db.md)**
    - **9.1.2. [Database Migrations](./migrations.md)**
  - **9.2. 🔍 [Manticore (searchable documents) (WIP)](./manticore.md)**
  - **9.3. 🗄️ [NimbusDb (object store backed) (WIP)](./nimbusdb.md)**
  - **9.4. 🐯 [TigerBeetle (transactional workloads)](./tigerbeetle.md)**
  - **9.5. 📦 [NATS KV](./kv.md)**
  - **9.6. 📦 [MinIO (WIP)](./min-io.md)**
- **10. 🌐 [Invoking other services](./api.calls.md)**
- **11. 🌎 [Lightweight REST Endpoints](./rest.md)**
- **12. 🧪 [Manually Testing Microservices on local](./testing.md)**
- **13. 📊 Observability**
  - **13.1. 📝 [Logging](./logging.md)**
  - **13.2. 📈 [Metrics](./metrics.md)**
  - **13.3. 🔍 Tracing (WIP)**
- **14. 🚩 Feature Flagging (WIP)**
- **15. [Background Processing](./background-processing.md)**
  - **15.1 ⏰ [Background Jobs (cron based)](./background-jobs.md)**
  - **15.2 [Work Queue (WIP)](./)**
- **16. 🔒 [Distributed Locks](./distributed-locks.md)**
- **17. 🔐 Decentralized JWT Auth for UI**
- **18. 💨 [Caching](./caching.md)**
- **19. 🧭 [Additional Setup](./additional-setup.md)**
- **20. ❄️ [Snowflake ID Generation](./snowflake-id.md)**
