namespace ApexPost.Api.Services;

/// <summary>
/// Service interface for managing shipments.
/// Defines all business logic for CRUD operations and status transitions.
/// </summary>
public interface IShipmentService
{
    /// <summary>
    /// Gets a paginated, filtered list of shipments.
    /// </summary>
    Task<(List<ShipmentDTO> items, int total)> GetShipmentsAsync(int page, int pageSize, string? status, string? city);

    /// <summary>
    /// Gets a shipment by tracking number.
    /// </summary>
    /// <returns>ShipmentDTO if found; null otherwise</returns>
    Task<ShipmentDTO?> GetShipmentAsync(string trackingNumber);

    /// <summary>
    /// Creates a new shipment.
    /// </summary>
    /// <returns>The created shipment</returns>
    /// <exception cref="InvalidOperationException">If tracking number already exists</exception>
    Task<ShipmentDTO> CreateShipmentAsync(
        string trackingNumber,
        decimal weight,
        string recipientName,
        string recipientPhone,
        string recipientAddress,
        string recipientCity,
        string? notes);

    /// <summary>
    /// Updates a shipment's editable fields.
    /// </summary>
    /// <exception cref="InvalidOperationException">If shipment not found or status transition invalid</exception>
    Task<ShipmentDTO> UpdateShipmentAsync(
        string trackingNumber,
        decimal weight,
        string recipientName,
        string recipientPhone,
        string recipientAddress,
        string recipientCity,
        string? notes);

    /// <summary>
    /// Changes shipment status.
    /// </summary>
    /// <exception cref="InvalidOperationException">If shipment not found or transition invalid</exception>
    Task<ShipmentDTO> UpdateShipmentStatusAsync(string trackingNumber, string newStatus);

    /// <summary>
    /// Deletes a shipment. Only allowed when status is "Created".
    /// </summary>
    /// <exception cref="InvalidOperationException">If shipment not found or status is not "Created"</exception>
    Task DeleteShipmentAsync(string trackingNumber);

    /// <summary>
    /// Uploads scans from offline device (idempotent operation).
    /// Each scan carries a ScanId; re-uploading same ScanId returns duplicatesSkipped++.
    /// </summary>
    Task<ScanUploadResult> UploadScansAsync(string trackingNumber, List<OfflineScanUpload> scans);

    /// <summary>
    /// Gets scan history for a shipment, ordered by scan time (newest first).
    /// </summary>
    Task<List<ScanDTO>> GetScansAsync(string trackingNumber);
}

/// <summary>
/// Represents a scan being uploaded from the offline app.
/// </summary>
public class OfflineScanUpload
{
    public string ScanId { get; set; } = string.Empty;  // Format: scan::{Guid}
    public string ScanType { get; set; } = string.Empty; // Pickup, InTransit, Delivered, Failed
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
    public string? Notes { get; set; }
}
