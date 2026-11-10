# API Integration Notes

## Endpoint Reference

### GET /api/v1/shipments - List Shipments (Paginated)
**Status Codes:**
- `200 OK` - Successfully retrieved paginated list
- `400 Bad Request` - Invalid pagination parameters (page < 1, pageSize < 1 or > 50)
- `401 Unauthorized` - Missing or invalid authentication token

**Query Parameters:**
- `page` (int, default 1) - Page number
- `pageSize` (int, default 20, max 50) - Items per page
- `status` (string, optional) - Filter by status (Created, PickedUp, InTransit, OutForDelivery, Delivered, Failed)
- `city` (string, optional) - Filter by recipient city

### GET /api/v1/shipments/{trackingNumber} - Get Shipment
**Status Codes:**
- `200 OK` - Shipment found
- `404 Not Found` - Tracking number doesn't exist
- `401 Unauthorized` - Missing or invalid token

### POST /api/v1/shipments - Create Shipment
**Status Codes:**
- `201 Created` - Shipment created successfully
- `Location` header: `/api/v1/shipments/{trackingNumber}`
- `400 Bad Request` - Validation failed (tracking number format, weight range, missing fields)
- `409 Conflict` - Tracking number already exists
- `415 Unsupported Media Type` - Wrong Content-Type header

**Validation Rules:**
- Tracking number: `^APX-\d{8}$`
- Weight: 0.1 - 70 kg
- Recipient name, phone, address, city: required, non-empty
- Phone: must match Phone validation attribute

### PUT /api/v1/shipments/{trackingNumber} - Update Shipment
**Status Codes:**
- `200 OK` - Updated successfully
- `400 Bad Request` - Validation failed
- `404 Not Found` - Shipment not found
- `409 Conflict` - Cannot update a Delivered or Failed shipment

### PATCH /api/v1/shipments/{trackingNumber}/status - Update Status
**Status Codes:**
- `200 OK` - Status updated
- `404 Not Found` - Shipment not found
- `409 Conflict` - Invalid status transition

**Valid Transitions:**
```
Created → PickedUp → InTransit → OutForDelivery → Delivered
                                               ↘ Failed
```

### DELETE /api/v1/shipments/{trackingNumber} - Delete Shipment
**Status Codes:**
- `204 No Content` - Deleted successfully
- `404 Not Found` - Shipment not found
- `409 Conflict` - Can only delete shipments in "Created" status

### POST /api/v1/shipments/{trackingNumber}/scans - Upload Offline Scans
**Status Codes:**
- `200 OK` - Scans processed (see response for counts)
- `400 Bad Request` - Validation failed (scan time in future, etc.)
- `404 Not Found` - Shipment not found
- `422 Unprocessable Entity` - Some scans rejected with details

**Response:**
```json
{
  "accepted": 18,
  "duplicatesSkipped": 2,
  "rejected": 0,
  "rejectionReasons": []
}
```

**Validation:**
- Scan time cannot be in future (allows 5 sec clock skew)
- Delivered scans must include signature name in Notes field
- Each scan carries unique ScanId from Part A

### GET /api/v1/shipments/{trackingNumber}/scans - Scan History
**Status Codes:**
- `200 OK` - Scans retrieved (ordered by time, ascending)
- `404 Not Found` - Shipment not found

---

## Error Response Format

All errors return `ProblemDetails` (RFC 7807):

```json
{
  "type": "https://apexpost.api/errors/validation-error",
  "title": "Validation Failed",
  "status": 400,
  "detail": "Tracking number must match format APX-XXXXXXXX",
  "instance": "/api/v1/shipments",
  "errors": {
    "TrackingNumber": [
      "Tracking number must match format APX-XXXXXXXX"
    ]
  }
}
```

### Common Error Responses

**400 Bad Request - Validation**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "...",
  "errors": {
    "Weight": ["The field Weight must be between 0.1 and 70."]
  }
}
```

**404 Not Found**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Shipment APX-99999999 not found",
  "instance": "/api/v1/shipments/APX-99999999"
}
```

**409 Conflict - Duplicate**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "Shipment with tracking number APX-12345678 already exists",
  "instance": "/api/v1/shipments"
}
```

**409 Conflict - Invalid Transition**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "Cannot transition from Delivered to InTransit",
  "instance": "/api/v1/shipments/APX-12345678/status"
}
```

**401 Unauthorized**
```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authorization header missing or invalid"
}
```

**403 Forbidden - Wrong Role**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "User does not have 'Admin' role required for this operation"
}
```

**403 Forbidden - Wrong Courier**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "Courier can only upload scans under their own ID",
  "instance": "/api/v1/shipments/APX-12345678/scans"
}
```

---

## API Versioning

### V1 (Deprecated)
- Flat recipient structure
- No latest scan summary
- Status as enum or string

### V2 (Current)
- Nested recipient object:
  ```json
  {
    "recipient": {
      "name": "John Doe",
      "phone": "+1234567890",
      "address": "123 Main St",
      "city": "Springfield"
    }
  }
  ```
- Latest scan summary:
  ```json
  {
    "latestScan": {
      "scanType": "InTransit",
      "location": "Distribution Center",
      "scannedAtUtc": "2024-11-10T15:30:00Z"
    }
  }
  ```
- Status as string enum

---

## Custom Validation Rules

### Rule 1: Tracking Number Format
- Pattern: `^APX-\d{8}$`
- Example valid: `APX-12345678`
- Example invalid: `APX-1234567`, `apx-12345678`, `APX123456`

### Rule 2: Weight Range
- Minimum: 0.1 kg
- Maximum: 70 kg
- Used for: Physical package constraints

### Rule 3: Scan Time Validation
- Constraint: Cannot be in the future
- Tolerance: 5-second clock skew allowed
- Rationale: Prevent backdating or invalid timestamps

### Rule 4: Delivered Scan Requires Signature
- Rule: If `ScanType` == "Delivered", then `Notes` must contain recipient signature name
- Rationale: Proof of delivery requires acknowledgment
- Example: Notes = "Signed by: Jane Smith"

---

## HTTP Status Code Summary

| Code | Meaning | Example |
|------|---------|---------|
| 200 | OK | Shipment retrieved, scans uploaded |
| 201 | Created | Shipment created with Location header |
| 204 | No Content | Shipment deleted |
| 400 | Bad Request | Weight not in range, invalid JSON |
| 401 | Unauthorized | Missing JWT token |
| 403 | Forbidden | Courier uploading with wrong ID, missing role |
| 404 | Not Found | Tracking number doesn't exist |
| 409 | Conflict | Duplicate shipment, invalid status transition |
| 415 | Unsupported Media Type | Content-Type not application/json |
| 422 | Unprocessable Entity | Scan validation failed |
| 500 | Internal Server Error | Unhandled exception (no stack trace in prod) |

---

## Idempotent Scan Upload

The scan upload endpoint (`POST /shipments/{trackingNumber}/scans`) is **idempotent**:
- Each scan carries a unique `ScanId` from the offline store (format: `scan::{Guid}`)
- Re-uploading the same `ScanId` does not create a duplicate
- The server tracks which `ScanId` values have been processed
- Duplicate attempts increment `duplicatesSkipped` counter

Example:
```
First upload:  [scan1, scan2, scan3] → {accepted: 3, duplicates: 0, rejected: 0}
Resend:        [scan1, scan2]        → {accepted: 0, duplicates: 2, rejected: 0}
New upload:    [scan4]               → {accepted: 1, duplicates: 0, rejected: 0}
```

This design ensures that network retries or accidental duplicate uploads don't corrupt the database.
