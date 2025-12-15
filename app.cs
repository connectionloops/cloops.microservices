using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Reflection;
using CLOOPS.NATS;
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
public class App
{
    /// <summary>
    /// The application settings
    /// </summary>
    public BaseAppSettings appSettings;
    /// <summary>
    /// The host application builder
    /// </summary>
    public HostApplicationBuilder builder;

    /// <summary>
    /// The host application
    /// </summary>
    public IHost host;

    /// <summary>
    /// Creates the DI pipeline and starts the application.
    /// </summary>
    /// <param name="introMessageProvider">Optional function that takes BaseAppSettings and returns a custom intro message. If not provided, a default message will be used.</param>
    public App(Func<BaseAppSettings, string>? introMessageProvider = null)
    {
        appSettings = new BaseAppSettings();
        string introMessage = introMessageProvider != null
            ? introMessageProvider(appSettings)
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
            NATS URL:                {appSettings.NatsURL}
            OTEL Endpoint:           {appSettings.OtelEndpoint}
            Cluster:                 {appSettings.Cluster}
            Enable NATS Consumers:   {appSettings.EnableNatsConsumers}
        ";
        Console.WriteLine(introMessage);
        Console.WriteLine("Boostrapping app...");
        ConfigureThreadPool();
        builder = Host.CreateApplicationBuilder();

        // add singleton services
        builder.Services.AddSingleton(appSettings);
        Console.WriteLine("Mapped AppSettings");

        ConfigureLogger();
        Console.WriteLine("Configured Serilog");

        if (!string.IsNullOrEmpty(appSettings.ConnectionString))
        {
            builder.Services.AddSingleton<IDB>(new DB(appSettings.ConnectionString));
            Console.WriteLine("Configured DB");
        }

        if (!string.IsNullOrEmpty(appSettings.NatsURL) && !string.IsNullOrEmpty(appSettings.NatsCreds))
        {
            var cnc = new CloopsNatsClient(
                url: appSettings.NatsURL,
                name: appSettings.AssemblyName,
                creds: appSettings.NatsCreds
            );
            builder.Services.AddSingleton<ICloopsNatsClient>(cnc);
            builder.Services.AddHostedService<NatsLifecycleService>();
            builder.Services.AddSingleton<INatsMetricsService, NatsMetricsService>();
            Console.WriteLine("Configured NATS Client, Lifecycle Service, and Metrics Service");
        }

        ConfigureOTEL();
        Console.WriteLine("Configured OpenTelemetry");

        RegisterControllers();
        RegisterServices();
        RegisterBackgroundServices();
        RegisterHttpServices();

        // build it
        host = builder.Build();
    }

    /// <summary>
    /// Runs the application asynchronously
    /// usage: await app.RunAsync().ConfigureAwait(false);
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task RunAsync()
    {
        return host.RunAsync();
    }

    private void RegisterControllers()
    {
        var targetAssembly = ResolveTargetAssembly();

        var controllerTypes = targetAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Controllers", StringComparison.OrdinalIgnoreCase);
            })
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        foreach (var controllerType in controllerTypes)
        {
            builder.Services.AddSingleton(controllerType);
            Console.WriteLine($"Registered controller: {controllerType.Name}");
        }
    }

    private void RegisterServices()
    {
        var targetAssembly = ResolveTargetAssembly();

        var serviceTypes = targetAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
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
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        foreach (var serviceType in serviceTypes)
        {
            builder.Services.AddSingleton(serviceType);
            Console.WriteLine($"Registered service: {serviceType.Name}");
        }
    }

    private void RegisterBackgroundServices()
    {
        var targetAssembly = ResolveTargetAssembly();
        if (targetAssembly == null)
        {
            Console.WriteLine("No assembly found for background service registration.");
            return;
        }

        var backgroundServiceTypes = targetAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Services.Background", StringComparison.OrdinalIgnoreCase);
            })
            .Where(t => typeof(IHostedService).IsAssignableFrom(t))
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        foreach (var backgroundServiceType in backgroundServiceTypes)
        {
            builder.Services.AddSingleton(typeof(IHostedService), backgroundServiceType);
            Console.WriteLine($"Registered background service: {backgroundServiceType.Name}");
        }
    }

    private void RegisterHttpServices()
    {
        var targetAssembly = ResolveTargetAssembly();

        var httpClientExtensionsType = Type.GetType("Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions, Microsoft.Extensions.Http");
        if (httpClientExtensionsType == null)
        {
            Console.WriteLine("HttpClientFactory extensions not found. Skipping HTTP service registration.");
            return;
        }
        var addHttpClientGenericMethod = httpClientExtensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "AddHttpClient" &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1);

        if (addHttpClientGenericMethod == null)
        {
            Console.WriteLine("AddHttpClient generic method not available. Skipping HTTP service registration.");
            return;
        }

        var httpServiceTypes = targetAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
            .Where(t =>
            {
                var ns = t.Namespace;
                return !string.IsNullOrEmpty(ns) &&
                       ns.EndsWith("Services.Http", StringComparison.OrdinalIgnoreCase);
            })
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .ToArray();

        foreach (var httpServiceType in httpServiceTypes)
        {
            try
            {
                var typedAddHttpClient = addHttpClientGenericMethod.MakeGenericMethod(httpServiceType);
                typedAddHttpClient.Invoke(null, [builder.Services]);
                Console.WriteLine($"Registered HTTP service: {httpServiceType.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register HTTP service {httpServiceType.Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
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
    }
}