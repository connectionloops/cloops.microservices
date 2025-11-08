# CLOOPS Micrservices SDK

The official Connection Loops SDK for building high performance micrservices designed to run on CCNP. This is an opinionated approach to building microservices.

## Overview

The goal here is to minimize the undifferentiated work and get started with a project quickly. Cloops Microservices SDK gives you all the bells and whistles so that you can develop your service quite effortlessly.

## Installation

### New Projects (Recommended)

```bash
dotnet new cloops.microservices <name_of_service>
```

### Existing Projects

Add cloops.nats package reference to your `.csproj` file. Please note we deliberately want all of our consumers to stay on latest version of the SDK.

```xml
<PackageReference Include="cloops.microservices" Version="*" />
```

Once added, just to `dotnet restore` to pull in the SDK.

> Please note: since cloops microservices is quite opinionated, you might need to restructure project to make it work. Please read the features section for more information.

## Quickstarts

Take a look at some examples [here](./examples/)

## Documentation

For detailed documentation, setup instructions, and contribution guidelines:

📚 **[View Complete Documentation](./docs/)**

## Environment Variables used by SDK

- `NATS_SUBSCRIPTION_QUEUE_SIZE`
  - This vaiable defines what should be the max limmit of messages queued up for each subscription. Use this to control backpressure. Default: 10,000
