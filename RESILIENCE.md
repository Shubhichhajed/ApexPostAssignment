# Resilience Design for Mobile API Integration

## Overview
The ApexPost MAUI mobile app must survive unreliable network conditions, server failures, and service degradation while ensuring data consistency and user experience.

## 1. Resilience Patterns with Polly

### 1.1 Retry Policy

**Configuration**:
```csharp
var retryPolicy = Policy.Handle<HttpRequestException>()
    .Or<TimeoutRejectedException>()
    .OrResult<HttpResponseMessage>(r => 
        r.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||      // 408
        r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||  // 503
        r.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)        // 504
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt =>
        {
            // Exponential backoff with jitter
            var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            return baseDelay.Add(jitter);
        },
        onRetry: (outcome, timespan, retryCount, context) =>
        {
            var logger = (ILogger)context["logger"];
            logger?.LogWarning(
                "Retry {RetryCount} after {DelayMs}ms due to: {Reason}",
                retryCount,
                timespan.TotalMilliseconds,
                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
        });
```

**Retry Sequence**:
```
Attempt 1: Immediate failure
  ↓ Wait 1s + 0-1000ms jitter
Attempt 2: Failure
  ↓ Wait 2s + 0-1000ms jitter
Attempt 3: Failure
  ↓ Wait 4s + 0-1000ms jitter
Attempt 4: Final failure → Circuit breaker opens
```

**Idempotent Operations** (Safe to Retry):
- `GET /shipments` - Read operation, no side effects
- `GET /shipments/{id}` - Read operation, no side effects
- `GET /shipments/{id}/scans` - Read operation, no side effects
- `POST /shipments/{id}/scans` - **Idempotent** via scan ID deduplication (see below)

**Non-Idempotent** (Should NOT Retry):
- `POST /shipments` - Creates new shipment each time
- `PUT /shipments/{id}` - Overwrites data each time
- `PATCH /shipments/{id}/status` - Modifies state each time
- `DELETE /shipments/{id}` - Deletes each time

### 1.2 Why Scan Upload is Safe to Retry

**Design**:
1. Client generates unique `ScanId` for each scan (format: `scan::{Guid}`)
2. Each scan is uploaded with its `ScanId` as part of the request
3. Server tracks processed `ScanId` values in a database
4. If same `ScanId` arrives twice, server responds with `duplicatesSkipped++` instead of creating duplicate

**Example Scenario**:
```
First POST /shipments/APX-12345678/scans
  Request: [{ ScanId: "scan::550e...", ScanType: "Pickup", ... }]
  Response: { accepted: 1, duplicates: 0, rejected: 0 }
  Client stores: scan is "Synced"

Network timeout, client retries...

Second POST /shipments/APX-12345678/scans (same body)
  Request: [{ ScanId: "scan::550e...", ScanType: "Pickup", ... }]
  Response: { accepted: 0, duplicates: 1, rejected: 0 }
  Client handles: Updates UI, no duplicate created on server
```

**Implementation**:
```csharp
// Server-side deduplication
var processedScans = new HashSet<string>(); // Or query database for existing ScanIds

foreach (var scan in request.Scans)
{
    if (processedScans.Contains(scan.ScanId))
    {
        result.DuplicatesSkipped++;
        continue;
    }

    try
    {
        // Store scan in database
        _scanRepository.SaveAsync(scan);
        result.Accepted++;
        processedScans.Add(scan.ScanId);
    }
    catch (Exception ex)
    {
        result.Rejected++;
        result.RejectionReasons.Add($"Scan {scan.ScanId}: {ex.Message}");
    }
}
```

### 1.3 Circuit Breaker Policy

**Configuration**:
```csharp
var circuitBreakerPolicy = Policy.Handle<HttpRequestException>()
    .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
    .CircuitBreakerAsync<HttpResponseMessage>(
        handledEventsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(60),
        onBreak: (outcome, duration, context) =>
        {
            var logger = (ILogger)context["logger"];
            logger?.LogError(
                "Circuit breaker OPEN for {Duration}s due to repeated failures: {Reason}",
                duration.TotalSeconds,
                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());

            // Notify UI
            MessagingCenter.Send<string>("Circuit breaker open - working offline");
        },
        onReset: (context) =>
        {
            var logger = (ILogger)context["logger"];
            logger?.LogInformation("Circuit breaker CLOSED - connection restored");
            MessagingCenter.Send<string>("Connection restored - resuming sync");
        });
```

**States**:
1. **Closed** (Normal): Requests pass through, failures counted
2. **Open** (Failure): Requests immediately rejected without calling server
3. **Half-Open** (Recovery): Next request attempts to test server recovery

**Behavior Timeline**:
```
Time  State        Event
0s    Closed       Request 1 fails → Failure count = 1
2s    Closed       Request 2 fails → Failure count = 2
4s    Closed       Request 3 fails → Failure count = 3 → Circuit OPENS
5s    Open         Request 4 attempted → Immediately rejected (no network call)
                   → UI shows "Server unavailable - working offline" banner
10s   Open         Requests still rejected
50s   Open         1 minute elapsed, circuit HALF-OPENS
51s   Half-Open    Request 5 attempted → If succeeds, circuit CLOSES
                   → If fails, circuit stays OPEN another 60s
```

**UI Indication**:
```csharp
// In MAUI View
public partial class SyncStatusPage : ContentPage
{
    public SyncStatusPage()
    {
        InitializeComponent();

        MessagingCenter.Subscribe<string>(this, "CircuitBreakerStatus", (message) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OfflineBanner.Text = message;
                OfflineBanner.IsVisible = message.Contains("offline");
            });
        });
    }
}
```

### 1.4 Timeout Policy

**Configuration**:
```csharp
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
    timeoutPerAttempt: TimeSpan.FromSeconds(10),    // Per request
    timespan: TimeSpan.FromSeconds(30));            // Overall (wrap)

// Combined policy stack (Bulkhead + Timeout + Retry + CircuitBreaker)
var policyWrap = Policy.WrapAsync(
    circuitBreakerPolicy,
    timeoutPolicy,
    retryPolicy);

builder.Services.AddHttpClient<ApexApiClient>()
    .AddPolicyHandler(policyWrap);
```

**Timeout Sequence**:
```
POST /shipments/APX-123/scans (10s per-request timeout)
  |
  ├─ Request 1 sent
  │   └─ 8s elapsed, server processing
  │      └─ 10s timeout → HttpRequestException
  │         └─ Retry Policy: Wait 1s + jitter
  │
  ├─ Request 2 sent
  │   └─ 12s elapsed (10s timeout)
  │      └─ Timeout → HttpRequestException
  │         └─ Retry Policy: Wait 2s + jitter
  │
  ├─ Request 3 sent
  │   └─ Server responds 200 OK within 5s
  │      └─ Success!
```

## 2. Offline Sync Integration

### 2.1 Connectivity Detection

```csharp
public class ConnectivityService
{
    private bool _isConnected;

    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    public ConnectivityService()
    {
        Connectivity.ConnectivityChanged += async (s, e) =>
        {
            var isConnected = e.NetworkAccess == NetworkAccess.Internet;

            if (!_isConnected && isConnected)
            {
                // Transitioned from Offline → Online
                _isConnected = true;
                ConnectivityChanged?.Invoke(this, new(true));

                // Trigger sync
                await SyncPendingScansAsync();
            }
            else if (_isConnected && !isConnected)
            {
                // Transitioned from Online → Offline
                _isConnected = false;
                ConnectivityChanged?.Invoke(this, new(false));

                // Show offline banner
                MessagingCenter.Send<string>("Now offline - scans saved locally");
            }
        };
    }

    private async Task SyncPendingScansAsync()
    {
        var pendingScans = await _repository.GetPendingScansAsync();
        if (!pendingScans.Any())
            return;

        // Batch upload in groups of 20
        const int batchSize = 20;
        var batches = pendingScans
            .Chunk(batchSize)
            .ToList();

        foreach (var batch in batches)
        {
            try
            {
                var response = await _apiClient.UploadScansAsync(batch);

                // Mark uploaded scans
                foreach (var scan in batch.Take(response.Accepted))
                {
                    scan.SyncStatus = SyncStatus.Synced;
                    await _repository.UpdateAsync(scan);
                }

                // Log failures
                foreach (var reason in response.RejectionReasons)
                {
                    _logger.LogWarning("Scan upload failed: {Reason}", reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch sync failed, will retry when reconnected");
                // Scans remain Pending, will retry on next connectivity change
            }
        }
    }
}
```

### 2.2 Batch Upload Strategy

**Rationale**:
- **Efficiency**: Reduce number of API calls (20 scans per call vs 1)
- **Bandwidth**: Fewer HTTP headers, single connection overhead
- **Server Load**: Easier to scale (process batches in background job)
- **Retry**: Simpler to track batch status than individual scans

**Response Handling**:
```json
{
  "accepted": 18,
  "duplicatesSkipped": 2,
  "rejected": 0,
  "rejectionReasons": []
}
```

Client marks all 20 scans as Synced if `accepted + duplicatesSkipped == 20` (all processed).

### 2.3 Logging Resilience Events

```csharp
_logger.LogInformation("Attempting scan upload: batch of {Count} scans", scans.Count());

foreach (var attempt in Enumerable.Range(1, 3))
{
    _logger.LogDebug("Upload attempt {AttemptNumber}/3", attempt);

    try
    {
        var result = await _apiClient.UploadScansAsync(scans);
        _logger.LogInformation("Upload successful: {Accepted} accepted, {Skipped} duplicates, {Rejected} rejected",
            result.Accepted, result.DuplicatesSkipped, result.Rejected);
        return;
    }
    catch (HttpRequestException ex) when (ex.InnerException is TimeoutException)
    {
        _logger.LogWarning("Upload attempt {AttemptNumber} timed out, retrying...", attempt);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Upload attempt {AttemptNumber} failed", attempt);
    }
}

_logger.LogError("Upload failed after 3 retries, will retry when connectivity changes");
```

## 3. JSON Serialization & Error Handling

### 3.1 Shared JsonSerializerOptions

```csharp
public class ApiJsonSerializerContext : JsonSerializerContext
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new ISO8601DateTimeConverter()
        }
    };

    public override JsonSerializerOptions Options => _options;
}

// Usage
var json = JsonSerializer.Serialize(scan, ApiJsonSerializerContext.Default.ShipmentScan);
```

### 3.2 ProblemDetails Error Handling

```csharp
// Server sends ProblemDetails on error
{
  "type": "https://apexpost.api/errors/validation-error",
  "title": "Validation Failed",
  "status": 400,
  "detail": "Scan time cannot be in the future",
  "errors": {
    "ScannedAtUtc": ["Scan time cannot be in the future"]
  }
}

// Client deserializes and displays
try
{
    var response = await _httpClient.PostAsync(uri, content);

    if (!response.IsSuccessStatusCode)
    {
        var problemJson = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(problemJson);

        var friendlyMessage = problem?.Detail ?? "An error occurred";
        await DisplayAlert("Error", friendlyMessage, "OK");
    }
}
catch (HttpRequestException ex)
{
    await DisplayAlert("Network Error", "Unable to connect to server", "OK");
}
```

## 4. Resilience Configuration Values

| Component | Value | Rationale |
|-----------|-------|-----------|
| **Retry Count** | 3 attempts | Balance between persistence and user wait time (~7s total) |
| **Backoff Base** | 2^n seconds | Exponential growth: 1s, 2s, 4s |
| **Jitter** | ±1000ms | Prevent thundering herd (multiple clients retrying simultaneously) |
| **Circuit Breaker Threshold** | 3 failures | Enough to distinguish temporary glitch from outage |
| **Circuit Breaker Duration** | 60 seconds | Give server time to recover without crushing it |
| **Per-Request Timeout** | 10 seconds | Network round-trip for typical shipment data (~1KB) |
| **Overall Timeout** | 30 seconds | Full policy (retry + backoff) should complete within this |
| **Batch Size** | 20 scans | Reasonable payload (JSON ~2-3 KB per scan, 40-60 KB batch) |

## 5. Offline Sync Checklist

- [x] Scans saved to local Couchbase Lite with `SyncStatus = Pending`
- [x] Connectivity listener monitors network state
- [x] On reconnection, upload Pending scans in batches of 20
- [x] Each scan carries unique ScanId from offline store
- [x] Server deduplicates by ScanId (idempotent)
- [x] Client marks scans Synced based on upload response
- [x] UI updates pending count via change listener
- [x] Failed scans remain Pending for retry
- [x] Circuit breaker shows "offline" banner to user
- [x] All retry/sync events logged for debugging

## 6. Resiliency Testing

### Manual Testing Scenarios
1. **Kill Server**: Stop API service → Circuit breaker opens → User sees offline banner
2. **Slow Network**: Simulate 10s latency → Timeouts trigger → Retries with backoff
3. **Packet Loss**: Intermittent 50% loss → Retries succeed on 2nd/3rd attempt
4. **Go Offline**: Airplane mode → Scans saved locally → Sync triggers on reconnect
5. **Duplicate Upload**: Same batch sent twice → Handled gracefully via deduplication

### Load Testing
- Batch 100 scans: Verify server handles efficiently
- Concurrent uploads: 50 couriers uploading simultaneously → No corruption
- Database contention: Verify indexes prevent lock timeouts

---

**Summary**: The resilience design ensures ApexPost couriers can work offline reliably, with automatic sync when connectivity returns, and graceful degradation under server failures.
