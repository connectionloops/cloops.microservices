using Microsoft.Data.SqlClient;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Connection details for the SQL Server the date/time integration tests run against.
///
/// <para>The behaviour under test - how SQL Server converts and compares <c>datetime</c> and
/// <c>datetime2</c> - is a database behaviour, so a mock would prove nothing. Point
/// <c>CLOOPS_TEST_SQL_CONNECTION_STRING</c> at any SQL Server instance, or start the container the
/// default connection string expects:</para>
/// <code>
/// docker run -d --name cloopsmssql --platform linux/amd64 \
///   -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Str0ng!Passw0rd' -e MSSQL_PID=Developer \
///   -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
/// </code>
/// </summary>
internal static class SqlServer
{
    internal const string ConnectionStringVariable = "CLOOPS_TEST_SQL_CONNECTION_STRING";

    private const string DefaultConnectionString =
        "Server=localhost,14333;User Id=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=True;Encrypt=False;Database=master";

    private static readonly Lazy<string?> unavailableReason = new(Probe, isThreadSafe: true);

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DefaultConnectionString;

    /// <summary>Null when a server answered, otherwise the reason the tests were skipped.</summary>
    internal static string? UnavailableReason => unavailableReason.Value;

    internal static SqlConnection Open()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static string? Probe()
    {
        try
        {
            using var connection = Open();
            using var command = new SqlCommand("SELECT 1", connection);
            command.CommandTimeout = 15;
            command.ExecuteScalar();
            return null;
        }
        catch (Exception ex)
        {
            return $"No SQL Server at the test connection string - set {ConnectionStringVariable} or start the " +
                   $"mcr.microsoft.com/mssql/server:2022-latest container. ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})";
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips when no SQL Server is reachable, so the suite still runs
/// on a machine without a container instead of reporting failures that are not about the code.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping the test when no SQL Server answers.</summary>
    public SqlServerFactAttribute()
    {
        if (SqlServer.UnavailableReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <inheritdoc cref="SqlServerFactAttribute"/>
public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute, skipping the test when no SQL Server answers.</summary>
    public SqlServerTheoryAttribute()
    {
        if (SqlServer.UnavailableReason is { } reason)
        {
            Skip = reason;
        }
    }
}
