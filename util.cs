using Cronos;
using CLOOPS.NATS;
using NATS.Client.Core;

namespace CLOOPS.microservices;

/// <summary>
/// Contains utility functions for the application
/// </summary>
public class BaseUtil : CLOOPS.NATS.BaseNatsUtil
{
    /// <summary>
    /// Default amount of time to wait for a NATS connection during startup coordination.
    /// </summary>
    public static readonly TimeSpan NatsConnectionWaitTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Default polling interval while waiting for a NATS connection during startup coordination.
    /// </summary>
    public static readonly TimeSpan NatsConnectionPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Checks whether a type inherits from an open generic type, such as BaseCacheService&lt;&gt;.
    /// </summary>
    /// <param name="type">The concrete type to check</param>
    /// <param name="openGenericType">The open generic type definition</param>
    /// <returns>True when the type inherits from the open generic type</returns>
    public static bool IsAssignableToOpenGeneric(Type type, Type openGenericType)
    {
        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == openGenericType)
            {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Parses a cron expression and returns a CronExpression object
    /// </summary>
    /// <param name="cron">The cron expression to parse</param>
    /// <returns>A CronExpression object</returns>
    /// <exception cref="Exception">Thrown if the cron expression is invalid</exception>
    public static CronExpression GetCronExpression(string cron)
    {
        var mode = cron.Split(" ").Count() == 5 ? CronFormat.Standard : CronFormat.IncludeSeconds;
        var cronExpression = CronExpression.Parse(cron, mode);
        if (cronExpression is null)
        {
            throw new Exception($"Invalid cron expression: {cron}");
        }
        return cronExpression;
    }

    /// <summary>
    /// Waits until a NATS client reports an open connection or the timeout elapses.
    /// </summary>
    /// <param name="natsClient">The NATS client to observe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="timeout">Optional wait timeout. Defaults to <see cref="NatsConnectionWaitTimeout"/>.</param>
    /// <param name="pollInterval">Optional poll interval. Defaults to <see cref="NatsConnectionPollInterval"/>.</param>
    /// <returns>True when NATS is connected; otherwise false.</returns>
    public static async Task<bool> WaitForNatsConnectionAsync(
        ICloopsNatsClient? natsClient,
        CancellationToken ct,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        if (natsClient == null)
        {
            return false;
        }

        if (natsClient.Connection.ConnectionState == NatsConnectionState.Open)
        {
            return true;
        }

        var deadline = DateTimeOffset.UtcNow + (timeout ?? NatsConnectionWaitTimeout);
        var delay = pollInterval ?? NatsConnectionPollInterval;
        while (natsClient.Connection.ConnectionState != NatsConnectionState.Open)
        {
            if (ct.IsCancellationRequested || DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Returns true when the NATS client exists and currently reports an open connection.
    /// </summary>
    /// <param name="natsClient">The NATS client to inspect.</param>
    /// <returns>True when NATS is connected; otherwise false.</returns>
    public static bool IsNatsConnected(ICloopsNatsClient? natsClient)
    {
        return natsClient?.Connection.ConnectionState == NatsConnectionState.Open;
    }
}
