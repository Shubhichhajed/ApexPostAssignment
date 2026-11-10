using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ApexPost.Api.Models;

/// <summary>
/// Request DTO for creating a new shipment.
/// </summary>
public class CreateShipmentRequest : IValidatableObject
{
    /// <summary>
    /// Tracking number (format: APX-XXXXXXXX).
    /// </summary>
    [Required]
    [RegularExpression(@"^APX-\d{8}$", ErrorMessage = "Tracking number must match format APX-XXXXXXXX")]
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>
    /// Weight in kilograms (0.1 - 70 kg).
    /// </summary>
    [Required]
    [Range(0.1, 70.0, ErrorMessage = "Weight must be between 0.1 and 70 kg")]
    public decimal Weight { get; set; }

    /// <summary>
    /// Recipient name.
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Recipient phone number (E.164 format or common formats).
    /// </summary>
    [Required]
    [Phone(ErrorMessage = "Invalid phone number format")]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>
    /// Recipient address.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 5)]
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>
    /// Recipient city.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string RecipientCity { get; set; } = string.Empty;

    /// <summary>
    /// Optional sender notes.
    /// </summary>
    [StringLength(1000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Custom validation: tracking number must be unique (will be checked in service)
        if (!string.IsNullOrEmpty(TrackingNumber) && !Regex.IsMatch(TrackingNumber, @"^APX-\d{8}$"))
        {
            yield return new ValidationResult("Tracking number format invalid", new[] { nameof(TrackingNumber) });
        }

        // Additional custom validation can be added here
    }
}

/// <summary>
/// Request DTO for updating shipment details.
/// </summary>
public class UpdateShipmentRequest : IValidatableObject
{
    [StringLength(255, MinimumLength = 1)]
    public string? RecipientName { get; set; }

    [Phone]
    public string? RecipientPhone { get; set; }

    [StringLength(500, MinimumLength = 5)]
    public string? RecipientAddress { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? RecipientCity { get; set; }

    [Range(0.1, 70.0)]
    public decimal? Weight { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // At least one field must be provided for update
        if (string.IsNullOrEmpty(RecipientName) &&
            string.IsNullOrEmpty(RecipientPhone) &&
            string.IsNullOrEmpty(RecipientAddress) &&
            string.IsNullOrEmpty(RecipientCity) &&
            !Weight.HasValue &&
            string.IsNullOrEmpty(Notes))
        {
            yield return new ValidationResult("At least one field must be provided for update");
        }
    }
}

/// <summary>
/// Request DTO for updating shipment status.
/// </summary>
public class UpdateStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for uploading offline scans.
/// </summary>
public class ScanUploadRequest
{
    /// <summary>
    /// Collection of scans recorded offline.
    /// </summary>
    [Required]
    public IEnumerable<ScanItemRequest> Scans { get; set; } = new List<ScanItemRequest>();
}

/// <summary>
/// Represents a single scan in the upload request.
/// </summary>
public class ScanItemRequest : IValidatableObject
{
    /// <summary>
    /// Unique ID from the offline store (scan::{Guid}).
    /// </summary>
    [Required]
    public string ScanId { get; set; } = string.Empty;

    /// <summary>
    /// Type of scan (Pickup, InTransit, Delivered, Failed).
    /// </summary>
    [Required]
    public string ScanType { get; set; } = string.Empty;

    /// <summary>
    /// Courier ID performing the scan.
    /// </summary>
    [Required]
    public string CourierId { get; set; } = string.Empty;

    /// <summary>
    /// Geographic location of the scan.
    /// </summary>
    [Required]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Scan timestamp in UTC (ISO 8601 format).
    /// </summary>
    [Required]
    public DateTime ScannedAtUtc { get; set; }

    /// <summary>
    /// Optional notes (e.g., recipient signature name, delivery issues).
    /// </summary>
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Custom validation: scan time cannot be in the future
        if (ScannedAtUtc > DateTime.UtcNow.AddSeconds(5)) // Allow 5 sec clock skew
        {
            yield return new ValidationResult("Scan time cannot be in the future", new[] { nameof(ScannedAtUtc) });
        }

        // If scan type is Delivered, Notes (signature name) should be present
        if (ScanType == "Delivered" && string.IsNullOrWhiteSpace(Notes))
        {
            yield return new ValidationResult(
                "Delivery scans must include recipient signature or name in Notes",
                new[] { nameof(Notes) });
        }
    }
}

/// <summary>
/// Response DTO for scan upload result.
/// </summary>
public class ScanUploadResponse
{
    public int Accepted { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int Rejected { get; set; }
    public List<string> RejectionReasons { get; set; } = new();
}
