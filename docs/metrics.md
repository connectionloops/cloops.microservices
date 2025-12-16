# Metrics

The `cloops.microservices` SDK provides built-in metrics collection using [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics), integrated with OpenTelemetry for observability. This enables you to monitor your microservice's performance, track business metrics, and export data to various observability platforms.

## Table of Contents

1. [Overview](#overview)
2. [Out-of-the-Box Metrics](#out-of-the-box-metrics)
3. [Creating Custom Metrics](#creating-custom-metrics)
4. [Using Metrics in Your Services](#using-metrics-in-your-services)
5. [OpenTelemetry Integration](#opentelemetry-integration)
6. [Metric Types](#metric-types)
7. [Best Practices](#best-practices)

## Overview

The SDK provides two types of metrics:

1. **Automatic NATS Metrics**: Automatically tracked for all NATS message handlers
2. **Custom Application Metrics**: Metrics you define for your business logic

All metrics are automatically exported via OpenTelemetry if configured, making them available to:

- VictoriaMetrics / Prometheus -> Grafana
- Application Insights
- Any OTLP-compatible observability platform

## Out-of-the-Box Metrics

The SDK automatically provides `NatsMetricsService` which tracks NATS message processing performance. This service is automatically registered when you configure NATS in your application.

### Available NATS Metrics

The `NatsMetricsService` automatically tracks the following metric:

**`nats_sub_msg_process_milliseconds`** (Histogram)

- **Description**: Time taken to process a NATS subscription message
- **Unit**: Milliseconds
- **Labels/Tags**:
  - `fn`: Function name that processed the message
  - `status`: Execution status (`success` or `fail`)
  - `retryable`: Whether the failure is retryable (`true` or `false`)

This histogram automatically generates three metric series:

- `nats_sub_msg_process_milliseconds_count`: Total count of messages processed
- `nats_sub_msg_process_milliseconds_sum`: Sum of all processing durations
- `nats_sub_msg_process_milliseconds_bucket`: Histogram buckets for quantile calculations (p50, p95, p99, etc.)

### Automatic Tracking

Metrics are automatically recorded for all NATS message handlers. You don't need to do anything - the SDK handles it:

```csharp
using CLOOPS.NATS.Attributes;
using CLOOPS.NATS;

namespace YourApp.Services;

public class OrderService
{
    [NatsConsumer("orders.process")]
    public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct)
    {
        // Your business logic here
        await ProcessOrderAsync(msg.Data);

        // Metrics are automatically recorded:
        // - Processing duration
        // - Success/failure status
        // - Function name (ProcessOrder)
        return NatsAck.Success;
    }
}
```

The SDK automatically tracks:

- How long each handler takes to execute
- Request counts for each handler.
- Whether the handler succeeded or failed
- Whether failures are retryable
- The function name for filtering and aggregation

## Creating Custom Metrics

While the SDK provides automatic NATS metrics, you'll often need to track custom business metrics. The SDK makes this easy by providing a template and automatic OpenTelemetry integration.

### Step 1: Create Your Metrics Service

Create a service class that defines your custom metrics. The SDK automatically discovers and registers services in namespaces ending with `Services`:

```csharp
using System.Diagnostics.Metrics;

namespace YourApp.Services;

public class AppMetricsService
{
    private readonly Meter _meter;

    // Define your custom metrics as properties
    public Counter<long> OrdersProcessed { get; }
    public Histogram<double> OrderProcessingTime { get; }
    public Counter<long> DatabaseErrors { get; }

    public AppMetricsService()
    {
        // Create a meter with the name "AppMetrics"
        // This name must match what's configured in OpenTelemetry
        _meter = new Meter("AppMetrics");

        // Create a counter for tracking order counts
        OrdersProcessed = _meter.CreateCounter<long>(
            "orders_processed_total",
            "count",
            "Total number of orders processed"
        );

        // Create a histogram for tracking processing time
        OrderProcessingTime = _meter.CreateHistogram<double>(
            "order_processing_duration",
            "ms",
            "Time taken to process an order in milliseconds"
        );

        // Create a counter for tracking errors
        DatabaseErrors = _meter.CreateCounter<long>(
            "database_errors_total",
            "count",
            "Total number of database errors"
        );
    }

    // Helper methods to record metrics with tags
    public void RecordOrderProcessed(string orderType, string status)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("order_type", orderType),
            new("status", status)
        };
        OrdersProcessed.Add(1, tags.AsSpan());
    }

    public void RecordOrderProcessingTime(double durationMs, string orderType)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("order_type", orderType)
        };
        OrderProcessingTime.Record(durationMs, tags.AsSpan());
    }

    public void RecordDatabaseError(string operation, string errorType)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("operation", operation),
            new("error_type", errorType)
        };
        DatabaseErrors.Add(1, tags.AsSpan());
    }
}
```

> Important: Don't forget to create the service in `.*.services` namespace for the framework to detect and register it.

### Step 2: Use Your Metrics Service

Inject your metrics service into other services and use it to record metrics:

```csharp
using Microsoft.Extensions.Logging;

namespace YourApp.Services;

public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly AppMetricsService _metrics;

    public OrderService(ILogger<OrderService> logger, AppMetricsService metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    [NatsConsumer("orders.process")]
    public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await ProcessOrderAsync(msg.Data);

            // Record success metrics
            _metrics.RecordOrderProcessed(msg.Data.OrderType, "success");
            _metrics.RecordOrderProcessingTime(stopwatch.ElapsedMilliseconds, msg.Data.OrderType);

            return NatsAck.Success;
        }
        catch (Exception ex)
        {
            // Record failure metrics
            _metrics.RecordOrderProcessed(msg.Data.OrderType, "failed");
            _logger.LogError(ex, "Failed to process order");
            return NatsAck.Fail;
        }
    }
}
```

### Important: Meter Name

**Critical**: Your custom metrics service must use the meter name `"AppMetrics"`. This is automatically configured in the SDK's OpenTelemetry setup. If you use a different meter name, you'll need to manually add it to the OpenTelemetry configuration.

> Do not create custom metrics for the sake of creating custom metrics. If its genuinely something that is not captured via NATS metrics, then only consider creating custom metric. For instance, above example is a bad choice to create custom metrics. We deliberately added that to show you what NOT to do. the orders processed could have been easily captured via existing NATS metrics through `nats_sub_msg_process_milliseconds_count` and applying filter on fn (function name) = `ProcessOrder`

## More on Using Custom Metrics in Your Services

### Dependency Injection

The SDK automatically registers your metrics service through dependency injection. Simply inject it into your services:

```csharp
public class MyService
{
    private readonly AppMetricsService _metrics;

    public MyService(AppMetricsService metrics)
    {
        _metrics = metrics;
    }
}
```

### Recording Metrics

Use the helper methods you defined in your metrics service, or record metrics directly:

```csharp
// Using helper methods (recommended)
_metrics.RecordOrderProcessed("premium", "success");

// Or record directly
var tags = new KeyValuePair<string, object?>[]
{
    new("region", "us-east"),
    new("tier", "premium")
};
_metrics.OrdersProcessed.Add(1, tags.AsSpan());
```

### Example: Background Service with Metrics

Here's a complete example of a background service using custom metrics:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace YourApp.Services.Background;

public class CleanupService : BackgroundService
{
    private readonly ILogger<CleanupService> _logger;
    private readonly IDB _db;
    private readonly AppMetricsService _metrics;

    public CleanupService(
        ILogger<CleanupService> logger,
        IDB db,
        AppMetricsService metrics)
    {
        _logger = logger;
        _db = db;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deletedCount = await CleanupOldRecordsAsync(stoppingToken);

                // Record cleanup metrics
                _metrics.RecordCleanupJobsCount(deletedCount);

                _logger.LogInformation("Cleaned up {count} old records", deletedCount);

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
                _metrics.RecordDatabaseError("cleanup", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task<int> CleanupOldRecordsAsync(CancellationToken ct)
    {
        // Your cleanup logic here
        return 0;
    }
}
```

## OpenTelemetry Integration

The SDK automatically configures OpenTelemetry to export metrics from both `NatsMetrics` and `AppMetrics` meters. This happens in the `App` class configuration.

### Configuration

Metrics are automatically exported if you configure the `OtelEndpoint` in base app settings. Use below environment variables to control the destination.

- `CCNPOTELENDPOINT`
  - OTEL collector gRPC endpoint for exporting telemetry. No default.
- `CCNPOTELHEADERS`
  - Additional OTEL headers required when sending telemetry. No default.

> You can use something like [alloy](https://grafana.com/docs/alloy/latest/) to provide the gRPC endpoint and then dump it into any metrics store of your choice. We recommend using VictoriaMetrics for metrics storage.

The SDK automatically:

1. Creates OpenTelemetry resource attributes (service name, version, cluster, etc.)
2. Adds both `NatsMetrics` and `AppMetrics` meters
3. Configures OTLP exporter if endpoint is provided
4. Adds runtime instrumentation (GC, thread pool, etc.)
5. Adds HTTP client and SQL client instrumentation

### Exporting to Observability Platforms

Your metrics are automatically exported to any OTLP-compatible platform:

- **Prometheus**: Use an OTLP-to-Prometheus bridge
- **Grafana Cloud**: Direct OTLP support
- **Azure Application Insights**: OTLP support
- **Datadog**: OTLP support
- **Custom collectors**: Any OTLP-compatible collector

## Metric Types

The SDK uses `System.Diagnostics.Metrics`, which supports several metric types:

### Counter

Use for metrics that only increase (e.g., total requests, total errors):

```csharp
public Counter<long> TotalRequests { get; }

// In constructor
TotalRequests = _meter.CreateCounter<long>(
    "requests_total",
    "count",
    "Total number of requests"
);

// Usage
TotalRequests.Add(1, tags.AsSpan());
```

### Histogram

Use for metrics that measure distributions (e.g., duration, size):

```csharp
public Histogram<double> RequestDuration { get; }

// In constructor
RequestDuration = _meter.CreateHistogram<double>(
    "request_duration",
    "ms",
    "Request processing duration in milliseconds"
);

// Usage
RequestDuration.Record(123.45, tags.AsSpan());
```

### Gauge

Use for metrics that can go up or down (e.g., current queue size, active connections):

```csharp
public ObservableGauge<long> ActiveConnections { get; }

// In constructor
ActiveConnections = _meter.CreateObservableGauge<long>(
    "active_connections",
    "count",
    "Current number of active connections",
    () => GetCurrentConnectionCount()
);
```

## Best Practices

### 1. Use Descriptive Metric Names

Follow naming conventions:

- Use snake_case: `orders_processed_total`
- Include units in the name or description: `request_duration_ms`
- Use `_total` suffix for counters: `errors_total`

### 2. Use Tags Wisely

Tags (labels) allow you to filter and aggregate metrics:

- Use tags for dimensions you'll query (region, status, type)
- Don't use high-cardinality values (user IDs, request IDs) as tags
- Keep tag values consistent across your application

### 3. Record Metrics at the Right Time

- Record success metrics after successful operations
- Record failure metrics in catch blocks
- Use `Stopwatch` for accurate duration measurements

### 4. Keep Metrics Focused

- Track business-critical metrics
- Avoid over-instrumentation
- Focus on metrics that help with debugging and monitoring

### 5. Use Helper Methods

Create helper methods in your metrics service for common patterns:

```csharp
public void RecordOperation(string operation, bool success, double durationMs)
{
    var tags = new KeyValuePair<string, object?>[]
    {
        new("operation", operation),
        new("status", success ? "success" : "failure")
    };

    OperationCount.Add(1, tags.AsSpan());
    OperationDuration.Record(durationMs, tags.AsSpan());
}
```

### 6. Meter Name Consistency

Always use `"AppMetrics"` as your meter name to ensure automatic OpenTelemetry integration. The SDK is pre-configured to export this meter.

## Summary

- ✅ **Automatic NATS metrics**: Tracked automatically for all message handlers
- ✅ **Custom metrics**: Create `AppMetricsService` with meter name `"AppMetrics"`
- ✅ **OpenTelemetry integration**: Automatically configured and exported
- ✅ **Dependency injection**: Metrics services automatically registered
- ✅ **Multiple metric types**: Counters, histograms, gauges supported

The SDK provides a complete metrics solution out of the box, while giving you full flexibility to add custom business metrics as needed.

---

[Back to documentation index](./README.md)
