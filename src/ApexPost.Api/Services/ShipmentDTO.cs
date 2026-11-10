namespace ApexPost.Api.Services;

/// <summary>
/// Internal DTO for shipment operations (used by service layer).
/// </summary>
public class ShipmentDTO
{
    public string Id { get; set; } = string.Empty;
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
/// DTO for scan results.
/// </summary>
public class ScanDTO
{
    public string Id { get; set; } = string.Empty;
    public string ScanType { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Result of uploading scans (idempotent operation).
/// </summary>
public class ScanUploadResult
{
    public int Accepted { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int Rejected { get; set; }
    public List<string> RejectionReasons { get; set; } = new();
}
