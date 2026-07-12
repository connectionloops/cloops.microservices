using IdGen;
using IdGen.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CLOOPS.microservices;

public partial class App
{
    /// <summary>
    /// Registers the Snowflake ID generator (IdGen) in dependency injection when
    /// <see cref="BaseAppSettings.EnableSnowflakeId"/> is set. The generator (node)
    /// id must be unique and stable per replica; see docs/snowflake-id.md.
    /// </summary>
    private void ConfigureSnowflake()
    {
        if (!appSettings.EnableSnowflakeId)
        {
            Log.Information("ℹ️ Snowflake ID generation not enabled (ENABLE_SNOWFLAKE_ID not set)");
            return;
        }

        if (appSettings.SnowflakeGeneratorId < 0)
        {
            throw new InvalidOperationException(
                "ENABLE_SNOWFLAKE_ID is set but SNOWFLAKE_GENERATOR_ID is missing or invalid. " +
                "Set SNOWFLAKE_GENERATOR_ID to a unique, stable per-node integer (e.g. a Kubernetes " +
                "StatefulSet pod ordinal) to avoid ID collisions across replicas.");
        }

        builder.Services.AddIdGen(appSettings.SnowflakeGeneratorId);

        Log.Information(
            "✅ Configured Snowflake ID generator (IdGen) with generator-id {GeneratorId}",
            appSettings.SnowflakeGeneratorId);
    }
}
