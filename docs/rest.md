# Lightweight REST Endpoints

`cloops.microservices` is NATS-first, but it includes a small REST server for Kubernetes probes and webhook ingress. This is particularly use for 3p applications that want to invoke services on our platform.

REST endpoints are enabled by default. The SDK starts Kestrel on `REST_PORT` and maps built-in health endpoints plus any app endpoint classes in namespaces ending with `Rest`.

## Built-in endpoints

| Method | Path       | Auth   | Response                                                                                            |
| ------ | ---------- | ------ | --------------------------------------------------------------------------------------------------- |
| `GET`  | `/healthz` | Public | `200 OK` with `{ "status": "ok" }`                                                                  |
| `GET`  | `/readyz`  | Public | `200 OK` with `{ "ready": true }` when NATS is connected and configured TigerBeetle is reachable, otherwise `503` with `{ "ready": false, "reason": "..." }` |

`/readyz` is designed to be cheap to call. The NATS check is an in-memory connection-state read. The TigerBeetle check reads a cached L1-only probe result — the actual RPC to TigerBeetle runs in the background every ~4 minutes (see `TigerBeetleReadinessCacheService`), not on every request.

## Configuration

| Variable                | Default | Description                                                    |
| ----------------------- | ------- | -------------------------------------------------------------- |
| `ENABLE_REST_ENDPOINTS` | `True`  | Starts the lightweight REST server when enabled                |
| `REST_PORT`             | `8080`  | Port Kestrel listens on                                        |
| `REST_API_SECRET`       | None    | Shared secret required by endpoints marked `RestAuth.Required` |

## Adding endpoints

Create a class in a namespace ending with `Rest` and mark public methods with `[RestEndpoint]`.

```csharp
using CLOOPS.microservices;
using Microsoft.AspNetCore.Http;

namespace my.app.Rest;

public class WebhookEndpoints
{
    [RestEndpoint(RestHttpMethod.Post, "/webhooks/vendor", RestAuth.Required)]
    public async Task<IResult> VendorWebhook(HttpContext context, CancellationToken ct)
    {
        var payload = await context.Request.ReadFromJsonAsync<VendorPayload>(ct);

        // Call your service here.

        return Results.Accepted();
    }
}
```

REST endpoint classes support the same constructor injection style as services and controllers.

## Authentication

Every app endpoint must choose its auth behavior in the attribute:

```csharp
[RestEndpoint(RestHttpMethod.Get, "/public-status", RestAuth.Public)]
[RestEndpoint(RestHttpMethod.Post, "/webhooks/vendor", RestAuth.Required)]
```

For `RestAuth.Required`, set `REST_API_SECRET` and send either header:

```http
Authorization: Bearer <REST_API_SECRET>
```

or:

```http
X-CLOOPS-REST-SECRET: <REST_API_SECRET>
```

If any endpoint requires auth and `REST_API_SECRET` is missing, the SDK fails startup instead of exposing the endpoint.

## Supported method shape

Supported parameters:

- `HttpContext`
- `CancellationToken`
- no parameters

Supported return values:

- `IResult` or `Task<IResult>`
- object or `Task<object>` as JSON `200`
- `string` as text `200`
- `void` or `Task` as `204`

Keep REST handlers thin. They should parse HTTP input, call services, and return an HTTP result.
