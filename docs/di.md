# Automated Dependency Injection (DI) Setup

Any app created with `cloops.microservices` creates an ApplicationHost which acts as the DI container. All controllers, services, and background services are registered in it and can be accessed by any other component. The DI setup is automatic in the sense that you just have to keep your classes in correct namespaces and the framework will auto register them.

## Namespace Rules

cloops.microservices require a very strict namespace structure to auto register your classes into DI container properly

let's assume your main namespace is `cloops.app1`

- All controllers (NATS message handlers) should be in namespace `cloops.app1.controllers`
- All traditional services should be in namespace `cloops.app1.services`
- All http services should be in namespace `cloops.app1.services.http`
- All background services should be in namespace `cloops.app1.services.background`

If you need further organization you need to add it before the namespace suffix part. e.g. `cloops.app1.BnR.services` or `cloops.app1.orders.controllers`. `cloops.microservices` matches against "ends with".

## Project Structure

This is not mandatory. But, we recommend below file structure to cleanly organize your code.

## File System Organization

```
- Root
- src
  - Controllers
    - *.cs                          All controllers (NATS message handlers with [NatsConsumer] attributes)
  - Services
    - http
        - *.cs                      All services with managed http client for outbound 3P API calls.
    - background
        - *.cs                      All background services (services that do something continuously, e.g. on a schedule)
    - *.cs                          All traditional services (business logic, return pure C# objects)
  - Util
    - Util.cs                       Utility functions
  - Program.cs                      Startup and app setup
```

## Setting up the application (Program.cs)

Once you have things defined in this way, all you have to do is,

```cs
// filename: Program.cs

using CLOOPS.microservices;

var app = new App();
await app.RunAsync();

```

This sets up the application, registers all controllers and services, connects to NATS and starts your consumers and background services and everything else you have in your app.

## Accessing a service

In standard DI way.

### Constructor Injection (Recommended)

The most common way to access services is through constructor injection. Simply add the service as a constructor parameter:

```cs
// filename: services/MyService.cs

using Microsoft.Extensions.Logging;

namespace cloops.app1.services;

public class MyService
{
    private readonly ILogger<MyService> _logger;
    private readonly AnotherService _anotherService;

    public MyService(ILogger<MyService> logger, AnotherService anotherService)
    {
        _logger = logger;
        _anotherService = anotherService;
    }

    public void DoWork()
    {
        _logger.LogInformation("Doing work...");
        _anotherService.Help();
    }
}
```

### Manual Resolution via IServiceProvider

If you need to resolve services manually (e.g., in a background service that needs to resolve services at runtime), you can inject `IServiceProvider`:

```cs
// filename: services/MyService.cs

using Microsoft.Extensions.Logging;

namespace cloops.app1.services;

public class MyService
{
    private readonly IServiceProvider _serviceProvider;

    public MyService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void DoWork()
    {
        // Resolve a service manually
        var anotherService = _serviceProvider.GetRequiredService<AnotherService>();
        anotherService.Help();
    }
}
```
