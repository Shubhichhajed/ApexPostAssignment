namespace ApexPost.Api.Models;

/// <summary>
/// Response DTO for shipment details (v1 - flat structure).
/// </summary>
public class ShipmentResponseV1
{
    public string TrackingNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string RecipientCity { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Response DTO for shipment details (v2 - nested recipient object).
/// </summary>
public class ShipmentResponseV2
{
    public string TrackingNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Weight { get; set; }

    /// <summary>
    /// Recipient information (nested object).
    /// </summary>
    public RecipientInfo Recipient { get; set; } = new();

    /// <summary>
    /// Latest scan summary.
    /// </summary>
    public LatestScanInfo? LatestScan { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Nested recipient information for v2.
/// </summary>
public class RecipientInfo
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

/// <summary>
/// Latest scan summary for v2.
/// </summary>
public class LatestScanInfo
{
    public string ScanType { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
}

/// <summary>
/// Response DTO for paginated shipment list.
/// </summary>
public class PaginatedShipmentsResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

/// <summary>
/// Response DTO for scan history.
/// </summary>
public class ScanHistoryResponse
{
    public string TrackingNumber { get; set; } = string.Empty;
    public List<ScanDetailResponse> Scans { get; set; } = new();
}

/// <summary>
/// Response DTO for a single scan.
/// </summary>
public class ScanDetailResponse
{
    public string ScanId { get; set; } = string.Empty;
    public string ScanType { get; set; } = string.Empty;
    public string CourierId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
    public string? Notes { get; set; }
}
