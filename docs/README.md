# 📚 Documentation - cloops.microservices

Welcome to the **cloops.microservices** SDK documentation! 🎉 This SDK provides an opinionated, production-ready framework for building highly available, lean, and scalable cloud-native microservices using [NATS](https://nats.io/) as the primary communication layer.

## 📖 About This Documentation

This documentation is designed to guide you through building microservices with the cloops.microservices SDK. Whether you're just getting started or looking to implement advanced features, you'll find detailed guides covering everything from installation to distributed locking.

### What You'll Learn

- 🚀 **Getting Started**: Installation and setup instructions
- 🔧 **Core Concepts**: Dependency injection, configuration, and service registration
- 📡 **NATS Integration**: Building consumers and implementing request-reply patterns
- 💾 **Data & Communication**: Database operations and inter-service communication
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
- **7. 🔧 [Services](./services.md)**
- **8. 💾 [Making Database Calls](./db.md)**
- **9. 🌐 [Invoking other services](./api.calls.md)**
- **10. 📊 Observability**
  - **10.1. 📝 [Logging](./logging.md)**
  - **10.2. 📈 [Metrics](./metrics.md)**
  - **10.3. 🔍 Tracing (WIP)**
- **11. 🚩 Feature Flagging (WIP)**
- **12. 🗄️ NimbusDb (WIP)**
- **13. ⏰ Background Jobs(WIP)**
- **14. 🔒 [Distributed Locks](./distributed-locks.md)**
- **15. 🔐 Decentralized JWT Auth for UI**
- **16. 💨 Caching (WIP)**
