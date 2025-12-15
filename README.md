# CLOOPS Microservices SDK

An opinionated library to build highly available, lean and scalable cloud native microservices using [NATS](https://nats.io/) as primary communication layer.

## 🚀 Overview

The goal here is to minimize the undifferentiated work and give you all the bells and whistles for a lean, high performance and scalable microservices setup. Check out [docs](/docs/README.md) for more details on how this SDK works, and prepare to be impressed!

> This SDK uses NATS as the primary way for your services to communicate with each other. **i.e. No REST interface**

### 💎 Why C# for Microservices?

Modern C# is an excellent choice for building microservices! It's lean, fast, and fully open source with cross-platform support. With .NET's native Linux support, you get exceptional performance that rivals or exceeds many other languages. C# offers a perfect balance of developer productivity, type safety, and runtime efficiency—making it ideal for high-throughput, low-latency microservices architectures. Plus, with features like async/await, minimal APIs, and native AOT compilation, you can build services that are both performant and maintainable.

It is highly recommended that you familiarize yourself with NATS. Below are some really good resources -

1. [YouTube Playlist](https://www.youtube.com/playlist?list=PLgqCaaYodvKY22TpvwlsalIArTmc56W9h)
2. [Official Docs](https://docs.nats.io/)

This SDK focuses on building microserivices using NATS. It is based on the [cloops.nats](https://github.com/connectionloops/cloops.nats) which provides annotations based way to define consumers and bunch of other foundational things. Please do check it out.

### ⚡ Why use NATS over REST?

- Lower latency
- Higher throughput
- No protocol bloat
- Can implement temporal decoupling quite easily
- No additional hops for load balancing
- Decentralized PKI based AuthN and AuthZ
- No network ports exposed
- Easy to create highly available, globally distributed services

If you would like to know more, I would highly recommend checking out below resources

- [Rethinking Microservices with NATS](https://youtu.be/AiUazlrtgyU?si=B6XDRiniyw8hu4GF)
- [this podcast from nats.fm](https://podcasts.apple.com/us/podcast/ep03-escaping-the-http-mindset-with-nats-io/id1700459773?i=1000625476010)
- [NATS super clusters](https://docs.nats.io/running-a-nats-service/configuration/gateways)

## Documentation

Check out the [docs](/docs).

## 🌱 Environment Variables used by SDK

### 📡 NATS related environment variables

Below are the environment variables used by `CLOOPS.NATS`

- `NATS_URL`
  - Specifies the NATS server URL. Defaults to `tls://nats.ccnp.cloops.in:4222`.
  - For running on prod CCNP, make sure you set it to `tls://nats-headless.ccnp.cloops.in:4222` for faster within cluster operations.
- `NATS_CREDS`
  - Inline content of the NATS credentials file used for authentication. No default.
- `NATS_SUBSCRIPTION_QUEUE_SIZE`
  - This variable defines what should be the max limit of messages queued up for each subscription. Use this to control backpressure. Default: 20,000
- `NATS_CONSUMER_MAX_DOP`
  - Defines maximum degree of parallelism for all consumers. These many messages can be processed in parallel from the message queue. Default: 128
  - This puts upper limit on rps (request per second), not literally, but indirectly. (e.g. if your avg latency is 200ms then max_dop \* 5 is your max throughput). Increase this in order to support higher rps. Consider giving higher core / memory count as well.
- **(Caution)** Minting Service related environment variables
  - **Highly confidential. Only use in trusted services running on trusted infrastructure**.
  - Used by minting service when you need your application to mint new NATS tokens.
  - `NATS_ACCOUNT_SIGNING_SEED`
    - Signing account seed
    - How to get it
      - On nats-box
      - run `cd /data/nsc/nkeys/keys/A`
      - run `find . -type f -name "*.nk" -o -name "*.seed"`
      - run `cat <account-signing-public-key>.nk` to get the account signing seed. (remember to pick public key of singing account not main account)
  - `NATS_ACCOUNT_PUBLIC_KEY`
    - Main account public key
    - How to get it
      - Run this on nats-box to get the account public key: `nsc list keys --account=<account-name>` (remember to pick the main account not signing key)

### 🧪 Microservice operation related environment variables

- `DEBUG`
  - Turns on verbose logging and additional diagnostics when set to `True`. Defaults to `False`.
- `CCNPOTELENDPOINT`
  - OTEL collector endpoint for exporting telemetry to CCNP. No default.
- `CCNPOTELHEADERS`
  - Additional OTEL headers required when sending telemetry to CCNP. No default.
- `CLUSTER`
  - Target cluster where the service runs. Defaults to `ccnp`.
- `CNSTR`
  - SQL connection string. No default.
- `ENABLE_NATS_CONSUMERS`
  - Controls whether NATS consumers start with the service. Defaults to `False`.

## 🧭 Additional Setup you might need to do

1. Database deployments.
2. NATS JetStream streams, consumer creation
3. Secret management (Doppler) integration

These things are usually taken care at CI/CD level or as manual one time setup operations or combination of both. How to do these things highly varies based on project and company.
