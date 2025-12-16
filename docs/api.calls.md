# Making Third-Party API Calls

This guide explains how to make API calls to other services from your microservices. There are two main approaches depending on the type of service you're calling.

## Table of Contents

1. [When to Use What](#when-to-use-what)
2. [Making HTTP API Calls](#making-http-api-calls)
   - [Creating an HTTP Service](#creating-an-http-service)
   - [Basic HTTP Request Example](#basic-http-request-example)
   - [Handling Responses](#handling-responses)
   - [Error Handling](#error-handling)
3. [Making Calls to Cloops Microservices](#making-calls-to-cloops-microservices)
   - [Understanding Subject Builders](#understanding-subject-builders)
   - [Request-Reply Pattern (R_Subject)](#request-reply-pattern-r_subject)
   - [Publishing Events (P_Subject)](#publishing-events-p_subject)
   - [Stream Publishing (S_Subject)](#stream-publishing-s_subject)
4. [Best Practices](#best-practices)

## When to Use What

### Use HTTP Services When:

- ✅ Calling **external third-party APIs** (GitHub, Azure, Stripe, weather APIs, etc.)
- ✅ The service uses **REST/HTTP** protocol
- ✅ The service is **not** built using the cloops.microservices framework
- ✅ You need standard HTTP features (headers, query parameters, different HTTP methods)

### Use NATS Subject Builders When:

- ✅ Calling **other microservices** built with cloops.microservices
- ✅ You need **type-safe** communication with compile-time checking
- ✅ You want **automatic message validation**
- ✅ You need **request-reply**, **event publishing**, or **stream publishing** patterns
- ✅ You want to leverage NATS features (distributed messaging, JetStream, etc.)

### Quick Decision Tree

```
Is the service built with cloops.microservices?
├─ YES → Use NATS Subject Builders (R_Subject, P_Subject, S_Subject)
└─ NO → Use HTTP Service
```

## Making HTTP API Calls

HTTP services provide a managed `HttpClient` instance that prevents port exhaustion and follows best practices for HTTP client usage.

### Creating an HTTP Service

To create an HTTP service:

1. **Create a class in a namespace ending with `Services.Http`**
2. **Inject `HttpClient` in the constructor**
3. **Configure the client** (base URL, headers, etc.) in the constructor

**Example:**

```csharp
using System.Net.Http;

namespace myapp.services.http;

public class WeatherService
{
    private readonly HttpClient _httpClient;

    public WeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;

        // Configure the client
        _httpClient.BaseAddress = new Uri("https://api.weather.com/");
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", "your-api-key");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
}
```

### Basic HTTP Request Example

Here's a complete example of making an HTTP GET request:

```csharp
namespace myapp.services.http;

public class WeatherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.weather.com/v1/");
    }

    public async Task<WeatherData?> GetWeatherAsync(string city, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"weather?city={city}", ct);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var weatherData = Util.Deserialize<WeatherData>(json);
                return weatherData;
            }
            else
            {
                _logger.LogWarning("Weather API returned status {StatusCode}", response.StatusCode);
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get weather for city {City}", city);
            return null;
        }
    }
}

public class WeatherData
{
    public string City { get; set; } = "";
    public double Temperature { get; set; }
    public string Condition { get; set; } = "";
}
```

### Handling POST Requests

For POST requests with JSON payload:

```csharp
public async Task<bool> CreateUserAsync(User user, CancellationToken ct = default)
{
    try
    {
        var json = Util.Serialize(user);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("users", content, ct);

        return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to create user");
        return false;
    }
}
```

### Handling Responses

Always check the response status and handle different scenarios:

```csharp
public async Task<ApiResult<T>> GetDataAsync<T>(string endpoint, CancellationToken ct = default)
{
    try
    {
        var response = await _httpClient.GetAsync(endpoint, ct);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            var data = Util.Deserialize<T>(json);
            return new ApiResult<T> { Success = true, Data = data };
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new ApiResult<T> { Success = false, Error = "Resource not found" };
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            return new ApiResult<T>
            {
                Success = false,
                Error = $"API returned {response.StatusCode}: {errorContent}"
            };
        }
    }
    catch (TaskCanceledException) when (ct.IsCancellationRequested)
    {
        return new ApiResult<T> { Success = false, Error = "Request was cancelled" };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "HTTP request failed");
        return new ApiResult<T> { Success = false, Error = ex.Message };
    }
}

public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
}
```

### Error Handling

Always wrap HTTP calls in try-catch blocks to handle:

- Network failures (DNS resolution, connection timeouts)
- HTTP errors (4xx, 5xx status codes)
- Cancellation requests

```csharp
public async Task<string?> FetchDataAsync(string url, CancellationToken ct = default)
{
    try
    {
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode(); // Throws if status code is not success
        return await response.Content.ReadAsStringAsync(ct);
    }
    catch (HttpRequestException ex)
    {
        // Handle HTTP-specific errors
        _logger.LogError(ex, "HTTP error when calling {Url}", url);
        return null;
    }
    catch (TaskCanceledException) when (ct.IsCancellationRequested)
    {
        // Handle cancellation
        _logger.LogInformation("Request to {Url} was cancelled", url);
        return null;
    }
    catch (Exception ex)
    {
        // Handle other errors (network failures, etc.)
        _logger.LogError(ex, "Unexpected error when calling {Url}", url);
        return null;
    }
}
```

## Making Calls to Cloops Microservices

When calling other microservices built with cloops.microservices, use **Subject Builders** and **strongly typed messages**. This provides compile-time type safety and automatic validation.

### Understanding Subject Builders

Subject builders provide type-safe access to NATS subjects. They ensure:

- ✅ **Type safety**: You can only publish the correct message type to each subject
- ✅ **Compile-time checking**: Errors are caught before runtime
- ✅ **IntelliSense support**: Autocomplete for subjects and messages
- ✅ **Automatic validation**: Messages are validated before sending

### Request-Reply Pattern (R_Subject)

Use `R_Subject` when you need to send a request and wait for a response (like a synchronous API call).

**Example: Getting jobs from CLJPS service**

```csharp
using CLOOPS.NATS;
using CLOOPS.NATS.Messages.CLJPS;
using CLOOPS.NATS.Extensions;

namespace myapp.services;

public class JobQueryService
{
    private readonly ICloopsNatsClient _client;
    private readonly ILogger<JobQueryService> _logger;

    public JobQueryService(ICloopsNatsClient client, ILogger<JobQueryService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<Job>?> GetJobsAsync(JobStatus status, CancellationToken ct = default)
    {
        try
        {
            // 1. Create a request message
            var request = new GetJobsRequest
            {
                jobStatus = status,
                limit = 50,
                offset = 0
            };

            // 2. Get the subject using subject builder
            // Assuming there's a subject builder method: R_GetJobs()
            var subject = _client.Subjects().CLJPS().R_GetJobs();

            // 3. Send request and wait for response
            var response = await subject.Request(request, ct);

            // 4. Handle the response
            if (response?.Data != null)
            {
                return response.Data;
            }

            return null;
        }
        catch (ValidationException ex)
        {
            // Message validation failed before sending
            _logger.LogError(ex, "Request validation failed");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get jobs");
            return null;
        }
    }
}
```

**Key Points:**

- `R_Subject<RequestType, ResponseType>` is strongly typed
- The `Request()` method validates the message before sending
- You get a typed response back
- If validation fails, a `ValidationException` is thrown before the request is sent

### Publishing Events (P_Subject)

Use `P_Subject` for fire-and-forget event publishing (Core NATS).

**Example: Publishing a person save event**

```csharp
using CLOOPS.NATS;
using CLOOPS.NATS.Extensions;

namespace myapp.services;

public class PersonService
{
    private readonly ICloopsNatsClient _client;

    public PersonService(ICloopsNatsClient client)
    {
        _client = client;
    }

    public async Task PublishPersonSavedAsync(Person person, CancellationToken ct = default)
    {
        try
        {
            // 1. Create the message
            var personMessage = new Person
            {
                Id = person.Id,
                Name = person.Name,
                Age = person.Age,
                Addr = person.Addr
            };

            // 2. Get the subject using subject builder
            var subject = _client.Subjects().Example().P_SavePerson(person.Id);

            // 3. Publish (fire-and-forget)
            await subject.Publish(personMessage, ct);

            // Message is validated automatically before publishing
        }
        catch (ValidationException ex)
        {
            // Validation failed - message was not published
            throw new InvalidOperationException("Person data is invalid", ex);
        }
    }
}
```

**Key Points:**

- `P_Subject<T>` is for Core NATS publishing
- `Publish()` validates the message before sending
- This is fire-and-forget (no response expected)

### Stream Publishing (S_Subject)

Use `S_Subject` for JetStream publishing (durable, persistent events).

**Example: Publishing a job update event to JetStream**

```csharp
using CLOOPS.NATS;
using CLOOPS.NATS.Extensions;

namespace myapp.services;

public class JobService
{
    private readonly ICloopsNatsClient _client;

    public JobService(ICloopsNatsClient client)
    {
        _client = client;
    }

    public async Task ScheduleJobAsync(Job job, CancellationToken ct = default)
    {
        try
        {
            // 1. Create the job message
            var jobMessage = new Job
            {
                Id = job.Id,
                JobUrl = job.JobUrl,
                JobHttpMethod = job.JobHttpMethod,
                ExpectedExecutionAt = job.ExpectedExecutionAt
            };

            // 2. Get the subject using subject builder
            var subject = _client.Subjects().CLJPS().S_ScheduleJob(job.Id);

            // 3. Publish to JetStream (durable)
            // Parameters: message, waitForAck, messageId
            await subject.StreamPublish(jobMessage, waitForAck: true, messageId: job.Id, ct: ct);

            // Message is validated and persisted in JetStream
        }
        catch (ValidationException ex)
        {
            // Validation failed - message was not published
            throw new InvalidOperationException("Job data is invalid", ex);
        }
    }
}
```

**Key Points:**

- `S_Subject<T>` is for JetStream publishing
- `StreamPublish()` creates durable, persistent events
- Use `waitForAck: true` to ensure the message is persisted
- Messages are validated before publishing

### Complete Example: Service Using Both Patterns

Here's a service that uses both HTTP calls and NATS subject builders:

```csharp
using CLOOPS.NATS;
using CLOOPS.NATS.Extensions;
using CLOOPS.NATS.Messages.CLJPS;

namespace myapp.services;

public class OrderProcessingService
{
    private readonly ICloopsNatsClient _client;
    private readonly PaymentService _paymentService; // HTTP service
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        ICloopsNatsClient client,
        PaymentService paymentService,
        ILogger<OrderProcessingService> logger)
    {
        _client = client;
        _paymentService = paymentService;
        _logger = logger;
    }

    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            // 1. Call external payment API (HTTP)
            var paymentResult = await _paymentService.ProcessPaymentAsync(
                order.PaymentDetails, ct);

            if (!paymentResult.Success)
            {
                _logger.LogWarning("Payment failed for order {OrderId}", order.Id);
                return false;
            }

            // 2. Publish order created event to other microservices (NATS)
            var orderEvent = new OrderCreatedEvent
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Amount = order.Amount,
                Timestamp = DateTimeOffset.UtcNow
            };

            var subject = _client.Subjects().Orders().S_OrderCreated(order.Id);
            await subject.StreamPublish(orderEvent, waitForAck: true, messageId: order.Id, ct: ct);

            // 3. Request inventory update from inventory service (NATS)
            var inventoryRequest = new UpdateInventoryRequest
            {
                OrderId = order.Id,
                Items = order.Items
            };

            var inventorySubject = _client.Subjects().Inventory().R_UpdateInventory();
            var inventoryResponse = await inventorySubject.Request(inventoryRequest, ct);

            if (inventoryResponse?.Data?.Success == true)
            {
                _logger.LogInformation("Order {OrderId} processed successfully", order.Id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
            return false;
        }
    }
}
```

## Best Practices

### HTTP Services

1. **One HTTP service per external API service**: Create separate services for different third-party API applications (e.g., `GitHubService`, `AzureService`, `StripeService`)

2. **Configure HttpClient in constructor**: Set base URLs, default headers, and timeouts in the constructor

3. **Always handle errors**: Wrap HTTP calls in try-catch blocks to handle network failures

4. **Use cancellation tokens**: Always pass `CancellationToken` to async HTTP methods

5. **Don't create HttpClient manually**: Always use dependency injection to get `HttpClient` instances

### NATS Subject Builders

1. **Always use subject builders**: Never construct subjects manually; use builders for type safety

2. **Trust the validation**: Messages are validated automatically, so handlers can assume valid data

3. **Handle ValidationException**: Catch validation exceptions when publishing/requesting

4. **Choose the right subject type**:

   - `R_Subject`: When you need a response (request-reply)
   - `P_Subject`: For fire-and-forget events (Core NATS)
   - `S_Subject`: For durable, persistent events (JetStream)

5. **Use meaningful message IDs**: When using `StreamPublish`, provide meaningful message IDs for deduplication

6. **Wait for acknowledgments**: Use `waitForAck: true` for critical events that must be persisted

### General

1. **Log important operations**: Log successes and failures for debugging

2. **Use structured logging**: Include relevant context (IDs, status codes, etc.) in log messages

3. **Handle cancellation**: Always respect `CancellationToken` and handle `OperationCanceledException`

4. **Validate inputs**: Even though NATS messages are validated automatically, validate inputs for HTTP calls

## Summary

- **HTTP Services**: Use for external third-party APIs (GitHub, Azure, payment gateways, etc.)
- **NATS Subject Builders**: Use for calling other cloops.microservices with type safety and validation
- **Subject Types**:
  - `R_Subject`: Request-reply (synchronous calls)
  - `P_Subject`: Fire-and-forget events (Core NATS)
  - `S_Subject`: Durable events (JetStream)
- **Always handle errors**: Both HTTP and NATS calls can fail
- **Use cancellation tokens**: Respect cancellation requests in async operations

By following these patterns, you'll build reliable, type-safe communication between your microservices and external APIs.

---

[Back to documentation index](./README.md)
