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

Configuration values are loaded from environment variables. Set them before starting your application:

```bash
export DEBUG=True
export NATS_URL=tls://nats.example.com:4222
export NATS_CREDS="your-credentials-here"
export CNSTR="Server=localhost;Database=mydb;..."
export CLUSTER=production
export ENABLE_NATS_CONSUMERS=True
```

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

- Learn about [Services](./services.md)
- Explore [Database Operations](./db.md)
- Check out [Utility Functions](./util.md)
