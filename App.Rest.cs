using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CLOOPS.NATS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace CLOOPS.microservices;

public partial class App
{
    private static readonly IResult RestHealthOkResult = Results.Ok(new { status = "ok" });
    private static readonly IResult RestReadyOkResult = Results.Ok(new { ready = true });
    private static readonly IResult RestReadyUnavailableResult = Results.Json(new { ready = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private byte[]? restApiSecretBytes;

    private void RegisterRestEndpoints()
    {
        var restTypes = GetTargetTypes()
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Rest", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        var endpointDefinitions = new List<RestEndpointDefinition>();

        foreach (var restType in restTypes)
        {
            builder.Services.AddSingleton(restType);

            var interfaceType = FindInterface(restType);
            if (interfaceType != null)
            {
                builder.Services.AddSingleton(interfaceType, sp => sp.GetRequiredService(restType));
                Console.WriteLine($"Registered REST endpoint class: {interfaceType.Name} -> {restType.Name}");
            }
            else
            {
                Console.WriteLine($"Registered REST endpoint class: {restType.Name}");
            }

            var endpointMethods = restType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(method => method
                    .GetCustomAttributes<RestEndpointAttribute>(inherit: false)
                    .Select(attribute => CreateRestEndpointDefinition(restType, method, attribute)));

            endpointDefinitions.AddRange(endpointMethods);
        }

        ValidateRestEndpointMappings(endpointDefinitions);
        builder.Services.AddSingleton(new RestEndpointRegistry(endpointDefinitions));
    }

    private void ConfigureRestListener()
    {
        if (!appSettings.EnableRestEndpoints)
        {
            return;
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(appSettings.RestPort);
        });
    }

    private void MapRestEndpoints(WebApplication app)
    {
        if (!appSettings.EnableRestEndpoints)
        {
            app.Logger.LogInformation("REST endpoints are disabled");
            return;
        }

        var registry = app.Services.GetRequiredService<RestEndpointRegistry>();
        ValidateProtectedRestEndpointsHaveSecret(registry);

        app.MapGet("/healthz", () => RestHealthOkResult);
        app.MapGet("/readyz", () =>
        {
            var client = app.Services.GetService<ICloopsNatsClient>();
            var ready = client?.Connection.ConnectionState == NatsConnectionState.Open;
            return ready ? RestReadyOkResult : RestReadyUnavailableResult;
        });

        foreach (var endpoint in registry.Endpoints)
        {
            app.MapMethods(endpoint.Path, [endpoint.HttpMethod], async context =>
            {
                if (endpoint.Auth == RestAuth.Required && !IsRestRequestAuthorized(context))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var value = await endpoint.InvokeAsync(context.RequestServices, context).ConfigureAwait(false);
                await WriteRestEndpointResultAsync(context, value).ConfigureAwait(false);
            });

            app.Logger.LogInformation("Mapped REST endpoint: {Method} {Path} -> {Endpoint}.{Handler}",
                endpoint.HttpMethod,
                endpoint.Path,
                endpoint.EndpointType.Name,
                endpoint.Method.Name);
        }

        app.Logger.LogInformation("REST endpoints listening on port {RestPort}", appSettings.RestPort);
    }

    private static void ValidateRestEndpointMappings(IEnumerable<RestEndpointDefinition> endpointDefinitions)
    {
        var endpoints = endpointDefinitions.ToArray();
        var reservedEndpoints = new[] { "GET /healthz", "GET /readyz" };
        var reservedDuplicates = endpoints
            .Select(e => $"{e.HttpMethod} {e.Path}")
            .Intersect(reservedEndpoints, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (reservedDuplicates.Length > 0)
        {
            throw new InvalidOperationException($"REST endpoint mappings are reserved by the SDK: {string.Join(", ", reservedDuplicates)}");
        }

        var duplicates = endpoints
            .GroupBy(e => $"{e.HttpMethod} {e.Path}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate REST endpoint mappings found: {string.Join(", ", duplicates)}");
        }
    }

    private void ValidateProtectedRestEndpointsHaveSecret(RestEndpointRegistry registry)
    {
        if (!registry.Endpoints.Any(e => e.Auth == RestAuth.Required))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(appSettings.RestApiSecret))
        {
            throw new InvalidOperationException("REST endpoint authentication is required by one or more endpoints, but REST_API_SECRET is not configured.");
        }

        restApiSecretBytes = Encoding.UTF8.GetBytes(appSettings.RestApiSecret);
    }

    private static RestEndpointDefinition CreateRestEndpointDefinition(Type endpointType, MethodInfo method, RestEndpointAttribute attribute)
    {
        var httpMethod = attribute.Method.ToString().ToUpperInvariant();
        var path = attribute.Path.Trim();

        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"REST endpoint path must start with '/' on {endpointType.FullName}.{method.Name}.");
        }

        ValidateRestEndpointMethodSignature(endpointType, method);

        return new RestEndpointDefinition(endpointType, method, httpMethod, path, attribute.Auth, CreateRestEndpointInvoker(endpointType, method));
    }

    private static void ValidateRestEndpointMethodSignature(Type endpointType, MethodInfo method)
    {
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType != typeof(HttpContext) &&
                parameter.ParameterType != typeof(CancellationToken))
            {
                throw new InvalidOperationException(
                    $"Unsupported REST endpoint parameter '{parameter.Name}' of type '{parameter.ParameterType.Name}' on {endpointType.FullName}.{method.Name}. Supported parameters are HttpContext and CancellationToken.");
            }
        }

        var returnType = method.ReturnType;
        if (returnType == typeof(void) ||
            returnType == typeof(Task) ||
            typeof(IResult).IsAssignableFrom(returnType) ||
            returnType == typeof(string) ||
            returnType == typeof(object))
        {
            return;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported REST endpoint return type '{returnType.Name}' on {endpointType.FullName}.{method.Name}.");
    }

    private static Func<IServiceProvider, HttpContext, ValueTask<object?>> CreateRestEndpointInvoker(Type endpointType, MethodInfo method)
    {
        var services = Expression.Parameter(typeof(IServiceProvider), "services");
        var context = Expression.Parameter(typeof(HttpContext), "context");
        var getRequiredService = typeof(ServiceProviderServiceExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == nameof(ServiceProviderServiceExtensions.GetRequiredService) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1)
            .MakeGenericMethod(endpointType);

        var target = Expression.Call(getRequiredService, services);
        var arguments = method.GetParameters()
            .Select(parameter =>
            {
                if (parameter.ParameterType == typeof(HttpContext))
                {
                    return context;
                }

                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    return (Expression)Expression.Property(context, nameof(HttpContext.RequestAborted));
                }

                throw new InvalidOperationException($"Unsupported REST endpoint parameter type '{parameter.ParameterType.Name}'.");
            })
            .ToArray();

        var call = Expression.Call(target, method, arguments);
        var body = CreateRestEndpointInvokerBody(call, method.ReturnType);
        return Expression.Lambda<Func<IServiceProvider, HttpContext, ValueTask<object?>>>(body, services, context).Compile();
    }

    private static Expression CreateRestEndpointInvokerBody(MethodCallExpression call, Type returnType)
    {
        if (returnType == typeof(void))
        {
            return Expression.Block(
                call,
                Expression.Call(typeof(App).GetMethod(nameof(CompletedRestEndpointResult), BindingFlags.NonPublic | BindingFlags.Static)!));
        }

        if (returnType == typeof(Task))
        {
            return Expression.Call(
                typeof(App).GetMethod(nameof(AwaitRestEndpointTask), BindingFlags.NonPublic | BindingFlags.Static)!,
                call);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return Expression.Call(
                typeof(App).GetMethod(nameof(AwaitRestEndpointTaskOfT), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(returnType.GetGenericArguments()[0]),
                call);
        }

        return Expression.Call(
            typeof(App).GetMethod(nameof(BoxRestEndpointResult), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(returnType),
            call);
    }

    private static ValueTask<object?> CompletedRestEndpointResult()
    {
        return ValueTask.FromResult<object?>(null);
    }

    private static async ValueTask<object?> AwaitRestEndpointTask(Task task)
    {
        await task.ConfigureAwait(false);
        return null;
    }

    private static async ValueTask<object?> AwaitRestEndpointTaskOfT<T>(Task<T> task)
    {
        return await task.ConfigureAwait(false);
    }

    private static ValueTask<object?> BoxRestEndpointResult<T>(T value)
    {
        return ValueTask.FromResult<object?>(value);
    }

    private static async Task WriteRestEndpointResultAsync(HttpContext context, object? value)
    {
        switch (value)
        {
            case null:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            case IResult result:
                await result.ExecuteAsync(context).ConfigureAwait(false);
                return;
            case string text:
                await Results.Text(text).ExecuteAsync(context).ConfigureAwait(false);
                return;
            default:
                await Results.Json(value).ExecuteAsync(context).ConfigureAwait(false);
                return;
        }
    }

    private bool IsRestRequestAuthorized(HttpContext context)
    {
        var expectedSecretBytes = restApiSecretBytes;
        if (expectedSecretBytes == null)
        {
            return false;
        }

        var providedSecret = GetProvidedRestSecret(context);
        return !string.IsNullOrEmpty(providedSecret) && FixedTimeEquals(providedSecret, expectedSecretBytes);
    }

    private static string? GetProvidedRestSecret(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        return context.Request.Headers["X-CLOOPS-REST-SECRET"].FirstOrDefault();
    }

    private static bool FixedTimeEquals(string providedSecret, byte[] expectedSecretBytes)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        return providedBytes.Length == expectedSecretBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedSecretBytes);
    }
}
