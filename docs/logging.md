# Logging

The `cloops.microservices` SDK uses [Serilog](https://serilog.net/) for structured logging, integrated with Microsoft.Extensions.Logging. This provides a powerful, flexible logging system that works seamlessly with log collectors in production environments.

## Table of Contents

1. [Getting a Logger Through Dependency Injection](#getting-a-logger-through-dependency-injection)
2. [Using the Logger](#using-the-logger)
3. [Customizing Log Levels](#customizing-log-levels)
4. [Production Logging Setup](#production-logging-setup)

## Getting a Logger Through Dependency Injection

The SDK automatically configures Serilog and integrates it with Microsoft.Extensions.Logging. To use logging in your services, inject `ILogger<T>` through constructor dependency injection:

```csharp
using Microsoft.Extensions.Logging;

namespace YourApp.Services;

public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }
}
```

**Important:** Always use the generic `ILogger<T>` where `T` is your service class. This provides better log filtering and categorization.

## Using the Logger

The `ILogger<T>` interface provides several methods for logging at different levels:

### Log Levels

- **`LogTrace`** - Very detailed logs, typically only useful during development
- **`LogDebug`** - Detailed information for debugging
- **`LogInformation`** - General informational messages about application flow
- **`LogWarning`** - Warning messages for unexpected but recoverable situations
- **`LogError`** - Error messages for failures that don't stop the application
- **`LogCritical`** - Critical failures that may cause the application to abort

### Examples

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    public async Task ProcessOrder(Order order)
    {
        _logger.LogInformation("Processing order {OrderId} for customer {CustomerId}",
            order.Id, order.CustomerId);

        try
        {
            // Process order logic
            _logger.LogDebug("Order {OrderId} validated successfully", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
            throw;
        }
    }
}
```

### Structured Logging

Serilog supports structured logging using placeholders. Instead of string interpolation, use placeholders:

```csharp
// ✅ Good - Structured logging
_logger.LogInformation("User {UserId} logged in from {IPAddress}", userId, ipAddress);

// ❌ Avoid - String interpolation loses structure
_logger.LogInformation($"User {userId} logged in from {ipAddress}");
```

Structured logging allows log aggregators to parse and query logs by specific fields, making debugging and monitoring much more powerful.

## Customizing Log Levels

Log levels can be customized through environment variables via `AppSettings`.

### Default Configuration

By default, the SDK configures:

- **Minimum Level:** `Information`
- **Framework Overrides:** `System.Net.Http`, `Microsoft`, and `System` namespaces are set to `Warning` to reduce noise

### Enabling Debug Logging

To enable debug-level logging, set the `DEBUG` environment variable to `true`:

```bash
export DEBUG=true
```

This will:

- Set the minimum log level to `Debug`
- Allow `LogDebug` and `LogTrace` messages to be emitted

### Environment-Based Configuration

The logging configuration is automatically adjusted based on the environment:

- **Production:** Logs are output in compact JSON format (structured logging) for easy parsing by log collectors
- **Non-Production:** Logs are output in a human-readable, colorized format for easier development

The environment is determined by the `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` environment variable.

## Production Logging Setup

In production environments (especially Kubernetes), logs are typically collected by log aggregation systems. The SDK is configured to work seamlessly with these systems.

### How It Works

1. **Structured JSON Output:** In production, the SDK outputs logs in compact JSON format using Serilog's `CompactJsonFormatter`. This format is easily parseable by log collectors.

2. **Log Collection:** Log collectors (like Alloy, Fluentd, or Promtail) read logs from stdout/stderr of your containers.

3. **Log Aggregation:** Collected logs are forwarded to centralized log aggregation platforms for storage, search, and analysis.

### Log Collectors for Kubernetes

The SDK's structured JSON logging works with any log collector that can parse JSON. Here are some popular options:

#### Alloy (Grafana Alloy) [Recommended]

- **Documentation:** https://grafana.com/docs/alloy/latest/
- Lightweight, vendor-neutral log collector
- Part of the Grafana observability stack
- Supports OTLP and various log backends

#### Fluentd

- **Documentation:** https://docs.fluentd.org/
- Popular open-source log collector
- Large ecosystem of plugins
- Works with many log backends (Elasticsearch, Splunk, etc.)

#### Vector

- **Documentation:** https://vector.dev/docs/
- High-performance observability data pipeline
- Supports many sources and sinks
- Good performance characteristics

### Log Aggregation Platforms

Once logs are collected, they're typically sent to aggregation platforms:

- **Grafana Loki:** https://grafana.com/docs/loki/latest/ - Log aggregation system inspired by Prometheus
- **Elasticsearch + Kibana:** https://www.elastic.co/elasticsearch/ - Popular ELK stack
- **Splunk:** https://www.splunk.com/ - Enterprise log management
- **Datadog:** https://www.datadoghq.com/ - Cloud monitoring and logging
- **New Relic:** https://newrelic.com/ - Application performance monitoring with logging

### Example Kubernetes Setup

In a typical Kubernetes setup with Alloy:

1. **Application logs** are written to stdout/stderr in JSON format
2. **Alloy DaemonSet** collects logs from all pods
3. **Alloy forwards** logs to your chosen backend (Loki, Elasticsearch, etc.)
4. **Dashboards** in Grafana/Kibana visualize and query the logs

The SDK requires no additional configuration - it automatically outputs structured JSON in production environments, making it compatible with any log collector.

### Log Enrichment

The SDK automatically enriches all logs with:

- **Application name** (from `AssemblyName`)
- **Thread ID** and **Thread Name**
- **Log context** (from `LogContext`)
- **Timestamp** and **Log Level**

These enrichments help with filtering and correlation in log aggregation systems.

---

[Back to documentation index](./README.md)
