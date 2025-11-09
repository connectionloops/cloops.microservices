# CLOOPS Micrservices SDK

The official Connection Loops SDK for building high performance micrservices designed to run on CCNP. This is an opinionated approach to building microservices.

## Overview

The goal here is to minimize the undifferentiated work and get started with a project quickly. Cloops Microservices SDK gives you all the bells and whistles so that you can develop your service quite effortlessly.
Once SDK is pulled in, it is just a one liner setup to have full fledged microservice up and running.

> This SDK is supposed to be used when primary way to communicate to your microservice is NATS. **i.e. No REST interface**

### Benefits of using NATS over REST

- Lower latency
- Higher throughput
- No protocol bloat
- Can implement temporal decoupling quite easily
- No additional hops for load balancing
- Decentralized PKI based AuthN and AuthZ

## Installation

### New Projects (Recommended)

```bash
dotnet new cloops.microservice -n <name_of_service>
```

### Existing Projects

Add cloops.nats package reference to your `.csproj` file. Please note we deliberately want all of our consumers to stay on latest version of the SDK.

```xml
<PackageReference Include="cloops.microservices" Version="*" />
```

Once added, just to `dotnet restore` to pull in the SDK.

Usage (Program.cs) -

```cs

using CLOOPS.microservices;

var app = new App();
await app.RunAsync();

```

> Please note: since cloops microservices is quite opinionated, you might need to restructure project to make it work. Please read the features section for more information.

## Features

Out of the box implementations of -

- Observability
  - Logging
  - Metrics
  - _Tracing (WIP)_
- _Feature Flagging (WIP)_
- _Background jobs (WIP)_
- Automated DI Setup
  - Controllers
    - Each function is a handler for a NATS subject.
    - Controller is the starting point to process a request or published messaged.
    - (All classes under `.\*controllers` namespace are auto registered)
  - Services
    - A service executes business logic.
    - Typically controller executes one or more services as per what's needed to get the job done.
    - All classes under `.\*services` namespace are auto registered
  - Background Services
    - A piece of code that runs on background (usually on a continuous schedule)
    - e.g. a clean up service that deletes some things every 2 hours.
    - All classes under `.\*BackgroundServices` namespace and inherited from `BackgroundService` class are auto registered
  - Http services
    - Services with in-built http client
    - Used to make REST API calls to other third party services
- AppSettings
- Util
- MSSQL Server Client (DB.cs) (async streaming supported)
- CLOOPS.NATS functionality
  - Serialization and Deserialization
  - Client
  - Consumers
  - Publishers
  - KV and Distributed locking

## Environment Variables used by SDK

### NATS related environment variables

Below are the environment variables used by `CLOOPS.NATS`

- `NATS_URL`
  - Specifies the NATS server URL. Defaults to `tls://nats.ccnp.cloops.in:4222`.
  - For running on prod CCNP, make sure you set it to `tls://nats-headless.ccnp.cloops.in:4222` for faster within cluster operations.
- `NATS_CREDS`
  - Inline content of the NATS credentials file used for authentication. No default.
- `NATS_SUBSCRIPTION_QUEUE_SIZE`
  - This vaiable defines what should be the max limmit of messages queued up for each subscription. Use this to control backpressure. Default: 20,000
- `NATS_CONSUMER_MAX_DOP`
  - Defines maximum degree of parallelism for all consumers. These many messages can be processed in parallel from the message queue. Default: 128
  - This puts upper limit on rps (request per second), not literally, but indirectly. (e.g. if your avg latency is 200ms then max_dop \* 5 is your max throughput). Increase this in order to support higher rps. Consider giving higher core / memory count as well.
- **(Caution)** Minting Service related environment variables
  - **Highly confidential. Only use in trusted services running on trusted infrastructure**.
  - Used by minting service when you need your application to mint new Nats Tokens.
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

### Microservice operation related environment variables

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

## Additional Setup you might need to do

1. Database deployment
2. NATS JetStream streams, consumer creation
3. Doppler project creation

Please reach out to platform team for help on these.

## FAQ

### How do I use different version of `CLOOPS.NATS`?

`CLOOPS.Microservices` ships with latest version of `CLOOPS.NATS`. So all you have to do is just rebuild your service to bump up the `CLOOPS.NATS` version.

With that said, you can override exact version in your project if you need to. Please see below code. if you keep version as "\*", whenever you project gets built, it will pull in latest `CLOOPS.NATS` version.

```
<PackageReference Include="cloops.microservices" Version="*" />
    <!-- uncomment this if you need a specific version of cloops.nats -->
    <!-- <PackageReference Include="cloops.nats" Version="*" /> -->
```
