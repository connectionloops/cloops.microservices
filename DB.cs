using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace CLOOPS.microservices;

/// <summary>
/// Defines the functionality required to execute SQL commands against the application's database.
/// </summary>
public interface IDB
{
    /// <summary>
    /// Gets the connection string used for SQL connections.
    /// </summary>
    public string cnstr { get; }
    /// <summary>
    /// Executes a SQL query asynchronously and streams the result set as strongly typed objects.
    /// </summary>
    /// <typeparam name="T">The result type that each row is mapped to.</typeparam>
    /// <param name="query">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameters to bind to the SQL command.</param>
    /// <param name="infoMessageCallback">Callback used to surface SQL Server informational messages.</param>
    /// <param name="timeout">Command timeout in seconds.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A streamed sequence of results of type <typeparamref name="T"/>.</returns>
    IAsyncEnumerable<T> ExecuteReadAsync<T>(
        string query,
        SqlParameter[]? parameters = null,
        Action<string>? infoMessageCallback = null,
        int timeout = 30,
        CancellationToken cancellationToken = default
    ) where T : class;

    /// <summary>
    /// Executes a SQL script that contains <c>GO</c> batch separators.
    /// </summary>
    /// <param name="sqlScript">The full SQL script to execute.</param>
    /// <param name="infoMessageCallback">Callback used to surface SQL Server informational messages.</param>
    /// <param name="timeout">Command timeout in seconds for each batch.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of string results produced by the executed batches.</returns>
    Task<List<string>> ExecuteSQLScriptWithGo(string sqlScript, Action<string>? infoMessageCallback = null, int timeout = 600, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides concrete implementations for executing SQL commands against a SQL Server database.
/// </summary>
public class DB : IDB
{
    private string _cnstr;

    /// <summary>
    /// Initializes a new instance of the <see cref="DB"/> class with the provided connection string.
    /// </summary>
    /// <param name="cnstr">The SQL Server connection string.</param>
    public DB(string cnstr)
    {
        _cnstr = cnstr;
    }

    /// <inheritdoc/>
    public string cnstr
    {
        get
        {
            return _cnstr;
        }
    }

    /// <summary>
    /// Executes a data extracting SQL statement and streams the result one row at a time.
    /// </summary>
    /// <typeparam name="T">The result type that each row is mapped to.</typeparam>
    /// <param name="query">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameters to bind to the SQL command.</param>
    /// <param name="infoMessageCallback">Optional callback for handling <see cref="SqlConnection.InfoMessage"/> events.</param>
    /// <param name="timeout">Command timeout in seconds.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A streamed sequence of results of type <typeparamref name="T"/>.</returns>
    public async IAsyncEnumerable<T> ExecuteReadAsync<T>(
        string query,
        SqlParameter[]? parameters = null,
        Action<string>? infoMessageCallback = null,
        int timeout = 30,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) where T : class
    {
        using (var connection = new SqlConnection(_cnstr))
        {
            // Capture messages from the database server
            connection.InfoMessage += (sender, e) =>
            {
                if (infoMessageCallback != null)
                {
                    infoMessageCallback(e.Message);
                }
            };
            await connection.OpenAsync(cancellationToken);
            var command = new SqlCommand(query, connection);
            command.CommandTimeout = timeout;
            // Add parameters if any
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters);
            }
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                // caches
                var type = typeof(T);
                Dictionary<string, PropertyInfo> typePropertyCache = getPropertiesCache(type);
                var colSchema = await reader.GetColumnSchemaAsync(cancellationToken);

                // create items
                while (await reader.ReadAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = CreateInstance<T>(typeof(T));
                    foreach (var column in colSchema)
                    {

                        if (column.ColumnOrdinal is not null)
                        {
                            var columnValue = reader.GetValue(column.ColumnOrdinal.Value);
                            if (typePropertyCache.TryGetValue(column.ColumnName, out var cachedPropertyInfo))
                            {
                                AddValueToObject<T>(item, cachedPropertyInfo, columnValue);
                            }
                            else if (item is JsonObject jsonObject)
                            {
                                AddValueToJsonObject(jsonObject, column, columnValue);
                            }
                            else if (type == typeof(string))
                            {
                                item = (T)(object)(columnValue.ToString() ?? string.Empty);
                            }
                            else if (AtomicTypes.Contains(type))
                            {
                                item = (T)columnValue;
                            }
                        }
                    }
                    yield return item;
                }

            }
        }
    }

    /// <summary>
    /// Creates an array of <see cref="SqlParameter"/> instances from name-value tuples.
    /// </summary>
    /// <param name="sqlParams">Comma separated tuples. First item is the parameter name, second item is the parameter value.</param>
    /// <returns>An array of <see cref="SqlParameter"/> instances created from the provided tuples.</returns>
    public static SqlParameter[] pars(params (string, object?)[] sqlParams)
    {
        SqlParameter[] retval = new SqlParameter[sqlParams.Length];
        for (int i = 0; i < sqlParams.Length; i++)
        {
            retval[i] = new SqlParameter(sqlParams[i].Item1, sqlParams[i].Item2 ?? DBNull.Value);
        }
        return retval;
    }


    /// <summary>
    /// Executes a SQL script in batches separated by <c>GO</c> statements.
    /// </summary>
    /// <param name="sqlScript">The full SQL script to execute.</param>
    /// <param name="infoMessageCallback">Optional callback for handling <see cref="SqlConnection.InfoMessage"/> events.</param>
    /// <param name="timeout">Command timeout in seconds for each batch.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of string results produced by the executed batches.</returns>
    public async Task<List<string>> ExecuteSQLScriptWithGo(string sqlScript, Action<string>? infoMessageCallback = null, int timeout = 600, CancellationToken cancellationToken = default)
    {
        var batches = System.Text.RegularExpressions.Regex.Split(sqlScript, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var retval = new List<string>();
        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                var result = await ExecuteReadAsync<string>(trimmed, null, infoMessageCallback, timeout, cancellationToken).ToArrayAsync();
                retval.AddRange(result);
                Thread.Sleep(500); // Adding wait for server to process the command
            }
        }
        return retval;
    }

    #region utilityFunctions
    private Dictionary<string, PropertyInfo> getPropertiesCache(Type type)
    {
        Dictionary<string, PropertyInfo> typePropertyCache = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(JsonObject))
        {
            return typePropertyCache;
        }
        foreach (var prop in type.GetProperties())
        {
            typePropertyCache.Add(prop.Name, prop);
        }
        return typePropertyCache;
    }

    private T CreateInstance<T>(Type type) where T : class
    {
        if (type == typeof(JsonObject))
        {
            return (T)(object)new JsonObject();
        }
        else if (type == typeof(string))
        {
            return (T)(object)string.Empty;
        }
        else if (AtomicTypes.Contains(type))
        {
            return (T?)Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create an instance of {type}.");
        }
        else
        {
            return (T?)Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create an instance of {type}.");
        }
    }

    private void AddValueToJsonObject(JsonObject item, DbColumn column, object columnValue)
    {
        if (column.DataType == typeof(string))
        {
            var strValue = columnValue.ToString();
            if
            (
                strValue != null &&
                (
                    (strValue.StartsWith('{') && strValue.EndsWith('}'))
                    ||
                    (strValue.StartsWith('[') && strValue.EndsWith(']'))
                )
            )
            {
                item.Add(new KeyValuePair<string, JsonNode?>(column.ColumnName, JsonNode.Parse(strValue)));
                return;
            }
        }
        item.Add(new KeyValuePair<string, JsonNode?>(column.ColumnName, JsonValue.Create(columnValue)));
    }

    private void AddValueToObject<T>(T item, PropertyInfo pinfo, object columnValue)
    {
        if (AtomicTypes.Contains(pinfo.PropertyType) || pinfo.PropertyType.IsEnum)
        {
            pinfo.SetValue(item, columnValue == DBNull.Value ? null : columnValue);
        }
        else if (pinfo.PropertyType == typeof(JsonObject))
        {
            pinfo.SetValue(item, columnValue == DBNull.Value ? null : JsonNode.Parse(columnValue.ToString() ?? "{}"));
        }
        else if (pinfo.PropertyType.IsArray || (pinfo.PropertyType.IsGenericType && pinfo.PropertyType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var jarr = JsonNode.Parse(columnValue == DBNull.Value ? "[]" : (columnValue.ToString() ?? "[]"));
            pinfo.SetValue(item, jarr.Deserialize(pinfo.PropertyType, BaseUtil.JsonSerializerOptions));
        }
        else
        {
            var jobj = JsonNode.Parse(columnValue == DBNull.Value ? "{}" : (columnValue.ToString() ?? "{}"));
            pinfo.SetValue(item, jobj.Deserialize(pinfo.PropertyType, BaseUtil.JsonSerializerOptions));
        }
    }

    private static readonly HashSet<Type> AtomicTypes = new HashSet<Type>
    {
        typeof(string),
        typeof(int),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(char),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid),

        //nullables 
        typeof(int?),
        typeof(float?),
        typeof(double?),
        typeof(decimal?),
        typeof(bool?),
        typeof(byte?),
        typeof(sbyte?),
        typeof(short?),
        typeof(ushort?),
        typeof(uint?),
        typeof(long?),
        typeof(ulong?),
        typeof(char?),
        typeof(DateTime?),
        typeof(DateTimeOffset?),
        typeof(TimeSpan?),
        typeof(Guid?),
    };
}
#endregion
