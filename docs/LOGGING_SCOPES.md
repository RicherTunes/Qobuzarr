Logging Scopes and Correlation Keys

Why
- Consistent, structured keys in log scopes let us correlate events across async flows (search → API calls → queue → download) and speed up incident triage.

Standard Keys (in LoggingScopeExtensions)
- `correlationId`: top-level correlation across components; auto-generated if omitted
- `operationId`: logical operation name, e.g., Search, Download, RefreshToken
- `requestId`: external HTTP request id (Qobuz, host HTTP proxy)
- `sessionId`: auth/session identifier (Qobuz)
- `albumId`, `trackId`, `artistId`: domain identifiers when available
- `jobId`: orchestrator/queue job id
- `retryAttempt`: retry count for transient errors
- `rateLimitBucket`: limiter/partition that handled the call

Usage
- Wrap hot paths with `BeginOperationScope` and add data you have at that layer.
  - API request handler: `operationId=Search`, `requestId`, `retryAttempt`, `rateLimitBucket`
  - Download orchestrator: `operationId=Download`, `albumId`, `trackId`, `jobId`
  - Auth refresh: `operationId=RefreshToken`, `sessionId`

Example
```csharp
using var scope = _logger.BeginOperationScope(
    operationName: "Search",
    correlationId: correlationId,
    requestId: httpRequestId,
    albumId: albumId,
    retryAttempt: attempt,
    rateLimitBucket: partitionKey);

_logger.LogInformation("Searching Qobuz for {Query}", query);
```

Notes
- Keys are flat and consistent to simplify querying in log backends.
- Avoid high-cardinality keys unless they’re essential to correlate flows.

