# CLOOPS Microservices SDK

An opinionated library to build highly available, lean and scalable cloud native microservices using [NATS](https://nats.io/) as the primary communication layer.

See [Change Log](/docs/change-log.md) for change log and what's new.

> versions 1.1.14 and above target .net 10. If you need .net 9, please use 1.1.13.

## 🎯 Overview

Minimize undifferentiated work and get all the bells and whistles for a lean, high-performance, and scalable microservices setup. Check out the [docs](/docs/README.md) for detailed guides.

> This SDK uses NATS as the primary way for services to communicate. Lightweight REST support is included for Kubernetes probes and webhook ingress. See [REST Support](/docs/rest.md) for more details.

### 🚀 Why C# for Microservices?

Modern C# is an excellent choice for building microservices! It's lean, fast, and fully open source with cross-platform support. With .NET's native Linux support, you get exceptional performance that rivals or exceeds many other languages. C# offers a perfect balance of developer productivity, type safety, and runtime efficiency—making it ideal for high-throughput, low-latency microservices architectures. Plus, with features like async/await, minimal APIs, and native AOT compilation, you can build services that are both performant and maintainable.

### ⚡ Why NATS over REST?

- **Lower latency** - Direct messaging without HTTP overhead
- **Higher throughput** - Optimized for performance at scale
- **No protocol bloat** - Lightweight and efficient
- **Temporal decoupling** - Services communicate asynchronously
- **Built-in load balancing** - No additional hops required
- **Decentralized PKI** - AuthN and AuthZ without central servers
- **Zero exposed ports** - Services connect to NATS, not each other
- **Global distribution** - Easy to create highly available, globally distributed services

### 📚 Learning NATS

- [YouTube Playlist](https://www.youtube.com/playlist?list=PLgqCaaYodvKY22TpvwlsalIArTmc56W9h)
- [Official Docs](https://docs.nats.io/)
- [Rethinking Microservices with NATS](https://youtu.be/AiUazlrtgyU?si=B6XDRiniyw8hu4GF)
- [Escaping the HTTP Mindset (Podcast)](https://podcasts.apple.com/us/podcast/ep03-escaping-the-http-mindset-with-nats-io/id1700459773?i=1000625476010)
- [NATS Super Clusters](https://docs.nats.io/running-a-nats-service/configuration/gateways)

## 📖 Documentation

Comprehensive guides available in the [docs](/docs) directory.

For data persistence, see [SQL database operations](/docs/db.md) and [TigerBeetle ledger database usage](/docs/tigerbeetle.md).

> This SDK is built on [cloops.nats](https://github.com/connectionloops/cloops.nats), which provides annotation-based consumer definitions and foundational features.

## Agent Skill

Install agent skill with 

```shell
npx skills add connectionloops/cloops.microservices --skill cloops-microservices
```

