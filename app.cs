using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using CLOOPS.NATS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;

namespace CLOOPS.microservices;

/// <summary>
/// Coordinates dependency injection setup and application startup.
/// </summary>
public partial class App
{
    /// <summary>
    /// The application settings
    /// </summary>
    public BaseAppSettings appSettings;
    /// <summary>
    /// The host application builder
    /// </summary>
    public WebApplicationBuilder builder;

    /// <summary>
    /// The host application
    /// </summary>
    public WebApplication? host;

    /// <summary>
    /// Stores all the types in the assembly for faster startup.
    /// </summary>
    private Type[]? cachedTargetTypes;

    /// <summary>
    /// Creates the DI pipeline and starts the application.
    /// </summary>
    /// <param name="introMessageProvider">Optional function that takes BaseAppSettings and WebApplicationBuilder and returns a custom intro message. If not provided, a default message will be used.</param>
    public App(Func<BaseAppSettings, WebApplicationBuilder, string>? introMessageProvider = null)
    {
        appSettings = new BaseAppSettings();
        ConfigureThreadPool();
        builder = WebApplication.CreateSlimBuilder();
        ConfigureRestListener();
        string introMessage = introMessageProvider != null
            ? introMessageProvider(appSettings, builder)
            : $@"
             _____                            _   _               _                           
            / ____|                          | | (_)             | |                          
            | |     ___  _ __  _ __   ___  ___| |_ _  ___  _ __   | |     ___   ___  _ __  ___ 
            | |    / _ \| '_ \| '_ \ / _ \/ __| __| |/ _ \| '_ \  | |    / _ \ / _ \| '_ \/ __|
            | |___| (_) | | | | | | |  __/ (__| |_| | (_) | | | | | |___| (_) | (_) | |_) \__ \
            \_____\___/|_| |_|_| |_|\___|\___|\__|_|\___/|_| |_| |______\___/ \___/| .__/|___/
                                                                                    | |        
                                                                                    |_|        
            ╔╦╗┬┌─┐┬─┐┌─┐┌─┐┌─┐┬─┐┬  ┬┬┌─┐┌─┐┌─┐
            ║║║││  ├┬┘│ │└─┐├┤ ├┬┘└┐┌┘││  ├┤ └─┐
            ╩ ╩┴└─┘┴└─└─┘└─┘└─┘┴└─ └┘ ┴└─┘└─┘└─┘

            App:                     {appSettings.AssemblyName}
            Env:                     {builder.Environment.EnvironmentName}
            NATS URL:                {appSettings.NatsURL}
            TB Addresses:            {appSettings.TigerBeetleAddresses}
            TB Cluster ID:           {appSettings.TigerBeetleClusterId}
            OTEL Endpoint:           {appSettings.OtelEndpoint}
            Cluster:                 {appSettings.Cluster}
            Enable NATS Consumers:   {appSettings.EnableNatsConsumers}
        ";
        Console.WriteLine(introMessage);
        Console.WriteLine("Boostrapping app...");

        // add singleton services
        builder.Services.AddSingleton(appSettings);
        Console.WriteLine("Mapped AppSettings");

        ConfigureLogger();
        Console.WriteLine("Configured Serilog");

        ConfigureTigerBeetle(appSettings);

        if (!string.IsNullOrEmpty(appSettings.ConnectionString))
        {
            builder.Services.AddSingleton<IDB>(new DB(appSettings.ConnectionString));
            Console.WriteLine("Configured DB");
        }

        if (!string.IsNullOrEmpty(appSettings.NatsURL))
        {
            var cnc = new CloopsNatsClient(
                url: appSettings.NatsURL,
                name: appSettings.AssemblyName,
                creds: (!string.IsNullOrEmpty(appSettings.NatsCreds)) ? appSettings.NatsCreds : null
            );
            builder.Services.AddSingleton<ICloopsNatsClient>(cnc);
            builder.Services.AddHostedService<NatsLifecycleService>();
            builder.Services.AddSingleton<INatsMetricsService, NatsMetricsService>();
            Console.WriteLine("Configured NATS Client, Lifecycle Service, and Metrics Service");
        }

        ConfigureOTEL();

        RegisterControllers();
        RegisterServices();
        RegisterRestEndpoints();
        RegisterBackgroundServices();
        RegisterHttpServices();
    }

    /// <summary>
    /// Runs the application asynchronously
    /// usage: await app.RunAsync().ConfigureAwait(false);
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task RunAsync()
    {
        // build it
        host = builder.Build();
        MapRestEndpoints(host);
        return host.RunAsync();
    }

    private void RegisterControllers()
    {
        var controllerTypes = GetTargetTypes()
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Controllers", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        foreach (var controllerType in controllerTypes)
        {
            var interfaceType = FindInterface(controllerType);
            if (interfaceType != null)
            {
                builder.Services.AddSingleton(interfaceType, controllerType);
                Console.WriteLine($"Registered controller: {interfaceType.Name} -> {controllerType.Name}");
            }
            else
            {
                // Fallback: register concrete type if no interface found
                builder.Services.AddSingleton(controllerType);
                Console.WriteLine($"Registered controller (no interface): {controllerType.Name}");
            }
        }
    }

    private void RegisterServices()
    {
        var serviceTypes = GetTargetTypes()
            .Where(t =>
            {
                var ns = t.Namespace;
                if (string.IsNullOrEmpty(ns))
                {
                    return false;
                }

                var endsWithServices = ns.EndsWith("Services", StringComparison.OrdinalIgnoreCase);
                var endsWithBackground = ns.EndsWith("Services.Background", StringComparison.OrdinalIgnoreCase);
                var endsWithHttp = ns.EndsWith("Services.Http", StringComparison.OrdinalIgnoreCase);
                return endsWithServices && !endsWithBackground && !endsWithHttp;
            })
            .ToArray();

        foreach (var serviceType in serviceTypes)
        {
            var interfaceType = FindInterface(serviceType);
            if (interfaceType != null)
            {
                builder.Services.AddSingleton(interfaceType, serviceType);
                Console.WriteLine($"Registered service: {interfaceType.Name} -> {serviceType.Name}");
            }
            else
            {
                // Fallback: register concrete type if no interface found
                builder.Services.AddSingleton(serviceType);
                Console.WriteLine($"Registered service (no interface): {serviceType.Name}");
            }
        }
    }

    /// <summary>
    /// Finds the interface for a given type following the convention: interface starts with "I" and is in the same namespace.
    /// </summary>
    /// <param name="type">The concrete type to find an interface for</param>
    /// <returns>The interface type if found, null otherwise</returns>
    private Type? FindInterface(Type type)
    {
        var typeNamespace = type.Namespace;

        if (string.IsNullOrEmpty(typeNamespace))
        {
            return null;
        }

        // Check all interfaces that the type implements
        // Convention: Interface starts with "I" and is in the same namespace
        var interfaceType = type
            .GetInterfaces()
            .FirstOrDefault(i => i.Name.StartsWith("I", StringComparison.Ordinal) &&
                                 i.Namespace == typeNamespace);

        return interfaceType;
    }

    private void RegisterBackgroundServices()
    {
        var backgroundServiceTypes = GetTargetTypes()
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Services.Background", StringComparison.OrdinalIgnoreCase);
            })
            .Where(t => typeof(IHostedService).IsAssignableFrom(t))
            .ToArray();

        foreach (var backgroundServiceType in backgroundServiceTypes)
        {
            builder.Services.AddSingleton(typeof(IHostedService), backgroundServiceType);
            Console.WriteLine($"Registered background service: {backgroundServiceType.Name}");
        }
    }

    private void RegisterHttpServices()
    {
        builder.Services.AddHttpClient();

        var httpServiceTypes = GetTargetTypes()
            .Where(t => typeof(BaseHttpService).IsAssignableFrom(t))
            .ToArray();

        foreach (var httpServiceType in httpServiceTypes)
        {
            var interfaceType = FindInterface(httpServiceType);
            if (interfaceType != null)
            {
                builder.Services.AddSingleton(interfaceType, httpServiceType);
                Console.WriteLine($"Registered HTTP service: {interfaceType.Name} -> {httpServiceType.Name}");
            }
            else
            {
                builder.Services.AddSingleton(httpServiceType);
                Console.WriteLine($"Registered HTTP service (no interface): {httpServiceType.Name}");
            }
        }
    }

    private Type[] GetTargetTypes()
    {
        if (cachedTargetTypes != null)
        {
            return cachedTargetTypes;
        }

        var targetAssembly = ResolveTargetAssembly();
        cachedTargetTypes = targetAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        return cachedTargetTypes;
    }

    private Assembly ResolveTargetAssembly()
    {
        var targetAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a =>
                string.Equals(
                    a.GetName().Name,
                    appSettings.AssemblyName,
                    StringComparison.OrdinalIgnoreCase))
            ?? Assembly.GetEntryAssembly()
            ?? Assembly.GetExecutingAssembly();

        if (targetAssembly == null)
        {
            Console.WriteLine("No assembly found for registration.");
            throw new Exception("No assembly found for registration.");
        }
        return targetAssembly;
    }

    private void ConfigureLogger()
    {
        var environment = builder.Environment.EnvironmentName;
        var loggerConfig = new LoggerConfiguration()
        // Minimum levels
        .MinimumLevel.Information()
        // Override noisy framework namespaces
        .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)

        // Enrichers
        .Enrich.FromLogContext()
        .Enrich.WithThreadId()
        .Enrich.WithThreadName()
        .Enrich.WithProperty("Application", appSettings.AssemblyName);
        if (appSettings.Debug)
        {
            loggerConfig = loggerConfig.MinimumLevel.Debug();
        }

        // Configure console sink based on environment
        if (environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            // Production: Use compact JSON for structured logging
            loggerConfig = loggerConfig.WriteTo.Console(new CompactJsonFormatter());
        }
        else
        {
            // Non-production: Use human-friendly colorful console
            loggerConfig = loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                theme: AnsiConsoleTheme.Code
            );
        }

        Log.Logger = loggerConfig.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);
    }

    private void ConfigureThreadPool()
    {
        // Give the ThreadPool headroom under bursty loads
        ThreadPool.GetMinThreads(out var worker, out var io);
        var cpu = Environment.ProcessorCount;
        // bump min worker threads: enough to keep responders busy, not too high
        ThreadPool.SetMinThreads(Math.Max(worker, cpu * 2), io);
    }

    private void ConfigureOTEL()
    {
        string otelServiceName = appSettings.AssemblyName;
        string otelServiceEndpoint = appSettings.OtelEndpoint;
        string otelHeaders = appSettings.OtelHeaders;
        string clusterName = appSettings.Cluster;
        string appName = otelServiceName;

        ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: otelServiceName,
                serviceVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName
            )
            .AddAttributes(new Dictionary<string, object>
            {
                ["cluster"] = clusterName,
                ["app"] = appName,
                ["job"] = appName
            });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(meterProviderBuilder =>
            {
                meterProviderBuilder
                    .SetResourceBuilder(resourceBuilder: resourceBuilder)
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddMeter("AppMetrics")
                    .AddMeter("NatsMetrics")
                    .AddRuntimeInstrumentation();

                // Only add OTLP exporter if endpoint is configured
                if (!string.IsNullOrEmpty(otelServiceEndpoint))
                {
                    meterProviderBuilder.AddOtlpExporter(op =>
                    {
                        op.Endpoint = new Uri(otelServiceEndpoint);
                        op.Headers = otelHeaders;
                        op.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            })
            .WithTracing(traceProviderBuilder =>
            {
                traceProviderBuilder
                    .AddSource(appSettings.AssemblyName)
                    .SetResourceBuilder(resourceBuilder)
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation();

                // Only add OTLP exporter if endpoint is configured
                if (!string.IsNullOrEmpty(otelServiceEndpoint))
                {
                    traceProviderBuilder.AddOtlpExporter(op =>
                    {
                        op.Endpoint = new Uri(otelServiceEndpoint);
                        op.Headers = otelHeaders;
                        op.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            });
        Console.WriteLine("Configured OpenTelemetry");
    }

    /// <summary>
    /// Adds TigerBeetle Client to DI
    /// </summary>
    private void ConfigureTigerBeetle(BaseAppSettings appSettings)
    {
        if (String.IsNullOrWhiteSpace(appSettings.TigerBeetleAddresses))
        {
            Console.WriteLine("No TigerBeetle Database Configured");
            return;
        }

        var addresses = appSettings.TigerBeetleAddresses
            .Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (addresses.Length == 0)
        {
            Console.WriteLine("No valid TigerBeetle addresses configured");
            return;
        }

        builder.Services.AddSingleton(_ =>
            new TigerBeetle.Client(appSettings.TigerBeetleClusterId, addresses));

        Console.WriteLine("Configured TigerBeetle Client");
    }

}
