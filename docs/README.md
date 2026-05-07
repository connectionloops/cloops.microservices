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

> 💡 **Note**: This SDK uses NATS as the primary communication mechanism between services—no REST interfaces required. If you're new to NATS, check out the [main README](../README.md) for learning resources.

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
- **12. 🧪 [Manually Testing Microservices on local](./testing.md)**
- **13. 📊 Observability**
  - **13.1. 📝 [Logging](./logging.md)**
  - **13.2. 📈 [Metrics](./metrics.md)**
  - **13.3. 🔍 Tracing (WIP)**
- **14. 🚩 Feature Flagging (WIP)**
- **15. 🗄️ NimbusDb (WIP)**
- **16. ⏰ Background Jobs(WIP)**
- **17. 🔒 [Distributed Locks](./distributed-locks.md)**
- **18. 🔐 Decentralized JWT Auth for UI**
- **19. 💨 Caching (WIP)**
- **20. 🧭 [Additional Setup](./additional-setup.md)**
