# Controllers

Controllers are the entry points for handling NATS messages in cloops.microservices. They follow a pattern similar to REST frameworks like Spring and ASP.NET, where controllers handle transport-layer concerns (NATS messages) and delegate business logic to services.

## What are Controllers?

Controllers are classes that:

- Handle incoming NATS messages using `[NatsConsumer]` attributes
- Receive `NatsMsg<T>` wrappers containing message data
- Return `NatsAck` responses to acknowledge message processing
- Call services to perform business logic
- Are automatically discovered and registered by the framework

## Controller Registration

> **To register a class as a controller, it must belong to a namespace ending with `Controllers`. e.g. `Cljps.Controllers`**

The framework automatically:

1. Discovers all classes in namespaces ending with `Controllers`
2. Registers them as singletons with dependency injection
3. Scans for methods decorated with `[NatsConsumer]` attributes
4. Subscribes to the corresponding NATS subjects

### Registering Controllers by Interface

Controllers can be automatically registered with their interface in the dependency injection container. This allows you to inject the interface instead of the concrete implementation, which improves testability and follows dependency inversion principles.

**Convention:**

- The interface must be named `I{ControllerName}` (e.g., `IOrderController` for `OrderController`)
- The interface must be in the same namespace as the controller class
- If such an interface exists, the controller will be registered as `AddSingleton<Interface, ConcreteType>()`
- If no matching interface is found, the concrete type will be registered directly

**Example:**

```cs
namespace your.namespace.controllers;

public interface IOrderController
{
    Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct = default);
}

public class OrderController : IOrderController
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [NatsConsumer(_subject: "orders.process")]
    public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct = default)
    {
        var result = await _orderService.ProcessOrderAsync(msg.Data, ct);
        return new NatsAck(true, result);
    }
}
```

In this example, `OrderController` will be automatically registered as `AddSingleton<IOrderController, OrderController>()`. You can then inject `IOrderController` in your tests or other components:

```cs
public class OrderControllerTests
{
    private readonly IOrderController _controller;

    public OrderControllerTests(IOrderController controller)
    {
        _controller = controller;
    }

    // Test methods here
}
```

**Note:** If you don't want to use interface-based registration, simply omit the interface. The controller will still be registered, but as the concrete type only.

## Controller Structure

Here's a typical controller structure:

```cs
using CLOOPS.NATS.Attributes;
using CLOOPS.NATS.Messages;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using your.namespace.services;

namespace your.namespace.controllers;

public class OrderController
{
    private readonly ILogger<OrderController> _logger;
    private readonly OrderService _orderService;

    public OrderController(ILogger<OrderController> logger, OrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
    }

    [NatsConsumer(_subject: "orders.process")]
    public async Task<NatsAck> ProcessOrder(NatsMsg<Order> msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing order: {OrderId}", msg.Data.OrderId);

        // Call service with pure C# object (extract from NatsMsg wrapper)
        var result = await _orderService.ProcessOrderAsync(msg.Data, ct);

        // Return NatsAck with optional reply
        return new NatsAck(_isAck: true, _reply: result);
    }
}
```

## Separation of Concerns

The key principle is **separation of concerns**:

### Controllers (Transport Layer)

- Handle NATS message protocol (`NatsMsg<T>`, `NatsAck`)
- Extract data from message wrappers
- Call services with pure C# objects
- Return NATS acknowledgments

### Services (Business Logic Layer)

- Work with pure C# objects (no NATS wrappers)
- Contain business logic
- Can be easily unit tested
- Can be reused across different contexts

### Example: Controller + Service

**Controller** (handles NATS):

```cs
namespace your.namespace.controllers;

public class HealthController
{
    private readonly HealthService _healthService;

    public HealthController(HealthService healthService)
    {
        _healthService = healthService;
    }

    [NatsConsumer(_subject: "health.service")]
    public Task<NatsAck> GetHealth(NatsMsg<string> msg, CancellationToken ct = default)
    {
        // Extract data from NATS wrapper
        var input = msg.Data;

        // Call service with pure C# object
        var healthStatus = _healthService.GetHealthStatus();

        // Return NATS acknowledgment
        return Task.FromResult(new NatsAck(true, healthStatus));
    }
}
```

**Service** (business logic):

```cs
namespace your.namespace.services;

public class HealthService
{
    // Returns pure C# object - no NATS dependencies
    public HealthStatus GetHealthStatus()
    {
        return new HealthStatus
        {
            Status = "ok",
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
```

## Benefits of This Architecture

1. **Testability**: Services can be unit tested without NATS infrastructure
2. **Reusability**: Services can be used by multiple controllers or even non-NATS contexts
3. **Separation**: Clear boundary between transport layer and business logic
4. **Familiarity**: Aligns with patterns from REST frameworks (Spring, ASP.NET)
5. **Maintainability**: Changes to NATS protocol don't affect business logic

## Best Practices

1. **Keep controllers thin**: Controllers should primarily extract data from `NatsMsg<T>` and call services
2. **No business logic in controllers**: All business logic belongs in services
3. **Use dependency injection**: Inject services, loggers, and settings through constructor injection
4. **Handle errors appropriately**: Return `new NatsAck(false)` for transient errors that should be retried
5. **Log at controller level**: Log incoming messages and high-level flow in controllers
6. **Respect cancellation tokens**: Always pass `CancellationToken` to service calls

## Multiple Consumers in One Controller

You can have multiple `[NatsConsumer]` methods in a single controller:

```cs
namespace your.namespace.controllers;

public class OrderController
{
    private readonly OrderService _orderService;

    public OrderController(OrderService orderService)
    {
        _orderService = orderService;
    }

    [NatsConsumer("orders.create")]
    public async Task<NatsAck> CreateOrder(NatsMsg<CreateOrderRequest> msg, CancellationToken ct = default)
    {
        var result = await _orderService.CreateOrderAsync(msg.Data, ct);
        return new NatsAck(true, result);
    }

    [NatsConsumer("orders.cancel")]
    public async Task<NatsAck> CancelOrder(NatsMsg<CancelOrderRequest> msg, CancellationToken ct = default)
    {
        await _orderService.CancelOrderAsync(msg.Data, ct);
        return new NatsAck(true);
    }

    [NatsConsumer("orders.status", QueueGroupName = "pod-{POD_NAME}")]
    public async Task<NatsAck> HandleStatusChange(NatsMsg<OrderStatus> msg, CancellationToken ct = default)
    {
        await _orderService.HandleStatusChangeAsync(msg.Data, ct);
        return new NatsAck(true);
    }
}
```

## Related Documentation

- [Registering Your First NATS Consumer](./consumer.md) - Learn how to create your first controller
- [Services](./services.md) - Understand how services contain business logic
- [NatsConsumer Attribute Options](./consumer.md#natsconsumerattribute-options) - Configure queue groups, durable consumers, etc.
- [Back to documentation index](./README.md)
