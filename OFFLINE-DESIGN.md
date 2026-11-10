# Offline Design Document

## Overview
ApexPost couriers operate in environments with intermittent or no network connectivity. This design document outlines the offline-first architecture that enables couriers to continue recording pickups and deliveries without a network connection, while maintaining data integrity and eventual consistency with the central server.

## 1. Why Save Locally First

### Problem
Couriers in the field often lose network coverage but must still record shipment events. A purely online-first approach would result in:
- Lost scans when connectivity drops
- User frustration and reduced productivity
- Incomplete delivery records

### Solution: Local-First Persistence
Every new scan is immediately saved to the local Couchbase Lite database with `syncStatus = Pending` before any network operation is attempted. This approach ensures:

1. **Reliability**: Scans never get lost, even if the device crashes immediately after recording
2. **Offline Resilience**: Couriers can continue working without network connectivity
3. **User Feedback**: The app can confirm a scan was saved locally within milliseconds
4. **Graceful Degradation**: If the network is unavailable, the app still provides value
5. **Audit Trail**: Every scan is recorded with a timestamp, ensuring complete order history

### Implementation
```
User records scan → Validate locally → Save to Couchbase Lite (Pending) → Return success → Later: Sync with server
```

The local database is embedded in the courier device (Couchbase Lite), requires no server connection, and provides ACID guarantees for individual documents. While the device is offline, users can:
- Record new scans
- Query scan history by tracking number
- View pending scans awaiting sync
- Retry failed uploads

## 2. Conflict Resolution Strategy: Last-Write-Wins vs. Server-Decides

### Scenario
A courier and a partner company both attempt to update the same shipment scan while disconnected. When both devices reconnect, there is conflicting data.

### Two Approaches Considered

#### Option A: Last-Write-Wins (Current Implementation)
**Mechanism**: The scan with the latest `scannedAtUtc` timestamp overwrites earlier versions.

**Pros**:
- Simple to implement and understand
- Works well for append-only audit logs
- Requires no central coordination during sync
- Suitable for non-destructive operations (e.g., adding location, updating notes)

**Cons**:
- Can lose data if both devices modify the same field
- Does not preserve intentional corrections (a courier's correction might be overwritten)
- Assumes monotonically increasing clocks (clock skew could cause issues)

**When to Use**:
- Scan records are predominantly created once and rarely modified
- Modifications are additive (e.g., appending notes)
- User data loss from conflicts is acceptable

#### Option B: Server-Decides
**Mechanism**: When the server detects conflicting versions, it applies business logic to determine the authoritative version. Examples:
- Use the scan from the authenticated courier, reject the partner's scan
- Validate the scan against business rules before accepting it
- Return a conflict error and ask the client to manually resolve

**Pros**:
- Preserves application logic and business rules
- Can prevent invalid states (e.g., a scan with a future timestamp)
- Gives the server full visibility and control
- Supports complex resolution policies

**Cons**:
- More complex to implement
- Requires server to stay in sync with client logic
- Network round-trip for every conflict resolution
- Slower sync operations

**When to Use**:
- Shipment status must follow strict state transitions
- Data loss is unacceptable
- Regulatory compliance requires full audit trails
- Scans are frequently modified after creation

### Recommended Strategy for ApexPost
For this implementation, **Last-Write-Wins** is used because:
1. Scans are recorded once and rarely modified
2. The primary use case is audit logging (appending history)
3. Couriers typically work on non-overlapping shipments
4. A simple implementation minimizes bugs

**Future Improvement**: Migrate to **Server-Decides** if:
- Delivery status must strictly enforce state transitions (e.g., cannot move back from Delivered to InTransit)
- Regulatory requirements mandate full conflict resolution tracking
- Business logic becomes too complex for timestamps alone

**Implementation**: The server API (Part B) will accept scans with their local IDs. The OutboxService marks a scan as `Synced` if the server accepts it, or `Failed` if rejected. A courier can manually retry or delete a failed scan.

## 3. Why Embedded Databases Must Not Be Shared by Multiple Processes

### Problem
Couchbase Lite is an embedded database designed for single-process access. If multiple processes (e.g., two MAUI apps, or the courier app and a background service) try to access the same database file simultaneously, data corruption can occur.

### Why Sharing Fails
1. **No Process-Level Locking**: Couchbase Lite assumes the owning process exclusively accesses the database. It does not implement distributed locking across processes.
2. **Memory-Mapped I/O**: Couchbase Lite optimizes performance using memory-mapped file I/O. Multiple processes can have conflicting views of the same memory page.
3. **Concurrent Mutations**: One process might hold a document in memory while another process modifies its disk representation, leading to lost updates or corrupted indexes.
4. **WAL (Write-Ahead Logging) Conflicts**: Couchbase Lite uses a transaction log to ensure durability. Multiple writers can corrupt this log.

### Consequences
- Scans recorded by one process might disappear
- Indexes become invalid, queries return wrong results
- The database file becomes unusable (requires manual repair or deletion)
- Data loss with no recovery path

### Solution: Single-Process Ownership
1. **One App Instance per Device**: The courier MAUI app is the sole owner and accessor of the local database.
2. **Background Sync Service** (if needed): If background sync is required, run it within the same process using async/await, not a separate system service.
3. **Shared Data via APIs**: If multiple apps on the device need courier data, they should query it via HTTP (the central API) or use a shared in-process service, not direct database access.
4. **Mobile OS Restrictions**: iOS and Android already enforce single-app-per-process, so this is a non-issue on mobile.

### Configuration Safeguards
```csharp
// Ensure only one database instance per app
public class CourierDatabase : IDisposable
{
    private static readonly Lazy<CourierDatabase> _instance = 
        new(() => new CourierDatabase());

    public static CourierDatabase Instance => _instance.Value;
    // Or use DI with Singleton registration
}
```

## 4. Sync Failures and Retry Strategy

### Handling Failed Syncs
When the OutboxService attempts to upload a scan and the API returns an error:

1. **Network Errors** (4xx, 5xx, timeout): Mark as `Failed`, user can manually retry
2. **Validation Errors** (400 Bad Request): Mark as `Failed`, display error to courier
3. **Duplicate Detection** (409 Conflict): Mark as `Synced` (already on server)
4. **Success** (201, 204): Mark as `Synced`, delete from local Pending list

The OutboxService exposes a `RetryScanAsync(scanId)` method that resets a Failed scan back to Pending.

## 5. Change Notifications

### Real-Time Updates
The ChangeListenerService registers a Couchbase Lite collection listener that fires whenever a document changes (local or via replication in future parts). This enables:

1. **Live Pending Count**: The UI displays the current number of scans awaiting sync
2. **Activity Logging**: Operational logs record which documents changed for debugging
3. **User Feedback**: The app can update UI elements in real-time (e.g., a sync progress bar)

### Event Details
```csharp
changeListener.DocumentChanged += (sender, args) =>
{
    Console.WriteLine($"Changed: {string.Join(", ", args.ChangedDocumentIds)}");
    Console.WriteLine($"Pending: {args.PendingScanCount}");
};
```

### Listener Cleanup
When the app shuts down or the database is disposed, the listener token is explicitly removed to prevent memory leaks and resource exhaustion.

## 6. Document Lifecycle

### State Diagram
```
Created (local)
    ↓
Pending (awaiting sync)
    ├→ Synced (server accepted)
    └→ Failed (server rejected or network error)
        ↓
    Pending (if courier retries)
```

### Document Structure (JSON)
```json
{
  "id": "scan::550e8400-e29b-41d4-a716-446655440000",
  "trackingNumber": "APX-12345678",
  "scanType": "Delivered",
  "courierId": "COURIER-001",
  "location": "42 Main St, Springfield",
  "scannedAtUtc": "2024-11-10T20:15:30Z",
  "notes": "Left with front desk",
  "syncStatus": "Pending"
}
```

## Summary

This offline-first design ensures:
1. **Resilience**: Couriers never lose data due to connectivity issues
2. **Simplicity**: Last-Write-Wins conflict resolution is straightforward
3. **Safety**: Single-process database access prevents corruption
4. **Visibility**: Change notifications keep the UI synchronized
5. **Recoverability**: Failed syncs can be retried or manually resolved

The OutboxService bridges the gap between the local database and the remote API, ensuring eventual consistency while maintaining local availability.
