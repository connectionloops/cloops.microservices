using CLOOPS.NATS;
using CLOOPS.NATS.Locking;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

/// <summary>
/// Runs DbUp SQL migrations from the app output migrations directory during host startup.
/// </summary>
public sealed class DbMigrationHostedService : IHostedService
{
    private const string MigrationsDirectoryName = "migrations";
    private static readonly TimeSpan MigrationLockTimeout = TimeSpan.FromMilliseconds(500);

    private readonly BaseAppSettings appSettings;
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<DbMigrationHostedService> logger;
    private readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Creates a hosted service that applies DbUp migrations during host startup.
    /// </summary>
    public DbMigrationHostedService(
        BaseAppSettings appSettings,
        IServiceProvider serviceProvider,
        ILogger<DbMigrationHostedService> logger,
        ILoggerFactory loggerFactory)
    {
        this.appSettings = appSettings;
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var migrationsPath = Path.Combine(AppContext.BaseDirectory, MigrationsDirectoryName);

        if (!Directory.Exists(migrationsPath))
        {
            logger.LogInformation("✅ Database migrations directory not found at {MigrationsPath}; skipping migrations", migrationsPath);
            return;
        }

        if (!appSettings.EnableMigrations)
        {
            logger.LogWarning("⚠️ Database migrations directory found at {MigrationsPath}, but ENABLE_MIGRATIONS=False; skipping migrations", migrationsPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(appSettings.ConnectionString))
        {
            throw new InvalidOperationException("Database migrations are enabled and a migrations directory exists, but CNSTR is not configured.");
        }

        var sqlScripts = LoadSqlScripts(migrationsPath);
        if (sqlScripts.Length == 0)
        {
            logger.LogInformation("✅ Database migrations directory found at {MigrationsPath}, but it contains no .sql files; skipping migrations", migrationsPath);
            return;
        }

        var natsClient = serviceProvider.GetService<ICloopsNatsClient>();
        if (!await BaseUtil.WaitForNatsConnectionAsync(natsClient, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("⚠️ Skipping database migrations because NATS is not ready after {NatsWaitTimeout}. Another pod may be applying migrations; ensure migrations are backward compatible.", BaseUtil.NatsConnectionWaitTimeout);
            return;
        }

        var migrationLockKey = $"db-migrations.{appSettings.AssemblyName}";
        DistributedLockHandle? handle;
        try
        {
            handle = await natsClient!.AcquireDistributedLockAsync(
                migrationLockKey,
                MigrationLockTimeout,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "⚠️ Skipping database migrations because distributed lock {MigrationLockKey} could not be acquired. Another pod may be applying migrations.", migrationLockKey);
            return;
        }

        if (handle == null)
        {
            logger.LogWarning("⚠️ Skipping database migrations because distributed lock {MigrationLockKey} could not be acquired. Another pod may be applying migrations.", migrationLockKey);
            return;
        }

        await using (handle)
        {
            RunMigrations(sqlScripts);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private SqlScript[] LoadSqlScripts(string migrationsPath)
    {
        return Directory
            .EnumerateFiles(migrationsPath, "*.sql", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Name = Path.GetRelativePath(migrationsPath, path)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/'),
            })
            .OrderBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
            .Select(script => new SqlScript(script.Name, File.ReadAllText(script.Path)))
            .ToArray();
    }

    private void RunMigrations(SqlScript[] sqlScripts)
    {
        var upgrader = DeployChanges.To
            .SqlDatabase(appSettings.ConnectionString)
            .WithScripts(sqlScripts)
            .LogTo(loggerFactory)
            .LogScriptOutput()
            .Build();

        var pendingScripts = upgrader.GetScriptsToExecute();
        if (pendingScripts.Count == 0)
        {
            logger.LogInformation("✅ Database schema is up to date; no migrations to apply");
            return;
        }

        logger.LogInformation("✅ Applying {MigrationCount} database migration(s): {MigrationNames}",
            pendingScripts.Count,
            string.Join(", ", pendingScripts.Select(script => script.Name)));

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            logger.LogError(result.Error, "❌ Database migration failed");
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        logger.LogInformation("✅ Successfully applied {MigrationCount} database migration(s)", pendingScripts.Count);
    }
}
