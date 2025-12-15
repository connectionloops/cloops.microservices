# Registering Your First NATS Consumer

## What is cloops.microservices?

At its core, `cloops.microservices` is a framework that helps you build microservices as a set of NATS subscriptions that respond to incoming messages on NATS subjects. Instead of manually managing NATS connections, subscriptions, and message handling, `cloops.microservices` helps you define these subscriptions cleanly and reduces undifferentiated work.

The framework automatically:

- Discovers and registers your consumer methods
- Manages NATS connections and lifecycle
- Handles message deserialization
- Provides dependency injection for your services
- Manages queue groups for load balancing or broadcasting
- Handles acknowledgments and error responses

You simply define methods with the `[NatsConsumer]` attribute, and the framework handles the rest.

## Your First NATS Consumer

Let's create a simple health check consumer that responds to health check requests. This example is based on the `HealthService` from the microservice template.

### Step 1: Create a Service Class

First, create a service class in a namespace ending with `Services`. This is required for the framework to automatically discover and register your service.

```cs
using CLOOPS.NATS.Attributes;
using CLOOPS.NATS.Messages;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace your.namespace.services;

public class HealthService
{
    private readonly ILogger<HealthService> _logger;
    private readonly AppSettings _appSettings;

    public HealthService(ILogger<HealthService> logger, AppSettings appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    [NatsConsumer(_subject: "health.your.service")]
    public async Task<NatsAck> GetHealth(NatsMsg<string> msg, CancellationToken ct = default)
    {
        _logger.LogDebug("Health check requested");

        var reply = new HealthReply
        {
            Status = new()
            {
                ["appName"] = _appSettings.AssemblyName,
                ["appStatus"] = "ok",
                ["responder"] = $"{_appSettings.AssemblyName}:{Environment.MachineName}"
            }
        };

        return new NatsAck(_isAck: true, _reply: reply);
    }
}
```

### Step 2: Understanding the Consumer Method Signature

Your consumer method must follow this pattern:

1. **Return Type**: Must return `Task<NatsAck>` or `NatsAck`
2. **Parameters**:
   - First parameter: `NatsMsg<T>` where `T` is the type of your message payload
   - Second parameter (optional): `CancellationToken ct = default`
3. **Attribute**: The method must be decorated with `[NatsConsumer]` attribute

### Step 3: The NatsAck Response

The `NatsAck` class is used to acknowledge message processing:

- `_isAck: true` - Acknowledges the message (success)
- `_isAck: false` - Negatively acknowledges the message (failure, will be redelivered)
- `_reply: replyObject` - Optional reply message to send back to the requester

### Step 4: Run Your Service

Once you've created your service, the framework will automatically:

1. Discover the service class (because it's in a namespace ending with `Services`)
2. Register it with dependency injection
3. Discover the `[NatsConsumer]` method
4. Subscribe to the NATS subject `health.your.service`
5. Process incoming messages automatically

That's it! Your consumer is now ready to receive and process messages.

## NatsConsumerAttribute Options

The `[NatsConsumer]` attribute provides several options to configure your consumer:

### Basic Parameters

```cs
[NatsConsumer(
    _subject: "your.subject.here",
    _consumerId: null,              // Optional: For JetStream durable consumers
    _QueueGroupName: ""             // Optional: Queue group name for load balancing
)]
```

### Parameter Details

#### `_subject` (Required)

The NATS subject you want to listen to. This is the channel where messages will be published.

- **Wildcards are supported**: You can use `*` (single token) and `>` (multi-token) wildcards
- **Examples**:
  - `"orders.process"` - Exact match
  - `"orders.*"` - Matches `orders.new`, `orders.cancel`, etc.
  - `"orders.>"` - Matches `orders.new`, `orders.cancel.refund`, etc.

> 📖 **Learn more**: Read [NATS wildcards documentation](https://docs.nats.io/nats-concepts/subjects#wildcards) for details.

#### `_consumerId` (Optional)

Durable Consumer ID for JetStream consumers. When specified:

- The consumer becomes **durable** (persists across restarts)
- The sender and recipient can be temporally decoupled.
- Messages are persisted and can be replayed
- The consumer ID must exist in your JetStream configuration

> ⚠️ **Note**: If you don't specify `_consumerId`, the consumer is a **core NATS subscription** (non-durable).

#### `_QueueGroupName` (Optional)

NATS Queue Group Name for load balancing. This parameter:

- Only valid for **core subscriptions** (non-durable consumers)
- For durable subscriptions, queue groups are controlled through the `ConsumerId`
- If omitted, an empty string is used (which still enables load balancing)

### Queue Group Name Placeholders

The `QueueGroupName` supports runtime placeholders that are resolved when the subscription is created. This is particularly useful for creating unique queue groups per instance to enable broadcasting.

#### Supported Placeholders

- **`{POD_NAME}`** - Resolves to `POD_NAME` environment variable, or falls back to `HOSTNAME`, or machine name
- **`{HOSTNAME}`** - Resolves to `HOSTNAME` environment variable, or falls back to machine name
- **`{MACHINE_NAME}`** - Resolves to machine name (via `Dns.GetHostName()`)
- **`{ENV:VAR_NAME}`** - Resolves to any environment variable (e.g., `{ENV:MY_CUSTOM_VAR}`)

#### Example Usage

```cs
// Each pod gets a unique queue group based on POD_NAME
[NatsConsumer("events.broadcast", QueueGroupName = "pod-{POD_NAME}")]

// Use a custom environment variable
[NatsConsumer("events.broadcast", QueueGroupName = "instance-{ENV:INSTANCE_ID}")]

// Use machine name
[NatsConsumer("events.broadcast", QueueGroupName = "machine-{MACHINE_NAME}")]
```

## Load Balancing vs Broadcasting

NATS queue groups determine how messages are distributed across multiple instances of your service. You can choose between two patterns:

### Load Balancing Pattern

**Use case**: Distribute work across multiple instances so each message is processed by only one instance.

**How it works**: All instances use the **same queue group name**. NATS automatically distributes messages across instances in the queue group.

**Example**:

```cs
[NatsConsumer("orders.process", QueueGroupName = "order-workers")]
public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct = default)
{
    _logger.LogInformation("Processing order: {OrderId}", msg.Data.OrderId);
    // Process the order...
    return new NatsAck(true);
}
```

**Behavior**:

- If you have 3 instances running, messages will be distributed across all 3
- Each message is delivered to exactly one instance
- If one instance is busy, messages go to other available instances
- Perfect for scaling work processing

### Broadcasting Pattern

**Use case**: Every instance needs to receive and process every message (e.g., cache invalidation, configuration updates).

**How it works**: Each instance uses a **unique queue group name** (using placeholders). Since each instance has a different queue group, they all receive all messages.

**Example**:

```cs
[NatsConsumer("cache.invalidate", QueueGroupName = "pod-{POD_NAME}")]
public async Task<NatsAck> InvalidateCache(NatsMsg<CacheKey> msg, CancellationToken ct = default)
{
    _logger.LogInformation("Invalidating cache for key: {Key}", msg.Data.Key);
    // Invalidate local cache...
    return new NatsAck(true);
}
```

**Behavior**:

- If you have 3 instances (pod-1, pod-2, pod-3), each gets its own queue group
- All 3 instances receive every message
- Perfect for scenarios where all instances need the same information

### Comparison Table

| Pattern            | Queue Group Name       | Use Case                        | Example            |
| ------------------ | ---------------------- | ------------------------------- | ------------------ |
| **Load Balancing** | Same for all instances | Distribute work                 | `"workers"`        |
| **Broadcasting**   | Unique per instance    | All instances need all messages | `"pod-{POD_NAME}"` |

### No Queue Group (Empty String)

If you omit `QueueGroupName` or use an empty string:

```cs
[NatsConsumer("orders.process")]  // QueueGroupName defaults to empty string
```

**Behavior**:

- Still enables load balancing (all consumers belong to queue group "")
- Messages are distributed across instances
- Similar to specifying a fixed queue group name

> 📝 **Note**: For JetStream (durable) subscriptions, queue groups are always load-balanced. Broadcasting is not supported for JetStream consumers.

## Complete Example

Here's a complete example showing both patterns in a single service:

```cs
using CLOOPS.NATS.Attributes;
using CLOOPS.NATS.Messages;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace your.namespace.services;

public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    // Load balancing: Distribute orders across workers
    [NatsConsumer("orders.process", QueueGroupName = "order-workers")]
    public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing order: {OrderId}", msg.Data.OrderId);
        // Process order...
        await Task.Delay(100, ct); // Simulate work
        return new NatsAck(true);
    }

    // Broadcasting: All instances need to know about order status changes
    [NatsConsumer("orders.status.changed", QueueGroupName = "pod-{POD_NAME}")]
    public async Task<NatsAck> HandleStatusChange(NatsMsg<OrderStatus> msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Order status changed: {OrderId} -> {Status}",
            msg.Data.OrderId, msg.Data.Status);
        // Update local cache, notify clients, etc.
        return new NatsAck(true);
    }

    // Health check (no queue group needed)
    [NatsConsumer("health.orders")]
    public Task<NatsAck> GetHealth(NatsMsg<string> msg, CancellationToken ct = default)
    {
        return Task.FromResult(new NatsAck(true));
    }
}
```

## Best Practices

1. **Use descriptive subject names**: Follow a hierarchical naming pattern (e.g., `service.action.entity`)
2. **Handle multi tenancy**: e.g. `service.{tenantId}.action.entity.{entityId}` . This will also help in designing good authN and authZ strategies using decentralized auth.
3. **Choose the right pattern**: Use load balancing for work distribution, broadcasting for cache/state synchronization
4. **Handle errors gracefully**: Return `new NatsAck(false)` for transient errors that should be retried
5. **Use dependency injection**: Inject services, loggers, and settings through constructor injection
6. **Log appropriately**: Use structured logging to track message processing
7. **Respect cancellation tokens**: Use the `CancellationToken` parameter for graceful shutdown

## Next Steps

- Learn about [Services](./services.md) and how they're organized
- Explore [Utility Functions](./util.md) available in the framework
- Check out [Database Operations](./db.md) for data persistence
- Review [Making Third Party API Calls](./installation.md) for external integrations
