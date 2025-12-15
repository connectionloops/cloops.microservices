using System.Reflection;

/// <summary>
/// The application settings
/// These are loaded from the environment variables.
/// You can inherit this class and expand your own settings.
/// </summary>
public class BaseAppSettings
{
    /// <summary>
    /// Gets a value indicating whether verbose debugging is enabled.
    /// </summary>
    public bool Debug { get; init; } = Convert.ToBoolean(Environment.GetEnvironmentVariable("DEBUG") ?? "False");

    /// <summary>
    /// Gets the NATS server URL.
    /// </summary>
    public string NatsURL { get; init; } = Environment.GetEnvironmentVariable("NATS_URL") ?? "tls://nats.ccnp.cloops.in:4222";

    /// <summary>
    /// Gets the NATS credentials content.
    /// </summary>
    public string NatsCreds { get; init; } = Environment.GetEnvironmentVariable("NATS_CREDS") ?? "";

    /// <summary>
    /// Gets the assembly name reported by the application.
    /// </summary>
    public string AssemblyName { get; init; } = Assembly.GetEntryAssembly()?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName ?? "unknown";

    /// <summary>
    /// Gets the OpenTelemetry endpoint for CCNP.
    /// </summary>
    public string OtelEndpoint { get; init; } = Environment.GetEnvironmentVariable("OTELENDPOINT") ?? "";

    /// <summary>
    /// Gets the OpenTelemetry headers for CCNP.
    /// </summary>
    public string OtelHeaders { get; init; } = Environment.GetEnvironmentVariable("OTELHEADERS") ?? "";

    /// <summary>
    /// Gets the cluster name the service targets.
    /// </summary>
    public string Cluster { get; init; } = Environment.GetEnvironmentVariable("CLUSTER") ?? "ccnp";

    /// <summary>
    /// Gets the SQL connection string.
    /// </summary>
    public string ConnectionString { get; init; } = Environment.GetEnvironmentVariable("CNSTR") ?? "";

    /// <summary>
    /// Gets a value indicating whether NATS consumers should run.
    /// </summary>
    public bool EnableNatsConsumers { get; init; } = Convert.ToBoolean(Environment.GetEnvironmentVariable("ENABLE_NATS_CONSUMERS") ?? "False");
}