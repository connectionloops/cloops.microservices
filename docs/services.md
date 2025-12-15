# Services

Services are the primary components in cloops.microservices. Any class that's doing some business logic functionality is a service. It is similar to a classic DI enabled REST application with controllers and services. Few examples of services are -

- `JobService` - A service that accepts a job and processes it on a set of workers
- `WeatherService` - A service that makes a 3P API call to get weather information
- `DbBnRService` - A service that takes backup of databases on a regular schedule and also provides functions to restore a backup to a database.

Services are further divided into three types -

- Traditional: Nothing special, just normal services.
- Http: services that make a third party (3P) http api call
- Background: services that run in background that do some activity continuously (typically on a schedule) .e.g some sort of clean up.

Any member function in a service can act as a NATS consumer and listen for incoming messages on a subject.

## Types of services

### Traditional Services

The default type. Nothing special about it. Used for purposes mentioned above in main services section. Most NATS handlers will be registered as traditional services.

> **To register a class as traditional service it has to belong to a namespace ending with `Services` . e.g. `Cljps.Services`**

e.g. below service has a function that responds to health requests coming in on NATS subject `health.cljps.scheduler`

```cs
using CLOOPS.NATS.Attributes;
using CLOOPS.NATS.Meta;
using CLOOPS.NATS.Messages;
using NATS.Client.Core;

namespace cljps.scheduler.services;

public class HealthService
{

    [NatsConsumer(_subject: "health.cljps.scheduler")]
    public Task<NatsAck> GetHealth(NatsMsg<string> msg, CancellationToken ct = default)
    {
        var reply = new HealthReply
        {
            Status = new()
            {
                ["appName"] = "cljps.scheduler",
                ["appStatus"] = "ok"
            }
        };
        return Task.FromResult(new NatsAck(_isAck: true, _reply: reply));
    }
}
```

### Background Services

These are the services that are continuously doing something in the background (typically on a schedule).

> **To register a class as a background service it has to belong to a namespace ending with `Services.Background` . e.g. `Cljps.Services.Background`**

e.g. below code from Connection Loops Job Processing System deletes all the stale jobs

```cs
using Microsoft.Extensions.Hosting;
using CLOOPS.microservices.Extensions;
using CLOOPS.microservices;
using CLOOPS.NATS;
using Microsoft.Data.SqlClient;

namespace cljps.scheduler.services.background
{
    /// <summary>
    /// Background service that cleans up old CLJPS jobs based on retention policy
    /// Implements FR-2D: Deletes jobs that have passed retention period (default 24h after being in executed state)
    /// This service connects to the CLJPS database and removes old jobs in Done/Failed status
    /// </summary>
    public class CljpsJobCleanupService : BackgroundService
    {
        private readonly AppSettings _config;
        private readonly IDB _db;

        public CljpsJobCleanupService(AppSettings config, IDB db)
        {
            _db = db;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cronExpression = Util.GetCronExpression(_config.CljpsJobCleanupConfig.CleanupCron);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await cronExpression.AwaitUntilNextOccurrenceAsync(_logger, _config.CljpsJobCleanupConfig.CleanupCron, "CljpsJobCleanupService", stoppingToken)) { continue; }

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        var handle = await _client.AcquireDistributedLockAsync("cljps.cleanup", TimeSpan.FromMilliseconds(500), ct: stoppingToken);
                        if (handle is null) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                        await using (handle)
                        {
                            try { await CleanupOldJobsAsync(stoppingToken); }
                            catch (Exception ex) { _logger.LogError(ex, "[CljpsJobCleanupService]::Error during cleanup"); }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("[CljpsJobCleanupService]::Service shutting down");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[CljpsJobCleanupService]::Error executing cleanup loop");
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }
        }

        /// <summary>
        /// Performs the actual cleanup of old CLJPS jobs
        /// Deletes jobs that are in Done or Failed status and have passed the retention period
        /// </summary>
        /// <param name="stoppingToken">Cancellation token</param>
        private async Task CleanupOldJobsAsync(CancellationToken stoppingToken)
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddHours(-_config.CljpsJobCleanupConfig.RetentionHours);

            var deleteQuery = @"
                SET NOCOUNT ON;
                DELETE FROM jobs2 WHERE jobStatus > 2 AND updated_at < @cutoffTime;
                SELECT @@ROWCOUNT as DeletedCount;
            ";
            var deleteParams = DB.pars(("@cutoffTime", cutoffTime));
            var dbResult = await _db.ExecuteReadAsync<DeleteResult>(deleteQuery, deleteParams, cancellationToken: stoppingToken).ToArrayAsync();
            if (dbResult.Length > 0 && dbResult[0].DeletedCount > 0)
            {
                _logger.LogDebug("[CljpsJobCleanupService]::Successfully deleted {deletedCount} old CLJPS jobs. Retention cutoff: {cutoffTime}",
                    dbResult[0].DeletedCount, cutoffTime);
                _metrics.RecordCleanupJobsCount(dbResult[0].DeletedCount);
            }
            else
            {
                _metrics.RecordCleanupJobsCount(0);
                _logger.LogDebug("[CljpsJobCleanupService]::No CLJPS jobs found for cleanup");
            }
        }

        private class DeleteResult { public int DeletedCount { get; set; } }
    }
}

```

### Http Services

These are services that uses a http client to call third party (3P) http APIs.

http client is a very special class and it should not be instantiated at will. Creating a new http client every time can quickly results in port exhaustion. It is therefore recommended to reuse http client. People typically create one http client for one coherent functionality. e.g.

1. http client to make API calls to Azure.
2. http client to make API calls to GitHub

http services in cloops.microservices just means that the service will have access to an injected http client who's lifecycle is managed. It is recommended that you create one http service per 3P REST API service. e.g. If you were to incorporate above example, you would create two http services.

1. AzureService
2. GitHubService

each of these will come with their own managed http client, and you wouldn't run into port exhaustion.

> For more information on http client, please read - [this](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests)
> Please note: you wouldn't use this to communicate with other microservices created using cloops.nats. It is mainly for talking to http services not nats based services.

> **To register a class as a http service it has to belong to a namespace ending with `Services.Http` . e.g. `Cljps.Services.Http`**

Example from CLJPS (Connection Loops Job Processing System):

```cs
using CLOOPS.NATS;
using CLOOPS.NATS.Messages.CLJPS;
using System.Text.Json.Nodes;
using CljpsHttpMethod = CLOOPS.NATS.Messages.CLJPS.HttpMethod;
using System.Diagnostics;

namespace cljps.worker.services.http;
public sealed class JobExecutionService
{
    private readonly HttpClient _httpClient;
    public JobExecutionService(HttpClient httpClient)
    {
        _httpClient = httpClient; // injected http client
        // add any common headers, base url etc.
    }

    public async Task<bool> execute(RunnableJob job, CancellationToken ct)
    {
        HttpRequestMessage requestMessage = new HttpRequestMessage(Util.GetHttpMethod(job.JobHttpMethod), job.JobUrl);
        if (payload is not null)
        {
            requestMessage.Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }
        var isSuccess = false;
        var responseString = "";
        var statusCode = 0;
        HttpResponseMessage? responseMessage = null;
        try
        {
            responseMessage = await _httpClient.SendAsync(requestMessage, ct);
            isSuccess = responseMessage.IsSuccessStatusCode;
            statusCode = (int)responseMessage.StatusCode;
        }
        catch (Exception) { } // try-catch needed to handle network failures e.g. DNS resolution failure etc.
    }
}
```

## Deciding which type of service you'd need

When creating a new service, use the following guidelines to determine which type of service to use:

1. **To make 3P API calls** → Use an **Http Service**

   - When you need to make HTTP requests to third-party REST APIs
   - Examples: Calling external APIs like GitHub, Azure, weather services, payment gateways
   - Remember: This is for external HTTP services, not for communicating with other cloops.nats microservices

2. **To do something continuous in background** → Use a **Background Service**

   - When you need to run scheduled tasks, periodic cleanup, or continuous monitoring
   - Examples: Job cleanup, health checks, scheduled backups, periodic data synchronization
   - These services run continuously and typically operate on a schedule (cron-based)

3. **For NATS handlers** → Use a **Traditional Service**

   - When you need to handle NATS messages using `[NatsConsumer]` attributes
   - Most message handlers will be traditional services
   - Examples: Health check handlers, job processing handlers, event consumers

4. **Anything else** → Use a **Traditional Service**
   - Default choice for any business logic that doesn't fit the above categories
   - General-purpose services for your application logic
   - Examples: Data processing, business rule validation, internal utilities
