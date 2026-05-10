# 📚 Documentation - cloops.microservices

Welcome to the **cloops.microservices** SDK documentation! 🎉 This SDK provides an opinionated, production-ready framework for building highly available, lean, and scalable cloud-native microservices using [NATS](https://nats.io/) as the primary communication layer.

## 📖 About This Documentation

This documentation is designed to guide you through building microservices with the cloops.microservices SDK. Whether you're just getting started or looking to implement advanced features, you'll find detailed guides covering everything from installation to distributed locking.

### What You'll Learn

- 🚀 **Getting Started**: Installation and setup instructions
- 🔧 **Core Concepts**: Dependency injection, configuration, controller and service registration
- 📡 **NATS Integration**: Building controllers with consumers and implementing request-reply or publish-subscribe patterns
- 💾 **Data & Communication**: SQL database operations, TigerBeetle ledger operations, and inter-service communication
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
- **4. 📨 [Registering your first NATS consumer](./consumer.md)**
- **5. 📋 [Strong Schema Architecture](./schema.md)**
- **6. 🛠️ [Utility Functions](./util.md)**
- **7. 🎮 [Controllers](./controllers.md)**
- **8. 🔧 [Services](./services.md)**
- **9. 💾 [Making SQL Database Calls](./db.md)**
- **10. 🐯 [TigerBeetle Ledger Database](./tigerbeetle.md)**
- **11. 🌐 [Invoking other services](./api.calls.md)**
- **12. 🌎 [Lightweight REST Endpoints](./rest.md)**
- **13. 🧪 [Manually Testing Microservices on local](./testing.md)**
- **14. 📊 Observability**
  - **14.1. 📝 [Logging](./logging.md)**
  - **14.2. 📈 [Metrics](./metrics.md)**
  - **14.3. 🔍 Tracing (WIP)**
- **15. 🚩 Feature Flagging (WIP)**
- **16. 🗄️ NimbusDb (WIP)**
- **17. ⏰ Background Jobs(WIP)**
- **18. 🔒 [Distributed Locks](./distributed-locks.md)**
- **19. 🔐 Decentralized JWT Auth for UI**
- **20. 💨 [Caching](./caching.md)**
- **21. 🧭 [Additional Setup](./additional-setup.md)**
