# API Versioning Strategy

## Overview

ApexPost API uses **URL-segment versioning** to manage breaking changes while maintaining backward compatibility for existing clients. The versioning scheme is `/api/v{version}/resources`.

## Version Support

### v1 (Deprecated - Legacy Contract)

**URL Pattern**: `/api/v1/shipments`

**Status**: Deprecated as of 2024-01-15

**Sunset Date**: 2024-07-15 (6 months from release)

**Support Level**: Critical bugs only; no new features

**Response Format**:
```json
{
  "trackingNumber": "APX-12345678",
  "status": "InTransit",
  "weight": 5.5,
  "recipientName": "John Doe",
  "recipientPhone": "+61234567890",
  "recipientAddress": "123 Main Street",
  "recipientCity": "Sydney",
  "notes": "Handle with care",
  "createdAtUtc": "2024-01-15T10:00:00Z"
}
```

**Endpoints**:
- `GET /api/v1/shipments` - List shipments
- `GET /api/v1/shipments/{trackingNumber}` - Get shipment
- `POST /api/v1/shipments` - Create shipment
- `PUT /api/v1/shipments/{trackingNumber}` - Update shipment
- `PATCH /api/v1/shipments/{trackingNumber}/status` - Update status
- `DELETE /api/v1/shipments/{trackingNumber}` - Delete shipment
- `POST /api/v1/shipments/{trackingNumber}/scans` - Upload scans
- `GET /api/v1/shipments/{trackingNumber}/scans` - Get scan history

### v2 (Current - Recommended)

**URL Pattern**: `/api/v2/shipments`

**Status**: Current, actively supported

**Support Level**: Full; all new features go here

**Response Format** (with breaking changes):
```json
{
  "trackingNumber": "APX-12345678",
  "status": "in_transit",
  "weight": 5.5,
  "recipient": {
    "name": "John Doe",
    "phone": "+61234567890",
    "address": "123 Main Street",
    "city": "Sydney"
  },
  "latestScan": {
    "scanType": "in_transit",
    "location": "Sydney CBD",
    "scannedAtUtc": "2024-01-15T11:30:00Z"
  },
  "notes": "Handle with care",
  "createdAtUtc": "2024-01-15T10:00:00Z"
}
```

**Endpoints**:
- `GET /api/v2/shipments` - List shipments
- `GET /api/v2/shipments/{trackingNumber}` - Get shipment
- `POST /api/v2/shipments` - Create shipment
- `PUT /api/v2/shipments/{trackingNumber}` - Update shipment
- `PATCH /api/v2/shipments/{trackingNumber}/status` - Update status
- `DELETE /api/v2/shipments/{trackingNumber}` - Delete shipment
- `POST /api/v2/shipments/{trackingNumber}/scans` - Upload scans
- `GET /api/v2/shipments/{trackingNumber}/scans` - Get scan history
- `GET /api/v2/partner/shipments/{trackingNumber}` - Partner read-only access (new)

## Breaking Changes from v1 → v2

### 1. Recipient Object Restructuring (Breaking)

**v1 Response** (Flat structure):
```json
{
  "recipientName": "John Doe",
  "recipientPhone": "+61234567890",
  "recipientAddress": "123 Main Street",
  "recipientCity": "Sydney"
}
```

**v2 Response** (Nested object):
```json
{
  "recipient": {
    "name": "John Doe",
    "phone": "+61234567890",
    "address": "123 Main Street",
    "city": "Sydney"
  }
}
```

**Impact**: Any client parsing `recipientName`, `recipientPhone`, `recipientAddress`, `recipientCity` at the top level will fail.

**Migration Path**: Update client to access `recipient.name`, `recipient.phone`, `recipient.address`, `recipient.city`.

### 2. Latest Scan Summary (Non-Breaking Addition)

**v2 Response** (New optional field):
```json
{
  "latestScan": {
    "scanType": "in_transit",
    "location": "Sydney CBD",
    "scannedAtUtc": "2024-01-15T11:30:00Z"
  }
}
```

**Impact**: Clients using v1 will not receive this field. Clients upgraded to v2 can optionally use it for quick status display.

**Use Case**: Mobile apps can display latest scan immediately without calling `GET /scans` endpoint.

### 3. Status Enum Serialization (Breaking)

**v1 Response** (PascalCase):
```json
{
  "status": "InTransit"
}
```

**v2 Response** (camelCase enum):
```json
{
  "status": "in_transit"
}
```

**Impact**: Any string comparison like `response.status === "InTransit"` will fail in v2.

**Migration Path**: Update comparisons to use `response.status === "in_transit"`.

## Non-Breaking Changes from v1 → v2

These changes are backward compatible:
- New endpoints (e.g., `GET /api/v2/partner/shipments/{trackingNumber}`)
- New optional query parameters (with sensible defaults)
- New optional fields in responses (clients can ignore them)
- Improved validation error messages

## Implementation Strategy

### URL-Segment Versioning

ApexPost uses ASP.NET Core's `Asp.Versioning` library with URL-segment versioning:

```csharp
[Route("api/v{version:apiVersion}/shipments")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class ShipmentsController : ControllerBase
{
    [HttpGet("{trackingNumber}")]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(
        string trackingNumber,
        ApiVersion version)
    {
        if (version == new ApiVersion(1, 0))
            return await GetShipmentV1Async(trackingNumber);
        else
            return await GetShipmentV2Async(trackingNumber);
    }
}
```

### OpenAPI Documents

Separate OpenAPI documents are generated for each version:
- `/openapi/v1.json` - v1 specification (deprecated flag set)
- `/openapi/v2.json` - v2 specification

### Response Headers

Both versions include version information in response headers:

```
Supported-Versions: 1.0, 2.0
Deprecated: true (v1 only)
Sunset: Thu, 15 Jul 2024 00:00:00 GMT (v1 only, RFC 8594)
```

Clients can use these headers to detect deprecated APIs and plan migrations.

## Deprecation Timeline

### Phase 1: Release (2024-01-15)
- ✅ v2 released as current version
- ✅ v1 marked deprecated
- ✅ OpenAPI documents include deprecation info
- ✅ Swagger UI shows "v1 (deprecated)" and "v2"
- ✅ Documentation updated

### Phase 2: Notice Period (2024-01-15 → 2024-07-15)
- ✅ v1 continues to function normally
- ✅ v2 recommended for all new clients
- ✅ Response headers include Sunset date
- ✅ Clients encouraged to migrate

### Phase 3: Deprecation (2024-07-15)
- ⚠️ v1 endpoints may return 410 Gone
- ✅ v2 becomes mandatory
- ✅ All new features go to v2+
- ⚠️ No new v1 maintenance releases

## Client Migration Guide

### For Existing v1 Clients

**Step 1**: Update base URL
```
Old: https://api.apexpost.com/api/v1/shipments
New: https://api.apexpost.com/api/v2/shipments
```

**Step 2**: Update response parsing for recipient object
```csharp
// v1
var name = response.RecipientName;

// v2
var name = response.Recipient.Name;
```

**Step 3**: Update status comparisons
```csharp
// v1
if (shipment.Status == "InTransit")

// v2
if (shipment.Status == "in_transit")
```

**Step 4**: Test with new Swagger UI
- Navigate to `/swagger`
- Switch to v2 from dropdown
- Use "Try it out" for each endpoint
- Verify response format matches expectations

### For New Clients

**Use v2 from the start**:
1. Reference the current OpenAPI document: `/openapi/v2.json`
2. Generate client SDK from v2.json
3. Use v2 endpoints in all new code
4. Follow examples in `/contracts/v2.json`

## Versioning Decisions (RFC-Style)

### Decision: URL-Segment Versioning vs. Accept Header Versioning

**Chosen**: URL-Segment (`/api/v1/`, `/api/v2/`)

**Rationale**:
- ✅ Browser-friendly (can test URLs in browser)
- ✅ Clearer intention in logs and metrics
- ✅ Simpler for non-REST clients
- ✅ Works with CDN caching strategies
- ❌ Duplicates endpoint definitions
- ❌ Cannot version single endpoint

**Alternative Rejected**: Accept Header (`Accept: application/vnd.apexpost.v1+json`)
- Cleaner URLs
- But: Less discoverable, breaks browser testing
- But: Requires client knowledge of Accept header format

### Decision: Concurrent Versions Support

**Chosen**: Support v1 and v2 simultaneously for 6 months

**Rationale**:
- ✅ Existing clients not forced to migrate immediately
- ✅ Reduces production incidents
- ✅ Allows phased migration
- ✅ Business continuity for partners
- ❌ Increased server-side complexity
- ❌ More code to maintain

**Alternative Rejected**: Immediate v1 retirement
- Would break all existing clients on day 1
- Unacceptable for production API

### Decision: Breaking Changes Only in Major Versions

**Policy**: Non-breaking additions go to current version; breaking changes go to next version.

**Examples**:
- ✅ `v2`: Add `latestScan` field (non-breaking)
- ✅ `v2`: Add partner API endpoint (non-breaking)
- ❌ `v1`: Change recipient structure (breaking → moved to v2)
- ❌ `v1`: Change status enum format (breaking → moved to v2)

**Rationale**:
- ✅ Predictable compatibility
- ✅ Clients can opt-in to new features
- ✅ Minimizes version fragmentation

## Support Matrix

| Version | Status | End-of-Life | Maintenance |
|---------|--------|-----------|-------------|
| v1.0 | Deprecated | 2024-07-15 | Bug fixes only |
| v2.0 | Current | TBD (2+ years) | Full support, all new features |
| v3.0 | Future | TBD | Not yet released |

## API Versioning Best Practices

1. **Be explicit**: Include version in URL, not in media type
2. **Document thoroughly**: Every field change should be documented
3. **Provide migration guides**: Help clients transition smoothly
4. **Use headers**: Set `Deprecated`, `Sunset`, `Supported-Versions`
5. **Test multiple versions**: Ensure v1 and v2 both work correctly
6. **Version the documentation**: Maintain separate docs for v1 and v2
7. **Monitor adoption**: Track v1 vs v2 usage to plan retirement
8. **Communicate early**: Announce deprecation 6+ months in advance

## References

- [RFC 7231 - HTTP/1.1 Semantics and Content](https://tools.ietf.org/html/rfc7231)
- [RFC 8594 - The Sunset HTTP Header Field](https://tools.ietf.org/html/rfc8594)
- [Stripe API Versioning](https://stripe.com/blog/api-versioning)
- [ASP.NET Core API Versioning](https://github.com/dotnet/aspnet-api-versioning)
