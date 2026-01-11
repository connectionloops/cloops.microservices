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
- [Write Operations](#write-operations)
    - [ExecuteNonQueryAsync - INSERT/UPDATE/DELETE](#executenonqueryasync---insertupdatedelete)
    - [ExecuteScalarAsync - Single Value Queries](#executescalarasync---single-value-queries)
- [Transactions](#transactions)
    - [Basic Transaction Usage](#basic-transaction-usage)
    - [Transaction Methods](#transaction-methods)
    - [Transaction Best Practices](#transaction-best-practices)
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
- **Write operations**: Execute INSERT, UPDATE, DELETE with row count feedback
- **Scalar queries**: Efficiently retrieve single values (COUNT, MAX, etc.)
- **Transactions**: Atomic multi-step operations with commit/rollback
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

## Write Operations

### ExecuteNonQueryAsync - INSERT/UPDATE/DELETE

For INSERT, UPDATE, and DELETE operations that don't return result sets, use `ExecuteNonQueryAsync`. This method returns the number of affected rows, providing clear feedback and better performance than using `ExecuteReadAsync` for write operations.

#### Insert Example

```csharp
const string query = @"
    INSERT INTO templates (templateId, productId, name, content, createdAt)
    VALUES (@templateId, @productId, @name, @content, SYSDATETIMEOFFSET())
";

var parameters = DB.pars(
    ("@templateId", Guid.NewGuid()),
    ("@productId", product.ProductId),
    ("@name", template.Name),
    ("@content", template.Content)
);

int rowsAffected = await _db.ExecuteNonQueryAsync(query, parameters, cancellationToken: ct);

if (rowsAffected > 0)
{
    _logger.LogInformation("Template created successfully");
}
else
{
    _logger.LogWarning("Template was not created");
}
```

#### Update Example

```csharp
const string query = @"
    UPDATE jobs2
    SET jobStatus = @status,
        updated_at = SYSDATETIMEOFFSET()
    WHERE jobId = @jobId
";

var parameters = DB.pars(
    ("@jobId", jobId),
    ("@status", JobStatus.Completed)
);

int rowsAffected = await _db.ExecuteNonQueryAsync(query, parameters, cancellationToken: ct);

if (rowsAffected == 0)
{
    throw new NotFoundException($"Job {jobId} not found");
}
```

#### Delete Example

```csharp
const string query = @"
    DELETE FROM sessions
    WHERE sessionId = @sessionId
        AND createdAt < DATEADD(day, -30, GETDATE())
";

var parameters = DB.pars(("@sessionId", sessionId));

int rowsAffected = await _db.ExecuteNonQueryAsync(query, parameters, cancellationToken: ct);

_logger.LogInformation("Deleted {Count} expired sessions", rowsAffected);
```

**Benefits:**
- ✅ Clear semantic separation between read and write operations
- ✅ Returns meaningful row count for verification
- ✅ No need for async enumeration workaround
- ✅ Better performance (no unnecessary reader initialization)
- ✅ Supports optional info message callbacks and custom timeouts

### ExecuteScalarAsync - Single Value Queries

For queries that return a single value (like COUNT, MAX, SUM, SCOPE_IDENTITY, etc.), use `ExecuteScalarAsync<T>`. This is more efficient and cleaner than `ExecuteReadAsync` for scalar operations.

#### Count Example

```csharp
const string query = "SELECT COUNT(*) FROM users WHERE status = @status";
var parameters = DB.pars(("@status", "active"));

int activeUserCount = await _db.ExecuteScalarAsync<int>(query, parameters, cancellationToken: ct);
Console.WriteLine($"Active users: {activeUserCount}");
```

#### Get New ID After Insert

```csharp
const string query = @"
    INSERT INTO orders (customerId, amount, orderDate)
    VALUES (@customerId, @amount, GETDATE());
    
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
";

var parameters = DB.pars(
    ("@customerId", customerId),
    ("@amount", 99.99m)
);

long? newOrderId = await _db.ExecuteScalarAsync<long>(query, parameters, cancellationToken: ct);

if (newOrderId.HasValue)
{
    _logger.LogInformation("Order created with ID: {OrderId}", newOrderId);
}
```

#### Aggregate Functions

```csharp
// Get maximum salary
const string maxQuery = "SELECT MAX(salary) FROM employees WHERE department = @dept";
var maxParams = DB.pars(("@dept", "Engineering"));
decimal? maxSalary = await _db.ExecuteScalarAsync<decimal>(maxQuery, maxParams, cancellationToken: ct);

// Get average
const string avgQuery = "SELECT AVG(salary) FROM employees WHERE department = @dept";
var avgParams = DB.pars(("@dept", "Engineering"));
decimal? avgSalary = await _db.ExecuteScalarAsync<decimal>(avgQuery, avgParams, cancellationToken: ct);
```

#### Existence Check

```csharp
const string query = @"
    SELECT CAST(CASE 
        WHEN EXISTS(SELECT 1 FROM users WHERE email = @email) 
        THEN 1 
        ELSE 0 
    END AS BIT)
";

var parameters = DB.pars(("@email", userEmail));
bool userExists = await _db.ExecuteScalarAsync<bool>(query, parameters, cancellationToken: ct);

if (userExists)
{
    throw new InvalidOperationException("User already exists");
}
```

**Benefits:**
- ✅ Efficient for single value queries
- ✅ Cleaner code than ExecuteReadAsync for scalar operations
- ✅ Automatic type conversion with support for nullable types
- ✅ Returns default(T) if query returns NULL

## Transactions

For atomic multi-step operations where all changes must succeed or fail together, use `BeginTransactionAsync` to create a transaction.

### Basic Transaction Usage

```csharp
await using var transaction = await _db.BeginTransactionAsync(ct);
try
{
    // Insert template
    const string insertQuery = @"
        INSERT INTO templates (templateId, productId, name, content)
        VALUES (@templateId, @productId, @name, @content)
    ";
    
    var insertParams = DB.pars(
        ("@templateId", templateId),
        ("@productId", product.ProductId),
        ("@name", template.Name),
        ("@content", template.Content)
    );
    
    int rowsInserted = await transaction.ExecuteNonQueryAsync(insertQuery, insertParams, cancellationToken: ct);
    
    if (rowsInserted == 0)
        throw new InvalidOperationException("Failed to insert template");
    
    // Insert related mappings
    foreach (var mapping in template.Mappings)
    {
        const string mappingQuery = @"
            INSERT INTO template_mappings (templateId, fragmentId, position)
            VALUES (@templateId, @fragmentId, @position)
        ";
        
        var mappingParams = DB.pars(
            ("@templateId", templateId),
            ("@fragmentId", mapping.FragmentId),
            ("@position", mapping.Position)
        );
        
        await transaction.ExecuteNonQueryAsync(mappingQuery, mappingParams, cancellationToken: ct);
    }
    
    // Commit all changes
    await transaction.CommitAsync(ct);
    _logger.LogInformation("Template and mappings created successfully");
}
catch (Exception ex)
{
    await transaction.RollbackAsync(ct);
    _logger.LogError(ex, "Failed to create template and mappings");
    throw;
}
```

### Transaction Methods

The `IDBTransaction` interface provides the following methods:

- **`ExecuteNonQueryAsync()`** - Execute INSERT, UPDATE, DELETE operations within the transaction
- **`ExecuteReadAsync<T>()`** - Execute SELECT queries and stream results within the transaction
- **`CommitAsync()`** - Commit all changes made in the transaction
- **`RollbackAsync()`** - Rollback all changes made in the transaction
- **`DisposeAsync()`** - Cleanup resources (called automatically with `await using`)

### Transaction Best Practices

1. **Always use `await using`** for automatic cleanup:
   ```csharp
   await using var transaction = await _db.BeginTransactionAsync(ct);
   ```

2. **Keep transactions as short as possible** to minimize database locking.

3. **Wrap transaction code in try-catch** and rollback on error:
   ```csharp
   await using var transaction = await _db.BeginTransactionAsync(ct);
   try
   {
       // ... operations ...
       await transaction.CommitAsync(ct);
   }
   catch
   {
       await transaction.RollbackAsync(ct);
       throw;
   }
   ```

4. **Use transactions only when atomicity is required**.

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

8. **Verify row counts for critical operations**:

    ```csharp
    int rowsAffected = await _db.ExecuteNonQueryAsync(updateQuery, params, ct);
   
    if (rowsAffected == 0)
    {
         throw new NotFoundException("Record not found");
    }
    ```

9. **Use the right method for the job**:

    ```csharp
    // ✅ SELECT queries - use ExecuteReadAsync
    await foreach (var user in _db.ExecuteReadAsync<User>(selectQuery, params))
   
    // ✅ INSERT/UPDATE/DELETE - use ExecuteNonQueryAsync
    int rows = await _db.ExecuteNonQueryAsync(insertQuery, params, cancellationToken: ct);
   
    // ✅ Single value queries - use ExecuteScalarAsync
    int count = await _db.ExecuteScalarAsync<int>(countQuery, params, cancellationToken: ct);
   
    // ✅ Multiple related operations - use transactions
    await using var transaction = await _db.BeginTransactionAsync(ct);
    ```

## Summary

The `DB` class provides:

- ✅ Streaming results with `ExecuteReadAsync<T>` for efficient memory usage
- ✅ Non-query operations with `ExecuteNonQueryAsync` for INSERT/UPDATE/DELETE
- ✅ Scalar queries with `ExecuteScalarAsync<T>` for COUNT, MAX, etc.
- ✅ Transactions with `BeginTransactionAsync` for atomic operations
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
