# Making Database Calls

The `cloops.microservices` framework provides a `DB` class for executing SQL queries against SQL Server databases. The database operations support streaming results, parameterized queries, and flexible result mapping. It encourages writing raw sql queries for highest performance over using something like EntityFramework. It is highly performant and very lean.

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
- [Basic Query Execution](#basic-query-execution)
- [Parameterized Queries](#parameterized-queries)
- [Streaming Results](#streaming-results)
- [Result Type Mapping](#result-type-mapping)
  - [Strongly Typed Objects](#strongly-typed-objects)
  - [String Results](#string-results)
  - [JsonObject Results](#jsonobject-results)
  - [Atomic Types](#atomic-types)
- [Advanced Features](#advanced-features)
  - [SQL Scripts with GO Statements](#sql-scripts-with-go-statements)
  - [Info Message Callbacks](#info-message-callbacks)
  - [Command Timeout](#command-timeout)
  - [Cancellation Support](#cancellation-support)
- [Helper Methods](#helper-methods)
- [Best Practices](#best-practices)

## Overview

The `DB` class provides asynchronous database operations with the following key features:

- **Streaming results**: Process rows one at a time without loading everything into memory
- **Type mapping**: Automatically maps SQL results to C# objects
- **Parameterized queries**: Safe parameter binding to prevent SQL injection
- **Flexible return types**: Support for strongly typed objects, strings, JsonObject, and atomic types
- **SQL script execution**: Execute multi-batch SQL scripts with `GO` separators

## Getting Started

The `DB` class is typically injected into your services through dependency injection. It requires a SQL Server connection string.

```csharp
// DB is typically injected via dependency injection
// In your service constructor:
public class MyService
{
    private readonly IDB _db;

    public MyService(IDB db)
    {
        _db = db;
    }
}
```

## Basic Query Execution

The simplest way to execute a query is using `ExecuteReadAsync`. This method returns an `IAsyncEnumerable<T>`, which allows you to process results as they arrive.

### Simple SELECT Query

```csharp
string query = @"
    SELECT
        jobId,
        jobHttpMethod,
        jobUrl,
        jobPayload
    FROM jobs2 WITH (NOLOCK)
    WHERE jobStatus = @scheduled
";

var parameters = new SqlParameter[] {
    new SqlParameter("@scheduled", JobStatus.Scheduled)
};

await foreach (var job in _db.ExecuteReadAsync<Job>(query, parameters, cancellationToken: stoppingToken))
{
    // Process each job as it arrives
    await ProcessJob(job);
}
```

## Parameterized Queries

Always use parameterized queries to prevent SQL injection attacks. Parameters are passed as an array of `SqlParameter` objects.

### Using SqlParameter Array

```csharp
string query = @"
    UPDATE jobs2
    SET jobStatus = GREATEST(@status, jobStatus),
        updated_at = SYSDATETIMEOFFSET()
    WHERE jobId IN (@jobId1, @jobId2, @jobId3)
";

var parameters = new SqlParameter[]
{
    new SqlParameter("@status", status),
    new SqlParameter("@jobId1", jobIds[0]),
    new SqlParameter("@jobId2", jobIds[1]),
    new SqlParameter("@jobId3", jobIds[2])
};

await _db.ExecuteReadAsync<string>(query, parameters, cancellationToken: cancellationToken)
    .ToArrayAsync();
```

### Using the `pars` Helper Method

For convenience, you can use the static `DB.pars()` helper method to create parameters from tuples:

```csharp
string query = @"
    SELECT * FROM users
    WHERE email = @email AND status = @status
";

var parameters = DB.pars(
    ("@email", userEmail),
    ("@status", "active")
);

await foreach (var user in _db.ExecuteReadAsync<User>(query, parameters))
{
    // Process user
}
```

### Dynamic Parameter Lists

For dynamic lists (like IN clauses), you can build the query string dynamically while still using parameters for values:

```csharp
var query = $@"
    UPDATE jobs2
    SET jobStatus = GREATEST(@status, jobStatus),
        updated_at = SYSDATETIMEOFFSET()
    WHERE jobId IN ({string.Join(",", jobIds.Select(id => $"'{id}'"))})
";

var parameters = new SqlParameter[] {
    new SqlParameter("@status", status)
};

await _db.ExecuteReadAsync<string>(query, parameters, cancellationToken: cancellationToken)
    .ToArrayAsync();
```

## Streaming Results

One of the key features of `ExecuteReadAsync` is that it streams results row-by-row. This means you can process large result sets without loading everything into memory at once.

### Processing Results One at a Time

```csharp
string query = @"
    SELECT jobId, jobUrl, jobPayload
    FROM jobs2 WITH (NOLOCK)
    WHERE expectedExecutionAt <= SYSDATETIMEOFFSET()
";

var parameters = new SqlParameter[] {
    new SqlParameter("@scheduled", JobStatus.Scheduled)
};

// Process each row as it arrives from the database
await foreach (var job in _db.ExecuteReadAsync<RunnableJob>(
    query,
    parameters,
    cancellationToken: stoppingToken))
{
    // Process immediately - don't wait for all rows
    await ExecuteJob(job);
}
```

### Collecting All Results

If you need all results at once, you can use `ToArrayAsync()`:

```csharp
var allJobs = await _db.ExecuteReadAsync<Job>(query, parameters)
    .ToArrayAsync();

// Now allJobs is an array of all results
foreach (var job in allJobs)
{
    // Process job
}
```

### Batch Processing

You can also process results in batches:

```csharp
const int batchSize = 100;
var batch = new List<Job>();

await foreach (var job in _db.ExecuteReadAsync<Job>(query, parameters))
{
    batch.Add(job);

    if (batch.Count >= batchSize)
    {
        await ProcessBatch(batch);
        batch.Clear();
    }
}

// Process remaining items
if (batch.Count > 0)
{
    await ProcessBatch(batch);
}
```

## Result Type Mapping

The `ExecuteReadAsync<T>` method supports multiple return types. The framework automatically maps SQL columns to your object properties.

### Strongly Typed Objects

The most common use case is mapping to strongly typed C# objects. Column names are matched to property names (case-insensitive).

```csharp
public class RunnableJob
{
    public string JobId { get; set; }
    public string JobHttpMethod { get; set; }
    public string JobUrl { get; set; }
    public string JobPayload { get; set; }
    public int MaxRetries { get; set; }
    public int RetryCooloffMs { get; set; }
}

string query = @"
    SELECT
        jobId,
        jobHttpMethod,
        jobUrl,
        jobPayload,
        maxRetries,
        retryCooloffMs,
        failureCallbackHttpMethod,
        failureCallbackUrl,
        failureCallbackPayload
    FROM jobs2 WITH (NOLOCK)
    WHERE expectedExecutionAt <= SYSDATETIMEOFFSET()
        AND jobStatus = @scheduled
";

var parameters = new SqlParameter[] {
    new SqlParameter("@scheduled", JobStatus.Scheduled)
};

return _db.ExecuteReadAsync<RunnableJob>(query, parameters, cancellationToken: stoppingToken);
```

**Note**: Property names are matched case-insensitively to column names, so `JobId` matches `jobId`, `JOBID`, etc.

### String Results

For simple queries that return a single string value per row:

```csharp
string query = "SELECT name FROM users WHERE id = @id";
var parameters = new SqlParameter[] { new SqlParameter("@id", userId) };

await foreach (var name in _db.ExecuteReadAsync<string>(query, parameters))
{
    Console.WriteLine(name);
}
```

### JsonObject Results

For dynamic or schema-less queries, you can use `JsonObject`:

```csharp
string query = @"
    SELECT * FROM users
    WHERE department = @dept
";

var parameters = new SqlParameter[] {
    new SqlParameter("@dept", "Engineering")
};

await foreach (var user in _db.ExecuteReadAsync<JsonObject>(query, parameters))
{
    // Access properties dynamically
    var name = user["name"]?.ToString();
    var email = user["email"]?.ToString();

    // JSON columns are automatically parsed
    var metadata = user["metadata"]; // Already a JsonNode if column was JSON
}
```

### Atomic Types

You can also return atomic types (int, bool, DateTime, etc.):

```csharp
// Get count
string query = "SELECT COUNT(*) FROM jobs WHERE status = @status";
var parameters = new SqlParameter[] { new SqlParameter("@status", "active") };

await foreach (var count in _db.ExecuteReadAsync<int>(query, parameters))
{
    Console.WriteLine($"Total jobs: {count}");
}
```

Supported atomic types include:

- Numeric: `int`, `long`, `float`, `double`, `decimal`, `byte`, `short`, etc.
- Boolean: `bool`
- Date/Time: `DateTime`, `DateTimeOffset`, `TimeSpan`
- Other: `string`, `char`, `Guid`
- All nullable versions of the above

## Advanced Features

### SQL Scripts with GO Statements

For executing multi-batch SQL scripts (like migration scripts), use `ExecuteSQLScriptWithGo`:

```csharp
string sqlScript = @"
    CREATE TABLE IF NOT EXISTS users (
        id INT PRIMARY KEY,
        name NVARCHAR(100)
    );
    GO

    CREATE INDEX idx_name ON users(name);
    GO

    INSERT INTO users VALUES (1, 'John');
";

var results = await _db.ExecuteSQLScriptWithGo(
    sqlScript,
    infoMessageCallback: msg => Console.WriteLine($"SQL: {msg}"),
    timeout: 600,
    cancellationToken: cancellationToken
);

// results contains string outputs from each batch
```

**Note**: The method automatically splits the script on `GO` statements (case-insensitive) and executes each batch separately.

### Info Message Callbacks

You can capture informational messages from SQL Server:

```csharp
string query = "PRINT 'Processing started'; SELECT * FROM jobs";

await foreach (var job in _db.ExecuteReadAsync<Job>(
    query,
    parameters: null,
    infoMessageCallback: msg => Console.WriteLine($"SQL Info: {msg}")))
{
    // Process jobs
}
```

### Command Timeout

Set a custom timeout for long-running queries:

```csharp
// Default timeout is 30 seconds
// For long-running queries, increase the timeout
string query = "EXEC LongRunningStoredProcedure";

await foreach (var result in _db.ExecuteReadAsync<Result>(
    query,
    parameters: null,
    timeout: 300)) // 5 minutes
{
    // Process results
}
```

### Cancellation Support

All methods support cancellation tokens:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    await foreach (var item in _db.ExecuteReadAsync<Item>(
        query,
        parameters,
        cancellationToken: cts.Token))
    {
        // Process item
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Query was cancelled");
}
```

## Helper Methods

### `DB.pars()` - Parameter Builder

The static `pars()` method provides a convenient way to create parameter arrays:

```csharp
// Instead of:
var parameters = new SqlParameter[]
{
    new SqlParameter("@name", "John"),
    new SqlParameter("@age", 30),
    new SqlParameter("@active", true)
};

// You can write:
var parameters = DB.pars(
    ("@name", "John"),
    ("@age", 30),
    ("@active", true)
);
```

**Note**: `null` values are automatically converted to `DBNull.Value`.

## Best Practices

1. **Always use parameterized queries**: Never concatenate user input directly into SQL strings

   ```csharp
   // ❌ BAD - SQL injection risk
   var query = $"SELECT * FROM users WHERE name = '{userName}'";

   // ✅ GOOD - Safe parameterized query
   var query = "SELECT * FROM users WHERE name = @name";
   var parameters = new SqlParameter[] { new SqlParameter("@name", userName) };
   ```

2. **Use streaming for large result sets**: Process rows as they arrive instead of loading everything into memory

   ```csharp
   // ✅ GOOD - Streams results
   await foreach (var item in _db.ExecuteReadAsync<Item>(query))
   {
       await ProcessItem(item);
   }

   // ⚠️ Use with caution - loads all into memory
   var allItems = await _db.ExecuteReadAsync<Item>(query).ToArrayAsync();
   ```

3. **Handle null values**: Properties in your result objects should be nullable if the database column can be NULL

   ```csharp
   public class User
   {
       public string Name { get; set; }
       public string? Email { get; set; } // Nullable if column can be NULL
       public DateTime? LastLogin { get; set; } // Nullable DateTime
   }
   ```

4. **Use appropriate timeouts**: Set longer timeouts for queries that are expected to take a long time

   ```csharp
   // For data migration or bulk operations
   await _db.ExecuteReadAsync<Result>(query, timeout: 600); // 10 minutes
   ```

5. **Leverage cancellation tokens**: Always pass cancellation tokens to support graceful shutdown

   ```csharp
   await foreach (var item in _db.ExecuteReadAsync<Item>(
       query,
       cancellationToken: stoppingToken))
   {
       // Process item
   }
   ```

6. **Use WITH (NOLOCK) for read-only queries**: When reading data that doesn't need to be transactionally consistent, use `WITH (NOLOCK)` to avoid blocking

   ```csharp
   string query = "SELECT * FROM jobs2 WITH (NOLOCK) WHERE status = @status";
   ```

7. **Match property names to column names**: Property names are matched case-insensitively, but keeping them similar improves readability
   ```csharp
   // SQL column: jobId
   // C# property: JobId ✅ or jobId ✅ or JOBID ✅
   ```

## Summary

The `DB` class provides:

- ✅ Streaming results for efficient memory usage
- ✅ Automatic type mapping to C# objects
- ✅ Parameterized queries for security
- ✅ Support for multiple return types (objects, strings, JsonObject, atomic types)
- ✅ SQL script execution with GO statements
- ✅ Info message callbacks
- ✅ Configurable timeouts
- ✅ Cancellation support

By following these patterns and best practices, you can efficiently and safely interact with your SQL Server database in your microservices.

---

[Back to documentation index](./README.md)
