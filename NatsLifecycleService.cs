using CLOOPS.NATS;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace CLOOPS.microservices;

/// <summary>
/// Nats lifecycle service
/// Initializes the client connection
/// Maps consumers
/// </summary>
public class NatsLifecycleService : BackgroundService
{
    private readonly ICloopsNatsClient _client;
    private readonly ILogger<NatsLifecycleService> _logger;
    private readonly IServiceProvider _sp;
    private readonly AppSettings _appsettings;

    /// <summary>
    /// Creates an instance of NatsLifecycleService Instance
    /// </summary>
    /// <param name="client">Nats Client</param>
    /// <param name="logger">Logger</param>
    /// <param name="sp"></param>
    /// <param name="appSettings"></param>
    public NatsLifecycleService(
        ICloopsNatsClient client,
        ILogger<NatsLifecycleService> logger,
        IServiceProvider sp,
        AppSettings appSettings
    )
    {
        _client = client;
        _logger = logger;
        _sp = sp;
        _appsettings = appSettings;
    }

    /// <summary>
    /// Executes the service
    /// </summary>
    /// <param name="stoppingToken">Token to cancel the operation</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Connection.ConnectionDisconnected += (_, e) =>
        {
            _logger.LogError("NATS disconnected.");
            return ValueTask.CompletedTask;
        };

        _client.Connection.ConnectionOpened += (_, e) =>
        {
            _logger.LogInformation("NATS connected");
            return ValueTask.CompletedTask;
        };

        await _client.ConnectAsync();

        if (_client.Connection.ConnectionState == NatsConnectionState.Open)
        {
            if (_appsettings.EnableNatsConsumers)
            {
                _logger.LogInformation("EnableNatsConsumers is enabled, starting NATS consumers");
                await _client.MapConsumers(_sp, stoppingToken, [_appsettings.AssemblyName]);
            }
            else
            {
                _logger.LogInformation("EnableNatsConsumers is disabled, skipping NATS consumers");
            }
        }
        else
        {
            _logger.LogError("NATS is not able to connect, cannot start consumers");
        }
    }
}