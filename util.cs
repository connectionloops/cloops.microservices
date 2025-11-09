using Cronos;

namespace CLOOPS.microservices;

/// <summary>
/// Contains utility functions for the application
/// </summary>
public class BaseUtil : CLOOPS.NATS.BaseNatsUtil
{
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

}
