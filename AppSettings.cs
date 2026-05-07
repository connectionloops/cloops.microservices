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
    public string NatsURL { get; init; } = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

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

    /// <summary>
    /// Gets the comma-separated TigerBeetle address list. e.g. 127.0.0.1:3000,127.0.0.1:3001
    /// </summary>
    public String TigerBeetleAddresses { get; init; } = Environment.GetEnvironmentVariable("TB_ADDRESSES") ?? "";

    /// <summary>
    /// Gets the TigerBeetle cluster ID. Defaults to 0 if TB_CLUSTER_ID is missing or invalid.
    /// </summary>
    public ulong TigerBeetleClusterId { get; init; } = ParseTigerBeetleClusterId();

    private static ulong ParseTigerBeetleClusterId()
    {
        return ulong.TryParse(Environment.GetEnvironmentVariable("TB_CLUSTER_ID"), out var clusterId)
            ? clusterId
            : 0;
    }
}
