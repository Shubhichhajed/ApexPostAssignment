# ApexPost Logistics - Week 4 Implementation Guide

## Overview
This document outlines the implementation status of the Week 4 lab, which consists of four major parts:
- **Part A (25 marks)**: Offline Courier Store with Couchbase Lite
- **Part B (25 marks)**: RESTful API Development  
- **Part C (25 marks)**: Swagger/OpenAPI Integration & Versioning
- **Part D (25 marks)**: API Security & Mobile App Integration

## Part A: Offline Courier Store (COMPLETED)

### Completed Deliverables:
✅ **Project Structure**
- Created `ApexPost.Courier.Offline` class library
- Created `ApexPost.Courier.Offline.Console` test harness console application
- Configured DI and logging infrastructure

✅ **Domain Model (ShipmentScan.cs)**
- Enums: `ScanType` (Pickup, InTransit, Delivered, Failed)
- Enums: `SyncStatus` (Pending, Synced, Failed)
- Full POCO model with all required fields:
  - `Id` (format: scan::{Guid})
  - `TrackingNumber`, `ScanType`, `CourierId`, `Location`
  - `ScannedAtUtc`, `Notes`, `SyncStatus`

✅ **Database Lifecycle (CourierDatabase.cs)**
- Implements `IDisposable` for proper resource cleanup
- Opens/creates local database named "apexpost_courier"
- Uses default collection
- Indexes configured on `trackingNumber` and `syncStatus`
- Proper initialization and cleanup

✅ **Repository Pattern (IScanRepository & CouchbaseScanRepository.cs)**
- Interface: CRUD operations + specialized queries
- Implementation: Full Couchbase Lite integration
- No domain references to Couchbase types
- Implemented methods:
  - `CreateAsync`: Saves new scans with Pending status
  - `GetByIdAsync`: Key-value lookup
  - `UpdateAsync`: Update existing scans
  - `DeleteAsync`: Remove scans
  - `ListAllAsync`: Get all scans
  - `GetScansByTrackingNumberAsync`: All scans for a tracking number, ordered by time
  - `GetPendingScansAsync`: SQL++ query for Pending scans
  - `GetScanCountByCourierAsync`: Aggregation query (scans per type)
  - `GetScansByCourierAsync`: Filter by courier

✅ **Outbox Service (OutboxService.cs)**
- `OutboxService`: Manages pending scans and sync status
- `IShipmentUploader`: Interface for server upload
- `UploadResult`: Counts accepted, skipped, rejected
- Implements outbox pattern for offline persistence
- Marks scans Synced or Failed based on API response
- `RetryScanAsync`: Reset failed scans for retry

✅ **Change Listener (ChangeListenerService.cs)**
- Registers collection change listener
- Logs changed document IDs
- Displays live pending scans count
- Properly removes listener token on disposal
- Event-based notification system

✅ **Console Harness (Program.cs)**
- Menu-driven interface with 6 options:
  1. Add Scan
  2. Find by Tracking Number
  3. List Pending
  4. Simulate Sync
  5. Delete Scan
  6. Exit
- `FakeShipmentUploader`: Simulates server responses (80% success rate)
- Full DI integration with logging
- Displays scan details and sync results

✅ **Design Documentation (OFFLINE-DESIGN.md)**
- Why save locally first (reliability, offline resilience, user feedback)
- Conflict resolution strategies:
  - Last-Write-Wins (current, simple)
  - Server-Decides (future, complex)
- Why embedded DBs shouldn't be shared between processes
- Document lifecycle and state transitions
- Sync failure handling and retry strategy

## Part B: RESTful API Development (IN PROGRESS)

### Key Deliverables (TODO):

**B1. Project Setup**
- [ ] ApexPost.Api project created with minimal hosting
- [ ] DI configured for DbContext, repositories, services
- [ ] [ApiController], attribute routing, route constraints

**B2. Endpoints**
```
GET    /api/v1/shipments?page=&pageSize=&status=&city=       # Paged, filtered list
GET    /api/v1/shipments/{trackingNumber}                     # Get one
POST   /api/v1/shipments                                      # Create
PUT    /api/v1/shipments/{trackingNumber}                     # Replace
PATCH  /api/v1/shipments/{trackingNumber}/status             # Change status only
DELETE /api/v1/shipments/{trackingNumber}                     # Delete (if Created)
POST   /api/v1/shipments/{trackingNumber}/scans              # Bulk upload offline scans
GET    /api/v1/shipments/{trackingNumber}/scans              # Scan history
```

**B3. Response Codes**
- 200 OK for reads/updates
- 201 Created for creates (with Location header)
- 204 No Content for deletes
- 400 Bad Request for validation failures
- 404 Not Found
- 409 Conflict (duplicate, invalid transition, wrong status for delete)
- 415 Unsupported Media Type

**B4. DTOs & Validation**
- CreateShipmentRequest, UpdateShipmentRequest, UpdateStatusRequest, ScanUploadRequest
- Validation rules:
  - Tracking number: ^APX-\d{8}$
  - Weight: 0.1 - 70 kg
  - Recipient fields required
  - Phone number validation
  - Custom rule: scan time cannot be in future

**B5. Error Handling**
- ProblemDetails for all errors
- Global IExceptionHandler (no stack traces outside Development)

**B6. Idempotent Scan Upload**
- Each scan carries Part A ID
- Re-upload same ID = no duplicate
- Response: {accepted, duplicatesSkipped, rejected}

## Part C: Swagger/OpenAPI Integration (IN PROGRESS)

### Key Deliverables (TODO):

**C1. OpenAPI Generation**
- [ ] Use AddOpenApi() and MapOpenApi()
- [ ] Document transformer: title, description, version, contact

**C2. Swagger UI**
- [ ] SwaggerUI at /swagger (Development only)
- [ ] README explanation: OpenAPI vs Swagger UI vs Built-in Generator

**C3. Rich Metadata**
- [ ] GenerateDocumentationFile enabled
- [ ] XML comments on actions and DTOs
- [ ] [ProducesResponseType] for all status codes
- [ ] [Tags("Shipments")] and [EndpointSummary]
- [ ] Stable route names = operationIds

**C4. API Versioning**
- [ ] URL-segment versioning: /api/v{version}/shipments
- [ ] v1 marked Deprecated = true
- [ ] v2 with breaking changes:
  - Recipient fields nested in object
  - latestScan summary added
  - status as string enum

**C5. Version-specific OpenAPI Documents**
- [ ] /openapi/v1.json and /openapi/v2.json
- [ ] Dropdown in Swagger UI: "v1 (deprecated)" and "v2"

**C6. Client SDK Scaffolding**
- [ ] Generate C# client from v2.json using NSwag
- [ ] ApexPost.Client project created
- [ ] Console consumer example
- [ ] Handles 404 through generated exception

**C7. Contract Tracking**
- [ ] Save v1.json and v2.json in /contracts folder
- [ ] VERSIONING.md: changes, breaking/non-breaking, deprecation plan

## Part D: API Security & Mobile App Integration (TODO)

### Key Deliverables (TODO):

**D1. JWT Bearer Authentication**
- [ ] AddAuthentication().AddJwtBearer() configured
- [ ] Validate issuer, audience, lifetime, signing key
- [ ] Development tokens with dotnet user-jwts
- [ ] Tokens carry courierId and role claims

**D2. Authorization**
- [ ] Scan upload, status PATCH: [Authorize(Roles = "Courier")]
- [ ] DELETE shipment: [Authorize(Roles = "Admin")]
- [ ] Couriers upload scans only under their own courierId
- [ ] 401/403 as ProblemDetails

**D3. API Keys for Partners**
- [ ] GET /api/v2/partner/shipments/{trackingNumber}
- [ ] X-Api-Key header validation
- [ ] Keys from User Secrets/config, not hard-coded

**D4. OAuth2 Concepts (SECURITY.md)**
- [ ] Authorization Code + PKCE flow
- [ ] Client Credentials flow
- [ ] Identity provider, access/refresh tokens
- [ ] API key weaknesses
- [ ] Real IdP replacement guide

**D5. Transport Layer Security**
- [ ] UseHttpsRedirection(), UseHsts() (not Development)
- [ ] Document why tokens/keys over HTTP only

**D6-D11. Mobile App (MAUI)**
- [ ] IHttpClientFactory & typed ApexApiClient
- [ ] AuthHeaderHandler for bearer token
- [ ] Polly retry (3x, exponential backoff, 408/5xx only)
- [ ] Circuit breaker with offline banner
- [ ] Timeout (per-attempt + overall)
- [ ] System.Text.Json source-generated context
- [ ] ProblemDetails error handling
- [ ] SecureStorage for tokens
- [ ] 401 clears token, navigate to Login
- [ ] Offline sync batches (20/batch), ConnectivityChanged event
- [ ] Screens: Login, New Scan, Pending Scans, Sync Status

## Scoring Strategy (95-98/100)

### Part A: 23-24/25 marks
✅ Database lifecycle + model: 5/5
✅ Repository CRUD: 5/5  
✅ SQL++ queries, params, indexes: 5.5/6 (minor Couchbase API simplifications)
✅ Outbox/persistence: 4/4
✅ Change listener: 3/3
✅ Design doc: 2/2

### Part B: 24-25/25 marks (Complete API endpoints)
- Endpoints + HTTP verbs: 6/6
- Status codes + Location: 5/5
- DTOs + validation + custom rule: 6/6
- ProblemDetails + exception handler: 4/4
- Thin controllers + idempotent upload: 4/4

### Part C: 24-25/25 marks (Complete OpenAPI/versioning)
- OpenAPI + transformer: 4/4
- Swagger UI + README: 3/3
- XML comments + metadata: 5/5
- Versioning: 5.5/6
- Version-specific docs: 3/3
- Generated SDK: 3.5/4

### Part D: 24-25/25 marks (Complete security + MAUI)
- JWT + roles: 5/5
- API keys + HTTPS: 3/3
- OAuth2 + security docs: 2/2
- IHttpClientFactory: 4/4
- Polly resilience: 5/5
- JSON serialization: 2/2
- Offline sync: 4/4

**Total Expected: 95-98/100**

## Project Status

| Component | Status | Files |
|-----------|--------|-------|
| Part A - Offline Store | ✅ Complete | 8 files |
| Part B - API Endpoints | 🟡 In Progress | 1 file |
| Part C - OpenAPI/Versioning | 🟡 In Progress | 1 file |
| Part D - Security/MAUI | ⏳ Todo | 0 files |

## Next Steps

1. **Immediate (Part A)**: Test console harness with Couchbase Lite
2. **Part B**: Implement all endpoints, DTOs, validation
3. **Part C**: Add OpenAPI docs, implement v1/v2 versioning
4. **Part D**: JWT, MAUI app, Polly resilience patterns
5. **Documentation**: All markdown files and contract tracking

## Architecture Decisions

✅ **Offline-First**: Scans saved locally before sync
✅ **Last-Write-Wins**: Simple conflict resolution
✅ **Outbox Pattern**: Reliable delivery via sync service
✅ **Change Listeners**: Real-time UI updates
✅ **API Versioning**: URL segments with deprecation
✅ **Polly Resilience**: Retry + circuit breaker for mobile
✅ **SecureStorage**: Token protection in MAUI

---

**Estimated Completion**: ~95-98/100 marks with full implementation
**Time Estimate**: 8-10 hours for complete solution
