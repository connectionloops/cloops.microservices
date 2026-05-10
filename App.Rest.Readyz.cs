using CLOOPS.microservices.Readyz;
using CLOOPS.NATS;
using Microsoft.AspNetCore.Http;
using NATS.Client.Core;

namespace CLOOPS.microservices;

public partial class App
{
    private static readonly IResult RestReadyOkResult = Results.Ok(new { ready = true });

    /// <summary>
    /// Handles GET /readyz. Designed to be cheap to call:
    /// - NATS check is an in-memory connection-state read.
    /// - TigerBeetle check reads a cached L1-only probe result; the actual RPC
    ///   to TigerBeetle runs in the background every ~4 minutes via the
    ///   <see cref="TigerBeetleReadinessCacheService"/>.
    /// </summary>
    private static async Task<IResult> ReadyzAsync(
        ICloopsNatsClient? natsClient,
        BaseAppSettings appSettings,
        TigerBeetleReadinessCacheService? tigerBeetleReadiness,
        CancellationToken ct)
    {
        List<string>? reasons = null;

        if (natsClient?.Connection.ConnectionState != NatsConnectionState.Open)
        {
            (reasons ??= []).Add("nats disconnected");
        }

        if (!string.IsNullOrWhiteSpace(appSettings.TigerBeetleAddresses))
        {
            if (tigerBeetleReadiness is null)
            {
                (reasons ??= []).Add("tigerbeetle client not configured");
            }
            else if (!await tigerBeetleReadiness.GetReadyAsync(ct).ConfigureAwait(false))
            {
                (reasons ??= []).Add("tigerbeetle disconnected");
            }
        }

        if (reasons is null)
        {
            return RestReadyOkResult;
        }

        return Results.Json(
            new
            {
                ready = false,
                reason = string.Join("; ", reasons),
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
