# Strong Schema Architecture

## Overview

A strong, centralized schema is one of the most important foundations for building reliable microservices. Having strongly typed schemas for every message passed between microservices reduces potential errors due to loose types and weak contracts.

**Critical Principle**: You need to create your own **central schema repository** that all your microservices reference. This ensures a single source of truth for all message contracts across your system.

## Why Schema Matters

Having a strong schema helps you:

- **Avoid runtime errors** because of malformed messages/events
- **Document your interfaces** easily and enforce a strong governance process on changing interfaces
- **Implement input validation** of messages so that your handlers **never** receive invalid messages
- **Better developer experience** with IntelliSense and compile-time type checking
- **Lock down subjects** with specific message types, preventing accidental misuse

## Creating Your Own Central Schema Repository

You **must** create your own central schema repository that all your microservices reference. This repository should:

1. **Define all message types** used across your microservices
2. **Define subject builders** that enforce type-safe subject construction
3. **Be versioned and shared** as a NuGet package (or similar) across all services
4. **Enforce governance** - all schema changes go through this central repository

### Reference Implementation

For a complete working example of how to structure your schema repository, see the [cloops.nats schema examples](https://github.com/connectionloops/cloops.nats/tree/main/examples/schema). This example demonstrates:

- How to structure your schema repository
- How to implement `BaseMessage` with validation
- How to create type-safe subject builders
- How to organize messages, subject types, and builders
- Complete working examples you can adapt for your own schema

The example shows the architecture and patterns you should follow when building your own central schema repository.

> While this example is not a SDK project, but it is strongly recommended that you create an SDK out of your schema and have all your microservices depend on it. That way every service uses the same schema definition.

Within Connection Loops, the schema package we use is called as `cloops.nats.schema`.

## One Message Per Subject: The Key to Stability

This schema architecture follows a critical design principle: **one message type per subject**. This approach provides several stability benefits:

### Type Safety at Compile Time

- Each subject is strongly typed to a specific message class
- The compiler prevents you from accidentally publishing the wrong message type to a subject
- IntelliSense provides autocomplete and type checking when building subjects

### Runtime Safety

- No runtime payload parsing errors on the consumer side
- Deserialization failures are caught early with clear error messages
- Handlers can trust that the message type matches the subject

### Clear Contract Definition

- The relationship between subjects and message types is explicit and discoverable
- Subject builders enforce the correct event-to-subject mapping
- Changes to message structure are immediately visible across all services

## Subject Types

Your schema should support three types of NATS subjects, each serving a specific communication pattern:

### **P_Subject** - Publish Subject

- **Purpose**: Core NATS publishing for fire-and-forget messaging
- **Use Case**: When you need to broadcast events without expecting a response
- **Example**: Publishing events, notifications, or logging messages
- **Method**: `Publish(T payload)` - validates and publishes the message

```csharp
var subject = client.Subjects().Example().P_SavePerson(person.Id);
await subject.Publish(person); // Validates before publishing
```

### **S_Subject** - Stream Subject (JetStream)

- **Purpose**: JetStream publishing for durable, persistent messaging
- **Use Case**: When you need message persistence, delivery tracking, and guaranteed delivery
- **Features**: Supports deduplication, message acknowledgment, and stream-based delivery
- **Method**: `StreamPublish(T payload, ...)` - validates and publishes with JetStream guarantees

```csharp
var subject = client.Subjects().Example().S_ProcessOrder(order.Id);
await subject.StreamPublish(order); // Validates before stream publishing
```

### **R_Subject** - Request-Reply Subject

- **Purpose**: Request-response pattern for synchronous communication
- **Use Case**: When you need to send a request and wait for a specific reply
- **Pattern**: Request-reply is only applicable in core NATS (not JetStream)
- **Method**: `Request(Q payload)` - validates the request and returns the reply

```csharp
var subject = client.Subjects().Example().R_GetPerson(personId);
var reply = await subject.Request(request); // Validates before sending
```

**Note**: All subject names should start with their type prefix (P, S, or R) when defined in subject builders.

## Automatic Message Validation

Messages with a `Validate()` method are automatically validated before processing. Invalid messages are never sent to your handlers.

### How It Works

All messages should inherit from a `BaseMessage` class, which provides a `Validate()` method that uses Data Annotations to validate message properties. See the [reference implementation](https://github.com/connectionloops/cloops.nats/tree/main/examples/schema) for a complete examples:

```csharp
public abstract class BaseMessage
{
    public void Validate()
    {
        var validationContext = new ValidationContext(this);
        var validationResults = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(
            this, validationContext, validationResults,
            validateAllProperties: true
        );

        if (!isValid)
        {
            var errorMessages = validationResults
                .Select(r => r.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            throw new ValidationException(
                $"Message validation failed: {string.Join("; ", errorMessages)}"
            );
        }
    }
}
```

### Validation on Publish

All publishing methods (`Publish`, `StreamPublish`, `Request`) should automatically validate messages before sending:

```csharp
// P_Subject - Core NATS publishing
await subject.Publish(person); // Validates before publishing

// S_Subject - JetStream publishing
await subject.StreamPublish(person); // Validates before publishing

// R_Subject - Request-Reply
await subject.Request(person); // Validates before sending
```

If validation fails, a `ValidationException` should be thrown **before** the message is sent, preventing invalid data from entering your system.

### Defining Validation Rules

Use Data Annotations to define validation rules on your message classes:

```csharp
public class Person : BaseMessage
{
    [Required(ErrorMessage = "Id is required")]
    public string Id { get; set; } = "";

    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; } = "";

    [Range(0, 150, ErrorMessage = "Age must be between 0 and 150")]
    public int Age { get; set; } = 0;

    [Required(ErrorMessage = "Address is required")]
    public string Addr { get; set; } = "";
}
```

### Validation on Consumption

When messages are consumed by handlers, they should be automatically validated during deserialization. If a message fails validation:

- The validation exception is caught and logged
- The message is **not** delivered to your handler
- You can configure retry or dead-letter queue behavior as needed

This ensures your handlers **never** receive invalid messages, eliminating the need for defensive validation code in every handler.

> This functionality is taken care by `cloops.nats`, the core SDK on which this `cloops.microservices` SDK is based on.

## Architecture Overview

Your schema repository should be organized as follows:

```
Messages/
  ├── BaseMessage.cs      # Base class with Validate() method
  └── Person.cs           # Example message with validation attributes

SubjectTypes/
  ├── P_Subject.cs        # Core NATS publishing (validates on publish)
  ├── S_Subject.cs        # JetStream publishing (validates on publish)
  └── R_Subject.cs        # Request-Reply (validates on request)

SubjectBuilders/
  └── ExampleSubjectBuilder.cs  # Type-safe subject construction

Extensions/
  └── CloopsNatsClientExtension.cs  # Extension methods for subject builders
```

For a complete working example of this structure, see the [cloops.nats schema examples](https://github.com/connectionloops/cloops.nats/tree/main/examples/schema).

## Example Usage

### Type-Safe Subject Construction

```csharp
// Subject builder ensures type safety
var personSaveSubject = client.Subjects().Example().P_SavePerson(person.Id);
// ✅ Can only publish Person to this subject
await personSaveSubject.Publish(person);

// ❌ Compiler error if you try to publish wrong type
// await personSaveSubject.Publish(wrongMessage); // Won't compile!
```

### Complete Example

```csharp
// 1. Create a validated message
var person = new Person
{
    Id = "123",
    Name = "John Doe",
    Age = 30,
    Addr = "123 Main St"
};

// 2. Get type-safe subject from builder
var subject = client.Subjects().Example().P_SavePerson(person.Id);

// 3. Publish (automatically validates)
try
{
    await subject.Publish(person); // ✅ Valid message - publishes successfully
}
catch (ValidationException ex)
{
    // ❌ Invalid message - caught before publishing
    Console.WriteLine($"Validation failed: {ex.Message}");
}

// 4. Handler receives validated message
[NatsConsumer("test.persons.*.save")]
public Task<NatsAck> HandleSavePerson(NatsMsg<Person> msg, CancellationToken ct = default)
{
    // ✅ msg.Data is guaranteed to be valid - no need to check!
    var person = msg.Data;
    // Process the person...
    return Task.FromResult(new NatsAck(true));
}
```

## Best Practices

1. **Create a central schema repository** - All microservices must reference the same schema package
2. **Always inherit from `BaseMessage`** - This ensures all messages have validation capabilities
3. **Use Data Annotations liberally** - Define clear validation rules for all required fields
4. **One message per subject** - Maintain type safety and clear contracts
5. **Use Subject Builders** - Never construct subjects manually; use builders for type safety
6. **Trust the validation** - Handlers can assume messages are valid; no need for defensive checks
7. **Follow naming conventions** - Subject names should start with their type prefix (P, S, or R)
8. **Version your schema** - Use semantic versioning and ensure all services stay in sync

## Getting Started

To implement strong schema architecture in your system:

1. **Create a new repository** for your central schema
2. **Study the reference implementation** at [cloops.nats schema examples](https://github.com/connectionloops/cloops.nats/tree/main/examples/schema)
3. **Adapt the patterns** to your own domain and message types
4. **Package and distribute** your schema as a NuGet package (or similar)
5. **Reference the schema** in all your microservices
6. **Enforce governance** - all schema changes must go through the central repository

## Summary

This schema architecture provides:

- ✅ **Compile-time type safety** through subject builders
- ✅ **Runtime validation** before messages enter the system
- ✅ **Handler confidence** - handlers never see invalid messages
- ✅ **Clear contracts** - one message type per subject
- ✅ **Easy documentation** - schema serves as living documentation
- ✅ **Governance** - schema changes are visible and reviewable
- ✅ **Centralized control** - single source of truth for all message contracts

By following this pattern and creating your own central schema repository, you build microservices that are more stable, easier to maintain, and less prone to runtime errors.

For complete working examples and implementation details, refer to the [cloops.nats schema examples](https://github.com/connectionloops/cloops.nats/tree/main/examples/schema).
