# 🧪 Testing Microservices

Now that you know how to build microservices, let's see how to do manual testing on your local environment.

## Overview

The simplest approach to testing NATS-based microservices is to use shell scripts (`.sh` files) containing NATS CLI commands. These scripts act as a lightweight "Postman collection" that you can run directly from your terminal to test your microservices locally.

## Prerequisites

Before you can test your microservices, you'll need:

1. **NATS CLI installed**: Make sure you have the NATS CLI tool installed on your system
   ```bash
   # Install NATS CLI (example for macOS)
   brew install nats-io/nats-tools/nats
   ```

2. **NATS server running**: Your local NATS server should be running and accessible
   ```bash
   # Check if NATS is running
   nats server check
   ```

3. **jq installed** (optional but recommended): For pretty-printing JSON responses
   ```bash
   # Install jq (example for macOS)
   brew install jq
   ```

## Creating Test Scripts

Create a `nats/` directory in your microservice project root to organize your test scripts. Each script should test a specific endpoint or functionality.

### Basic Structure

Here's the basic structure of a test script:

```bash
#!/usr/bin/env bash
# usage: ./nats/health.sh
nats request <subject> <payload> | jq
```

### Example: Health Check

Let's look at a real example from the microservice template:

**File: `nats/health.sh`**
```bash
#!/usr/bin/env bash
# usage: ./nats/health.sh
nats request health.cloops.microservices.gh.template hello | jq
```

This script:
- Sends a request to the `health.cloops.microservices.gh.template` subject
- Passes `"hello"` as the payload (a simple string)
- Pipes the response through `jq` for formatted JSON output

**Corresponding Controller:**
```csharp
[NatsConsumer(_subject: "health.cloops.microservices.gh.template")]
public async Task<NatsAck> GetHealth(NatsMsg<string> msg, CancellationToken ct = default)
{
    // ... handler logic
    return new NatsAck(_isAck: true, _reply: reply);
}
```

## Testing Different Patterns

### Request-Reply Pattern

For request-reply endpoints, use the `nats request` command:

```bash
#!/usr/bin/env bash
# Test a request-reply endpoint
nats request <subject> <payload> | jq
```

**Example with JSON payload:**
```bash
#!/usr/bin/env bash
# usage: ./nats/get-person.sh
nats request person.get '{"id": "123"}' | jq
```

**Example with file payload:**
```bash
#!/usr/bin/env bash
# usage: ./nats/create-order.sh
nats request order.create "$(cat payloads/create-order.json)" | jq
```

### Publish Pattern

For publish-subscribe endpoints, use the `nats pub` command:

```bash
#!/usr/bin/env bash
# Test a publish endpoint
nats pub <subject> <payload>
```

**Example:**
```bash
#!/usr/bin/env bash
# usage: ./nats/publish-event.sh
nats pub events.user.created '{"userId": "123", "timestamp": "2024-01-01T00:00:00Z"}'
```

### Subscribe Pattern

To listen for messages on a subject (useful for debugging):

```bash
#!/usr/bin/env bash
# Listen to a subject
nats sub <subject> | jq
```

**Example:**
```bash
#!/usr/bin/env bash
# usage: ./nats/listen-events.sh
nats sub events.user.created | jq
```

## Best Practices

### 1. Organize Your Scripts

Keep all test scripts in a dedicated `nats/` directory:

```
your-microservice/
├── nats/
│   ├── health.sh
│   ├── get-person.sh
│   ├── create-order.sh
│   └── publish-event.sh
└── ...
```

### 2. Make Scripts Executable

Ensure your scripts are executable:

```bash
chmod +x nats/*.sh
```

### 3. Add Usage Comments

Include a usage comment at the top of each script:

```bash
#!/usr/bin/env bash
# usage: ./nats/health.sh
# description: Tests the health check endpoint
```

### 4. Use jq for JSON Formatting

Always pipe responses through `jq` for readable output:

```bash
nats request <subject> <payload> | jq
```

### 5. Handle Complex Payloads

For complex JSON payloads, consider:
- Using a separate `payloads/` directory for JSON files
- Using heredoc syntax for multi-line JSON
- Validating JSON before sending

**Example with heredoc:**
```bash
#!/usr/bin/env bash
nats request order.create <<EOF | jq
{
  "userId": "123",
  "items": [
    {"productId": "456", "quantity": 2}
  ]
}
EOF
```

### 6. Test Different Scenarios

Create multiple scripts for different test scenarios:

```bash
nats/
├── health.sh              # Basic health check
├── get-person-valid.sh    # Valid request
├── get-person-invalid.sh  # Invalid request (error handling)
└── get-person-missing.sh  # Missing data scenario
```

## Running Your Tests

### Run Individual Scripts

```bash
./nats/health.sh
```

### Run All Tests

Create a simple test runner:

```bash
#!/usr/bin/env bash
# usage: ./nats/run-all.sh
for script in nats/*.sh; do
    if [ "$(basename $script)" != "run-all.sh" ]; then
        echo "Running $(basename $script)..."
        $script
        echo ""
    fi
done
```

## Troubleshooting

### Connection Issues

If you get connection errors, check:
- NATS server is running: `nats server check`
- Correct NATS server URL (default: `nats://localhost:4222`)
- Network connectivity

### Subject Not Found

If you get "no responders available":
- Ensure your microservice is running
- Verify the subject name matches exactly
- Check that the consumer is properly registered

### JSON Parsing Errors

If `jq` fails:
- Validate your JSON payload is correct
- Check for special characters that need escaping
- Try without `jq` first to see raw output

## Advanced Testing

### Testing with Timeouts

```bash
#!/usr/bin/env bash
# Request with custom timeout (default is 5s)
nats request --timeout 10s <subject> <payload> | jq
```

### Testing with Headers

```bash
#!/usr/bin/env bash
# Request with headers
nats request --header "X-Request-ID: test-123" <subject> <payload> | jq
```

### Testing JetStream Subjects

For JetStream subjects, use the `nats js` commands:

```bash
#!/usr/bin/env bash
# Publish to a JetStream subject
nats js pub <subject> <payload>
```

## Integration with CI/CD

While these scripts are primarily for local manual testing, you can also use them in CI/CD pipelines:

```yaml
# Example GitHub Actions step
- name: Test microservice endpoints
  run: |
    chmod +x nats/*.sh
    ./nats/health.sh
    ./nats/get-person.sh
```

## Next Steps

- Learn about [observability](./logging.md) to monitor your microservices
- Check out [distributed locks](./distributed-locks.md) for advanced features
- Review [additional setup](./additional-setup.md) for production configurations

