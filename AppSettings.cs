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
    /// Gets the Redis connection string used by distributed caching.
    /// </summary>
    public string RedisConnectionString { get; init; } = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "";

    /// <summary>
    /// Gets the optional Redis instance name prefix used by distributed caching.
    /// </summary>
    public string RedisInstanceName { get; init; } = Environment.GetEnvironmentVariable("REDIS_INSTANCE_NAME") ?? "";

    /// <summary>
    /// Gets a value indicating whether NATS consumers should run.
    /// </summary>
    public bool EnableNatsConsumers { get; init; } = Convert.ToBoolean(Environment.GetEnvironmentVariable("ENABLE_NATS_CONSUMERS") ?? "False");

    /// <summary>
    /// Gets a value indicating whether database migrations should run when a migrations directory is present.
    /// </summary>
    public bool EnableMigrations { get; init; } = Convert.ToBoolean(Environment.GetEnvironmentVariable("ENABLE_MIGRATIONS") ?? "True");

    /// <summary>
    /// Gets a value indicating whether REST endpoints should run.
    /// </summary>
    public bool EnableRestEndpoints { get; init; } = Convert.ToBoolean(Environment.GetEnvironmentVariable("ENABLE_REST_ENDPOINTS") ?? "True");

    /// <summary>
    /// Gets the port used by the lightweight REST endpoint server.
    /// </summary>
    public int RestPort { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("REST_PORT"), out var restPort) ? restPort : 8080;

    /// <summary>
    /// Gets the shared secret required by REST endpoints marked as auth-required.
    /// </summary>
    public string RestApiSecret { get; init; } = Environment.GetEnvironmentVariable("REST_API_SECRET") ?? "";

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
