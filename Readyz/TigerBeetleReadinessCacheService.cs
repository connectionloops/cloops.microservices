using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TigerBeetle;

namespace CLOOPS.microservices.Readyz;

/// <summary>
/// L1-only readiness cache for the TigerBeetle client.
/// Keeps the readyz endpoint cheap: instead of doing a live RPC per probe, readyz reads
/// the cached connection status, which is refreshed in the background every ~4 minutes.
/// </summary>
[CacheConfig(
    name: TigerBeetleReadinessCacheService.CacheNameValue,
    l1Ttl: "00:05:00",
    RefreshOnStartup = true,
    RefreshCron = "0 */4 * * * *")]
internal sealed class TigerBeetleReadinessCacheService : BaseCacheService<bool>
{
    internal const string CacheNameValue = "tigerbeetle-readiness";
    internal const string StatusKey = "status";

    private static readonly UInt128[] ProbeIds = [UInt128.One];
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly Client tigerBeetleClient;
    private readonly ILogger<TigerBeetleReadinessCacheService> readinessLogger;

    public TigerBeetleReadinessCacheService(IServiceProvider serviceProvider, Client tigerBeetleClient)
        : base(serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(tigerBeetleClient);
        this.tigerBeetleClient = tigerBeetleClient;
        readinessLogger = serviceProvider.GetRequiredService<ILogger<TigerBeetleReadinessCacheService>>();
    }

    /// <summary>
    /// Returns the cached readiness status. Performs a single live probe on a cold miss,
    /// then returns the cached value until the next scheduled refresh.
    /// </summary>
    public async Task<bool> GetReadyAsync(CancellationToken ct = default)
    {
        // GetAsync returns Task<TValue?>; for value-type TValue (bool) the runtime type is bool,
        // so a "missing" cache entry surfaces as false, which is the right "not ready" answer.
        var value = await GetAsync(StatusKey, ct).ConfigureAwait(false);
        return value;
    }

    /// <inheritdoc />
    protected override async Task<bool> HydrateSingleAsync(string key, CancellationToken ct)
    {
        return await ProbeAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyDictionary<string, bool>> HydrateAllAsync(CancellationToken ct)
    {
        var ready = await ProbeAsync(ct).ConfigureAwait(false);
        return new Dictionary<string, bool>(capacity: 1)
        {
            [StatusKey] = ready,
        };
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            await tigerBeetleClient
                .LookupAccountsAsync(ProbeIds)
                .WaitAsync(ProbeTimeout, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller is shutting down; don't poison the cache with a failure.
            throw;
        }
        catch (TimeoutException)
        {
            readinessLogger.LogWarning("TigerBeetle readiness probe timed out after {ProbeTimeout}", ProbeTimeout);
            return false;
        }
        catch (Exception ex)
        {
            readinessLogger.LogWarning(ex, "TigerBeetle readiness probe failed");
            return false;
        }
    }
}
