# Application Configuration

## Table of Contents

1. [Overview](#overview)
2. [BaseAppSettings](#baseappsettings)
3. [Extending AppSettings](#extending-appsettings)
4. [Environment Variables](#environment-variables)
5. [Secret Management](#secret-management)
6. [Dependency Injection](#dependency-injection)
7. [Examples](#examples)

## Overview

The `cloops.microservices` SDK provides a simple and flexible configuration system based on environment variables. Configuration is managed through the `BaseAppSettings` class, which is automatically registered in the dependency injection container.

## BaseAppSettings

`BaseAppSettings` is the base configuration class that reads values from environment variables. It includes the following properties:

- `Debug` - Enable verbose debugging (default: `False`)
- `NatsURL` - NATS server URL (default: `tls://nats.ccnp.cloops.in:4222`)
- `NatsCreds` - NATS credentials content
- `AssemblyName` - Application assembly name (auto-detected)
- `OtelEndpoint` - OpenTelemetry endpoint URL
- `OtelHeaders` - OpenTelemetry headers
- `Cluster` - Target cluster name (default: `ccnp`)
- `ConnectionString` - SQL database connection string
- `EnableNatsConsumers` - Enable/disable NATS consumers (default: `False`)

All properties use `init` accessors and read from environment variables with sensible defaults.

## Extending AppSettings

You can extend `BaseAppSettings` to add your own configuration properties:

```csharp
namespace yourapp.services;

public class AppSettings : BaseAppSettings
{
    public string ApiKey { get; init; } = Environment.GetEnvironmentVariable("API_KEY") ?? "";

    public int MaxRetries { get; init; } = int.Parse(
        Environment.GetEnvironmentVariable("MAX_RETRIES") ?? "3"
    );

    public bool FeatureEnabled { get; init; } = Convert.ToBoolean(
        Environment.GetEnvironmentVariable("FEATURE_ENABLED") ?? "False"
    );
}
```

Make sure the `AppSettings` you just created is in `.*.services` namespace so that it is auto registered in DI.

```csharp
var app = new App();
app.appSettings = new AppSettings(); // Use your custom class
app.builder.Services.AddSingleton<AppSettings>(app.appSettings);
```

## Environment Variables

Configuration values are loaded from environment variables. Set them before starting your application. Below is a comprehensive reference of all environment variables used by the SDK.

### Environment Variables Reference

| Variable Name                  | Category       | Description                                                                                                                                                                                                                                                                                                                                                          | Default Value                    | Required |
| ------------------------------ | -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------- | -------- |
| `DEBUG`                        | Microservice   | Turns on verbose logging and additional diagnostics when set to `True`                                                                                                                                                                                                                                                                                               | `False`                          | No       |
| `NATS_URL`                     | NATS           | Specifies the NATS server URL. For running on prod CCNP, set to `tls://nats-headless.ccnp.cloops.in:4222` for faster within cluster operations                                                                                                                                                                                                                       | `tls://nats.ccnp.cloops.in:4222` | No       |
| `NATS_CREDS`                   | NATS           | Inline content of the NATS credentials file used for authentication                                                                                                                                                                                                                                                                                                  | None                             | No       |
| `NATS_SUBSCRIPTION_QUEUE_SIZE` | NATS           | Maximum limit of messages queued up for each subscription. Use this to control backpressure                                                                                                                                                                                                                                                                          | `20000`                          | No       |
| `NATS_CONSUMER_MAX_DOP`        | NATS           | Maximum degree of parallelism for all consumers. These many messages can be processed in parallel from the message queue. This puts upper limit on rps (request per second) indirectly (e.g. if your avg latency is 200ms then max_dop × 5 is your max throughput). Increase this in order to support higher rps. Consider giving higher core / memory count as well | `128`                            | No       |
| `NATS_ACCOUNT_SIGNING_SEED`    | NATS (Minting) | Signing account seed. **⚠️ Highly confidential. Only use in trusted services running on trusted infrastructure.** Used by minting service when you need your application to mint new NATS tokens. See instructions below                                                                                                                                             | None                             | No       |
| `NATS_ACCOUNT_PUBLIC_KEY`      | NATS (Minting) | Main account public key. **⚠️ Highly confidential. Only use in trusted services running on trusted infrastructure.** Used by minting service when you need your application to mint new NATS tokens. See instructions below                                                                                                                                          | None                             | No       |
| `CCNPOTELENDPOINT`             | Microservice   | OTEL collector endpoint for exporting telemetry to CCNP                                                                                                                                                                                                                                                                                                              | None                             | No       |
| `CCNPOTELHEADERS`              | Microservice   | Additional OTEL headers required when sending telemetry to CCNP                                                                                                                                                                                                                                                                                                      | None                             | No       |
| `CLUSTER`                      | Microservice   | Target cluster where the service runs                                                                                                                                                                                                                                                                                                                                | `ccnp`                           | No       |
| `CNSTR`                        | Microservice   | SQL database connection string                                                                                                                                                                                                                                                                                                                                       | None                             | No       |
| `ENABLE_NATS_CONSUMERS`        | Microservice   | Controls whether NATS consumers start with the service                                                                                                                                                                                                                                                                                                               | `False`                          | No       |

### Setting Environment Variables

Set environment variables before starting your application:

```bash
export DEBUG=True
export NATS_URL=tls://nats.example.com:4222
export NATS_CREDS="your-credentials-here"
export CNSTR="Server=localhost;Database=mydb;..."
export CLUSTER=production
export ENABLE_NATS_CONSUMERS=True
```

### NATS Minting Service Variables

**⚠️ Caution:** The following environment variables are highly confidential and should only be used in trusted services running on trusted infrastructure. They are used by the minting service when you need your application to mint new NATS tokens.

#### NATS_ACCOUNT_SIGNING_SEED

**How to get it:**

1. On nats-box
2. Run `cd /data/nsc/nkeys/keys/A`
3. Run `find . -type f -name "*.nk" -o -name "*.seed"`
4. Run `cat <account-signing-public-key>.nk` to get the account signing seed
   - **Important:** Remember to pick public key of signing account, not main account

#### NATS_ACCOUNT_PUBLIC_KEY

**How to get it:**

- Run this on nats-box to get the account public key: `nsc list keys --account=<account-name>`
  - **Important:** Remember to pick the main account, not signing key

## Secret Management

**⚠️ Important:** Never commit sensitive credentials (API keys, connection strings, tokens) to version control.

We strongly recommend using a secret management service like **Doppler** to securely manage your environment variables, especially in production environments.

### Using Doppler

1. **Install Doppler CLI:**

   ```bash
   brew install doppler  # macOS
   # or
   curl -Ls --tlsv1.2 --proto "=https" https://cli.doppler.com/install.sh | sh
   ```

2. **Login and setup:**

create a file doppler.yaml

```yaml
setup:
  - project: your-project
    config: dev
```

To login to doppler, just export an environment variable `DOPPLER_TOKEN` with your doppler token.

and run below command -

```bash
doppler setup
```

3. **Run your application with Doppler:**

   ```bash
   doppler run -- dotnet run
   ```

Doppler automatically injects environment variables from your configured project and config, keeping secrets out of your codebase and deployment manifests.

> If using doppler on kubernetes or other production environments, check out official doppler [integration guide](https://www.doppler.com/integrations).

### Other Secret Management Options

- **Azure Key Vault** - For Azure-hosted applications
- **AWS Secrets Manager** - For AWS-hosted applications
- **HashiCorp Vault** - For self-hosted secret management
- **Kubernetes Secrets** - For containerized deployments

## How to read configs through Dependency Injection

`BaseAppSettings` (or your custom `AppSettings`) is automatically registered as a singleton in the DI container during application initialization. You can inject it into any service through constructor injection.

### Basic Injection

```csharp
namespace your.namespace.services;

public class MyService
{
    private readonly AppSettings _settings;

    public MyService(AppSettings settings)
    {
        _settings = settings;
    }

    public void DoSomething()
    {
        if (_settings.Debug)
        {
            Console.WriteLine("Debug mode enabled");
        }
    }
}
```

---

**Next Steps:**

- **[How to register NATS consumer](./consumer.md)**
- [Back to documentation index](./README.md)
