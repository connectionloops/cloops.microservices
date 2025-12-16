# Utility Functions

Utility functions are common helper functions that you need to use throughout your application. The `cloops.microservices` framework provides a utility class hierarchy that allows you to use framework-provided utility functions and extend them with your own application-specific utilities.

## Class Hierarchy

The utility class structure follows an inheritance pattern:

```
BaseNatsUtil (from CLOOPS.NATS)
    ↓
BaseUtil (from CLOOPS.microservices)
    ↓
Util (your application-specific class)
```

Your application's `Util` class should inherit from `CLOOPS.microservices.BaseUtil`, which in turn inherits from `CLOOPS.NATS.BaseNatsUtil`. This allows you to access all framework utility functions while adding your own.

## Framework-Provided Utility Functions

### From BaseNatsUtil (CLOOPS.NATS)

These utility functions are available through the inheritance chain:

#### `JsonSerializerOptions`

A static property providing default JSON serializer options configured with:

- **Camel case naming policy**: Property names are serialized in camelCase
- **Case-insensitive property names**: Deserialization is case-insensitive
- **Number handling**: Allows reading numbers from strings
- **Enum converter**: Automatically converts enums to/from strings

**Usage:**

```csharp
var options = Util.JsonSerializerOptions;
// Use with JsonSerializer.Serialize/Deserialize if needed
```

#### `Serialize<T>(T? obj)`

Serializes an object to a JSON string using the default serializer options.

**Parameters:**

- `obj`: The object to serialize (can be null)

**Returns:**

- A JSON string representing the object, or `null` if the input object is null

**Example:**

```csharp
var person = new { Name = "John", Age = 30 };
string json = Util.Serialize(person);
// Result: {"name":"John","age":30}
```

#### `Deserialize<T>(string json)`

Deserializes a JSON string to an object of the specified type.

**Parameters:**

- `json`: The JSON string to deserialize

**Returns:**

- An object of type `T`, or `null` if deserialization fails

**Example:**

```csharp
string json = "{\"name\":\"John\",\"age\":30}";
var person = Util.Deserialize<Person>(json);
// Returns a Person object with Name="John" and Age=30
```

### From BaseUtil (CLOOPS.microservices)

#### `GetCronExpression(string cron)`

Parses a cron expression and returns a `CronExpression` object from the Cronos library.

**Parameters:**

- `cron`: The cron expression string (supports both 5-field and 6-field formats)

**Returns:**

- A `CronExpression` object that can be used to calculate next occurrences

**Throws:**

- `Exception` if the cron expression is invalid

**Example:**

```csharp
// 5-field format (standard): minute hour day month weekday
var cronExpr = Util.GetCronExpression("0 9 * * *"); // Every day at 9 AM

// 6-field format (with seconds): second minute hour day month weekday
var cronExpr2 = Util.GetCronExpression("0 0 9 * * *"); // Every day at 9:00:00 AM

// Calculate next occurrence
var nextRun = cronExpr.GetNextOccurrence(DateTime.UtcNow);
```

**Note:** The method automatically detects whether the cron expression uses 5 fields (standard format) or 6 fields (includes seconds) and parses accordingly.

## Adding Your Own Utility Functions

Each service (application) can extend the `Util` class with its own utility functions. This is done by creating a `Util` class in your application that inherits from `CLOOPS.microservices.BaseUtil`.

### Basic Setup

Create a `Util.cs` file in your project:

```csharp
namespace your.namespace;

public class Util : CLOOPS.microservices.BaseUtil
{
    // Add your application-specific utility methods here
}
```

### Examples

Here are some common examples of utility functions you might add:

#### Example 1: String Formatting Utilities

```csharp
namespace your.namespace;

public class Util : CLOOPS.microservices.BaseUtil
{
    /// <summary>
    /// Formats a phone number to a standard format
    /// </summary>
    public static string FormatPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        // Remove all non-digit characters
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());

        // Format as (XXX) XXX-XXXX
        if (digits.Length == 10)
        {
            return $"({digits[0..3]}) {digits[3..6]}-{digits[6..10]}";
        }

        return phoneNumber;
    }

    /// <summary>
    /// Truncates a string to a maximum length with ellipsis
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? string.Empty;

        return text[..(maxLength - 3)] + "...";
    }
}
```

#### Example 2: Date/Time Utilities

```csharp
namespace your.namespace;

public class Util : CLOOPS.microservices.BaseUtil
{
    /// <summary>
    /// Converts UTC time to a specific timezone
    /// </summary>
    public static DateTime ConvertToTimezone(DateTime utcTime, string timezoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
    }

    /// <summary>
    /// Gets the start of the day for a given date
    /// </summary>
    public static DateTime StartOfDay(DateTime date)
    {
        return date.Date;
    }

    /// <summary>
    /// Gets the end of the day for a given date
    /// </summary>
    public static DateTime EndOfDay(DateTime date)
    {
        return date.Date.AddDays(1).AddTicks(-1);
    }
}
```

#### Example 3: Data Transformation Utilities

```csharp
namespace your.namespace;

public class Util : CLOOPS.microservices.BaseUtil
{
    /// <summary>
    /// Converts a dictionary to a query string
    /// </summary>
    public static string ToQueryString(Dictionary<string, string> parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return string.Empty;

        return "?" + string.Join("&",
            parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
    }

    /// <summary>
    /// Safely converts a string to an integer
    /// </summary>
    public static int? SafeParseInt(string value)
    {
        if (int.TryParse(value, out var result))
            return result;
        return null;
    }
}
```

#### Example 5: Using Framework Utilities in Your Custom Functions

You can combine framework utilities with your own logic:

```csharp
namespace your.namespace;

public class Util : CLOOPS.microservices.BaseUtil
{
    /// <summary>
    /// Serializes an object to JSON and then encodes it as Base64
    /// </summary>
    public static string SerializeToBase64<T>(T obj)
    {
        var json = Serialize(obj); // Uses inherited Serialize method
        if (json == null)
            return string.Empty;

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Deserializes a Base64-encoded JSON string
    /// </summary>
    public static T? DeserializeFromBase64<T>(string base64Json)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Json);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return Deserialize<T>(json); // Uses inherited Deserialize method
        }
        catch
        {
            return default(T);
        }
    }
}
```

## Usage in Your Application

Once you've created your `Util` class, you can use both framework and custom utilities throughout your application:

```csharp
// Using framework utilities
var json = Util.Serialize(myObject);
var cron = Util.GetCronExpression("0 9 * * *");

// Using your custom utilities
var formattedPhone = Util.FormatPhoneNumber("1234567890");
var isValid = Util.IsValidEmail("user@example.com");
```

## Best Practices

1. **Keep utilities stateless**: Utility functions should be static and not depend on instance state
2. **Add XML documentation**: Document your utility functions with `<summary>` tags for better IntelliSense support
3. **Handle null/edge cases**: Always validate inputs and handle null values appropriately
4. **Use meaningful names**: Choose clear, descriptive names for your utility functions
5. **Group related functions**: Consider organizing utilities into separate classes if you have many related functions (e.g., `StringUtil`, `DateTimeUtil`, etc.)
6. **Test your utilities**: Write unit tests for your custom utility functions to ensure they work correctly

## Summary

The utility class system provides:

- ✅ Access to framework-provided JSON serialization utilities
- ✅ Access to cron expression parsing utilities
- ✅ Ability to extend with your own application-specific utilities
- ✅ Consistent access pattern through a single `Util` class
- ✅ Inheritance-based organization for easy discovery

By following this pattern, you can build a comprehensive utility library that combines framework capabilities with your application's specific needs.

---

[Back to documentation index](./README.md)
