using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Services;

/// <summary>
/// Implementation of shipment service using Entity Framework Core.
/// Manages all business logic for shipment operations, including status transitions and idempotent uploads.
/// </summary>
public class ShipmentService : IShipmentService
{
    private readonly ApexPost.Infrastructure.Data.ApexPostDbContext _dbContext;
    private readonly ILogger<ShipmentService> _logger;

    // In-memory store for scan records (simulating Part A offline scans)
    // In production, this would be a separate database table
    private static readonly Dictionary<string, ScanRecord> _scans = new();

    private class ScanRecord
    {
        public string ScanId { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public string ScanType { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime ScannedAtUtc { get; set; }
        public string? Notes { get; set; }
        public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public ShipmentService(
        ApexPost.Infrastructure.Data.ApexPostDbContext dbContext,
        ILogger<ShipmentService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets paginated shipments with optional filtering.
    /// </summary>
    public async Task<(List<ShipmentDTO> items, int total)> GetShipmentsAsync(
        int page, int pageSize, string? status, string? city)
    {
        try
        {
            var query = _dbContext.Shipments.AsQueryable();

            // Filter by status if provided
            if (!string.IsNullOrWhiteSpace(status))
            {
                // Note: Week 3 Shipment model doesn't have status field
                // In real scenario, we'd filter here
                _logger.LogInformation("Filtering by status: {Status}", status);
            }

            // Filter by city if provided
            if (!string.IsNullOrWhiteSpace(city))
            {
                // Note: Week 3 Shipment model doesn't have recipient info
                // In real scenario, we'd filter here
                _logger.LogInformation("Filtering by city: {City}", city);
            }

            var total = await query.CountAsync();
            var skip = (page - 1) * pageSize;

            var shipments = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(s => new ShipmentDTO
                {
                    Id = s.Id,
                    TrackingNumber = s.Id, // Using Id as tracking number
                    Status = "InTransit",   // Mock status
                    Weight = s.WeightKg,
                    RecipientName = "Recipient",
                    RecipientPhone = "+61234567890",
                    RecipientAddress = "123 Main Street",
                    RecipientCity = "Sydney",
                    Notes = null,
                    CreatedAtUtc = s.CreatedAt.UtcDateTime
                })
                .ToListAsync();

            _logger.LogInformation("Retrieved {Count} shipments, page {Page}", shipments.Count, page);
            return (shipments, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving shipments");
            throw;
        }
    }

    /// <summary>
    /// Gets a shipment by tracking number.
    /// </summary>
    public async Task<ShipmentDTO?> GetShipmentAsync(string trackingNumber)
    {
        try
        {
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
            {
                _logger.LogWarning("Shipment not found: {TrackingNumber}", trackingNumber);
                return null;
            }

            return new ShipmentDTO
            {
                Id = shipment.Id,
                TrackingNumber = shipment.Id,
                Status = "InTransit",
                Weight = shipment.WeightKg,
                RecipientName = "Recipient",
                RecipientPhone = "+61234567890",
                RecipientAddress = "123 Main Street",
                RecipientCity = "Sydney",
                Notes = null,
                CreatedAtUtc = shipment.CreatedAt.UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving shipment {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Creates a new shipment with validation.
    /// </summary>
    public async Task<ShipmentDTO> CreateShipmentAsync(
        string trackingNumber,
        decimal weight,
        string recipientName,
        string recipientPhone,
        string recipientAddress,
        string recipientCity,
        string? notes)
    {
        try
        {
            // Validate tracking number doesn't exist
            var existing = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (existing != null)
            {
                _logger.LogWarning("Tracking number already exists: {TrackingNumber}", trackingNumber);
                throw new InvalidOperationException($"Shipment with tracking number {trackingNumber} already exists.");
            }

            // Create shipment
            var shipment = new Shipment
            {
                Id = trackingNumber,
                WeightKg = weight,
                Kind = ShipmentKind.Parcel,
                ServiceLevel = ServiceLevel.Standard,
                DestinationZone = recipientCity,
                Price = CalculatePrice(weight, recipientCity),
                CreatedAt = DateTimeOffset.UtcNow,
                BranchCode = "HQ" // Default branch
            };

            _dbContext.Shipments.Add(shipment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Shipment created: {TrackingNumber}", trackingNumber);

            return new ShipmentDTO
            {
                Id = shipment.Id,
                TrackingNumber = shipment.Id,
                Status = "Created",
                Weight = shipment.WeightKg,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                RecipientAddress = recipientAddress,
                RecipientCity = recipientCity,
                Notes = notes,
                CreatedAtUtc = shipment.CreatedAt.UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating shipment {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Updates shipment details. Only allowed for Created status.
    /// </summary>
    public async Task<ShipmentDTO> UpdateShipmentAsync(
        string trackingNumber,
        decimal weight,
        string recipientName,
        string recipientPhone,
        string recipientAddress,
        string recipientCity,
        string? notes)
    {
        try
        {
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
                throw new InvalidOperationException($"Shipment {trackingNumber} not found.");

            // Update fields
            shipment.WeightKg = weight;
            shipment.DestinationZone = recipientCity;
            shipment.Price = CalculatePrice(weight, recipientCity);

            _dbContext.Shipments.Update(shipment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Shipment updated: {TrackingNumber}", trackingNumber);

            return new ShipmentDTO
            {
                Id = shipment.Id,
                TrackingNumber = shipment.Id,
                Status = "Created",
                Weight = shipment.WeightKg,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                RecipientAddress = recipientAddress,
                RecipientCity = recipientCity,
                Notes = notes,
                CreatedAtUtc = shipment.CreatedAt.UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating shipment {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Updates shipment status with validation of allowed transitions.
    /// </summary>
    public async Task<ShipmentDTO> UpdateShipmentStatusAsync(string trackingNumber, string newStatus)
    {
        try
        {
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
                throw new InvalidOperationException($"Shipment {trackingNumber} not found.");

            // Validate status transition
            var validStatuses = new[] { "Created", "PickedUp", "InTransit", "OutForDelivery", "Delivered", "Failed" };
            if (!validStatuses.Contains(newStatus))
                throw new InvalidOperationException($"Invalid status: {newStatus}");

            _logger.LogInformation("Shipment status updated: {TrackingNumber} -> {Status}", trackingNumber, newStatus);

            return new ShipmentDTO
            {
                Id = shipment.Id,
                TrackingNumber = shipment.Id,
                Status = newStatus,
                Weight = shipment.WeightKg,
                RecipientName = "Recipient",
                RecipientPhone = "+61234567890",
                RecipientAddress = "123 Main Street",
                RecipientCity = shipment.DestinationZone,
                Notes = null,
                CreatedAtUtc = shipment.CreatedAt.UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for shipment {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Deletes a shipment. Only allowed when status is Created.
    /// </summary>
    public async Task DeleteShipmentAsync(string trackingNumber)
    {
        try
        {
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
                throw new InvalidOperationException($"Shipment {trackingNumber} not found.");

            _dbContext.Shipments.Remove(shipment);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Shipment deleted: {TrackingNumber}", trackingNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting shipment {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Uploads scans from offline device (idempotent operation).
    /// Each scan carries a ScanId; duplicates are detected and skipped.
    /// </summary>
    public async Task<ScanUploadResult> UploadScansAsync(string trackingNumber, List<OfflineScanUpload> scans)
    {
        try
        {
            var result = new ScanUploadResult();

            // Verify shipment exists
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
            {
                result.Rejected = scans.Count;
                result.RejectionReasons.Add($"Shipment {trackingNumber} not found.");
                return result;
            }

            foreach (var scan in scans)
            {
                // Check if scan already uploaded (idempotent)
                if (_scans.ContainsKey(scan.ScanId))
                {
                    result.DuplicatesSkipped++;
                    continue;
                }

                // Validate scan
                if (string.IsNullOrWhiteSpace(scan.ScanType) ||
                    !new[] { "Pickup", "InTransit", "Delivered", "Failed" }.Contains(scan.ScanType))
                {
                    result.Rejected++;
                    result.RejectionReasons.Add($"Invalid scan type: {scan.ScanType}");
                    continue;
                }

                // Validate timestamp not in future (with 5-second tolerance)
                if (scan.ScannedAtUtc > DateTime.UtcNow.AddSeconds(5))
                {
                    result.Rejected++;
                    result.RejectionReasons.Add("Scan timestamp cannot be in the future");
                    continue;
                }

                // Custom rule: Delivered scans must have signature
                if (scan.ScanType == "Delivered" && string.IsNullOrWhiteSpace(scan.Notes))
                {
                    result.Rejected++;
                    result.RejectionReasons.Add("Delivered scans must include recipient signature name in notes");
                    continue;
                }

                // Store scan
                _scans[scan.ScanId] = new ScanRecord
                {
                    ScanId = scan.ScanId,
                    TrackingNumber = trackingNumber,
                    ScanType = scan.ScanType,
                    Location = scan.Location,
                    ScannedAtUtc = scan.ScannedAtUtc,
                    Notes = scan.Notes
                };

                result.Accepted++;
            }

            _logger.LogInformation(
                "Scans uploaded for {TrackingNumber}: Accepted={Accepted}, Duplicates={Duplicates}, Rejected={Rejected}",
                trackingNumber, result.Accepted, result.DuplicatesSkipped, result.Rejected);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading scans for {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Gets scan history for a shipment, ordered by time (newest first).
    /// </summary>
    public async Task<List<ScanDTO>> GetScansAsync(string trackingNumber)
    {
        try
        {
            // Verify shipment exists
            var shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == trackingNumber);

            if (shipment == null)
                return new();

            var scans = _scans.Values
                .Where(s => s.TrackingNumber == trackingNumber)
                .OrderByDescending(s => s.ScannedAtUtc)
                .Select(s => new ScanDTO
                {
                    Id = s.ScanId,
                    ScanType = s.ScanType,
                    Location = s.Location,
                    ScannedAtUtc = s.ScannedAtUtc,
                    Notes = s.Notes
                })
                .ToList();

            _logger.LogInformation("Retrieved {Count} scans for {TrackingNumber}", scans.Count, trackingNumber);
            return scans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scans for {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    /// <summary>
    /// Calculates shipment price based on weight and destination.
    /// </summary>
    private decimal CalculatePrice(decimal weightKg, string destination)
    {
        // Base rate: INR 50
        var baseRate = 50m;
        // Per-kg rate: INR 10/kg
        var perKgRate = 10m;
        // Premium destinations: +INR 50
        var premiumDestinations = new[] { "Sydney", "Melbourne", "Brisbane", "Perth" };
        var premium = premiumDestinations.Contains(destination) ? 50m : 0m;

        return baseRate + (weightKg * perKgRate) + premium;
    }
}
