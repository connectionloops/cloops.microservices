using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

/// <summary>
/// Base class for HTTP services that are used from singleton controllers, NATS consumers, or background services.
/// Make sure to call CreateClient() and get a fresh client whenever you are making a http request. 
/// More details: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0#basic-usage
/// </summary>
public abstract class BaseHttpService
{
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// Creates a base HTTP service.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create logical HTTP clients per operation.</param>
    /// <param name="logger">Optional logger for derived services.</param>
    protected BaseHttpService(IHttpClientFactory httpClientFactory, ILogger? logger = null)
    {
        this.httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    /// <summary>
    /// Optional logger available to derived services.
    /// </summary>
    protected ILogger? Logger { get; }

    /// <summary>
    /// The named HTTP client to create. Defaults to the concrete service type name.
    /// </summary>
    protected virtual string ClientName => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Creates a fresh logical HTTP client for one outbound operation.
    /// </summary>
    protected virtual HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(ClientName);
        ConfigureClient(client);
        return client;
    }

    /// <summary>
    /// Applies per-client customization such as base address, timeout, or default request headers.
    /// </summary>
    /// <param name="client">The client created for the current operation.</param>
    protected virtual void ConfigureClient(HttpClient client)
    {
    }
}
