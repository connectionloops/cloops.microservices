using Cronos;

namespace CLOOPS.microservices;

/// <summary>
/// Contains utility functions for the application
/// </summary>
public class BaseUtil : CLOOPS.NATS.BaseNatsUtil
{
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

}
