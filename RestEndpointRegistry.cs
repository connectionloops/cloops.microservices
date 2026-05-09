using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace CLOOPS.microservices;

internal sealed record RestEndpointDefinition(
    Type EndpointType,
    MethodInfo Method,
    string HttpMethod,
    string Path,
    RestAuth Auth,
    Func<IServiceProvider, HttpContext, ValueTask<object?>> InvokeAsync
);

internal sealed class RestEndpointRegistry
{
    public RestEndpointRegistry(IReadOnlyCollection<RestEndpointDefinition> endpoints)
    {
        Endpoints = endpoints;
    }

    public IReadOnlyCollection<RestEndpointDefinition> Endpoints { get; }
}
