using Xunit;
using Moq;
using ApexPost.Api.Services;
using Microsoft.Extensions.Logging;

namespace ApexPost.Api.Tests;

public class ShipmentServiceTests
{
    private readonly Mock<ApexPost.Infrastructure.Data.ApexPostDbContext> _dbContextMock;
    private readonly Mock<ILogger<ShipmentService>> _loggerMock;
    private readonly IShipmentService _service;

    public ShipmentServiceTests()
    {
        _dbContextMock = new Mock<ApexPost.Infrastructure.Data.ApexPostDbContext>();
        _loggerMock = new Mock<ILogger<ShipmentService>>();
        _service = new ShipmentService(_dbContextMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_ReturnsShipment()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        var weight = 5.5m;
        var recipientName = "John Doe";
        var recipientPhone = "+61234567890";
        var recipientAddress = "123 Main Street";
        var recipientCity = "Sydney";
        var notes = "Handle with care";

        // Act
        var result = await _service.CreateShipmentAsync(
            trackingNumber, weight, recipientName, recipientPhone, recipientAddress, recipientCity, notes);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(trackingNumber, result.TrackingNumber);
        Assert.Equal(weight, result.Weight);
        Assert.Equal(recipientName, result.RecipientName);
        Assert.Equal("Created", result.Status);
    }

    [Fact]
    public async Task CreateShipmentAsync_DuplicateTrackingNumber_ThrowsException()
    {
        // Arrange
        var trackingNumber = "APX-12345678";

        // Act & Assert
        // First creation should succeed
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        // Second creation with same tracking number should fail
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateShipmentAsync(
                trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task GetShipmentAsync_ExistingTracking_ReturnsShipment()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        // Act
        var result = await _service.GetShipmentAsync(trackingNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(trackingNumber, result.TrackingNumber);
    }

    [Fact]
    public async Task GetShipmentAsync_NonExistingTracking_ReturnsNull()
    {
        // Act
        var result = await _service.GetShipmentAsync("APX-00000000");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateShipmentStatusAsync_ValidTransition_Updates()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        // Act
        var result = await _service.UpdateShipmentStatusAsync(trackingNumber, "PickedUp");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("PickedUp", result.Status);
    }

    [Fact]
    public async Task UpdateShipmentStatusAsync_InvalidStatus_ThrowsException()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateShipmentStatusAsync(trackingNumber, "InvalidStatus"));

        Assert.Contains("Invalid status", ex.Message);
    }

    [Fact]
    public async Task DeleteShipmentAsync_ExistingShipment_Deletes()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        // Act
        await _service.DeleteShipmentAsync(trackingNumber);

        // Assert - verify shipment is deleted
        var result = await _service.GetShipmentAsync(trackingNumber);
        Assert.Null(result);
    }

    [Fact]
    public async Task UploadScansAsync_ValidScans_AcceptsAll()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scans = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "Pickup",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = null
            },
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "InTransit",
                Location = "Newcastle",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                Notes = null
            }
        };

        // Act
        var result = await _service.UploadScansAsync(trackingNumber, scans);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.DuplicatesSkipped);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public async Task UploadScansAsync_DuplicateScanIds_SkipsDuplicates()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scanId = "scan::" + Guid.NewGuid();
        var scans1 = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = scanId, 
                ScanType = "Pickup",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = null
            }
        };

        var scans2 = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = scanId, 
                ScanType = "Pickup",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = null
            }
        };

        // Act
        var result1 = await _service.UploadScansAsync(trackingNumber, scans1);
        var result2 = await _service.UploadScansAsync(trackingNumber, scans2);

        // Assert
        Assert.Equal(1, result1.Accepted);
        Assert.Equal(1, result2.DuplicatesSkipped);
    }

    [Fact]
    public async Task UploadScansAsync_FutureTimestamp_RejectsScan()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scans = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "Pickup",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(10), // Future timestamp
                Notes = null
            }
        };

        // Act
        var result = await _service.UploadScansAsync(trackingNumber, scans);

        // Assert
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Rejected);
    }

    [Fact]
    public async Task UploadScansAsync_DeliveredWithoutSignature_RejectsScan()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scans = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "Delivered",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = null // No signature
            }
        };

        // Act
        var result = await _service.UploadScansAsync(trackingNumber, scans);

        // Assert
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Rejected);
    }

    [Fact]
    public async Task UploadScansAsync_DeliveredWithSignature_Accepts()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scans = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "Delivered",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = "Signed by: John Smith"
            }
        };

        // Act
        var result = await _service.UploadScansAsync(trackingNumber, scans);

        // Assert
        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public async Task GetScansAsync_ExistingScans_ReturnsList()
    {
        // Arrange
        var trackingNumber = "APX-12345678";
        await _service.CreateShipmentAsync(
            trackingNumber, 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);

        var scans = new List<OfflineScanUpload>
        {
            new() 
            { 
                ScanId = "scan::" + Guid.NewGuid(), 
                ScanType = "Pickup",
                Location = "Sydney CBD",
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Notes = null
            }
        };

        await _service.UploadScansAsync(trackingNumber, scans);

        // Act
        var result = await _service.GetScansAsync(trackingNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Pickup", result[0].ScanType);
    }

    [Fact]
    public async Task GetShipmentsAsync_MultipleShipments_ReturnsPaginated()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _service.CreateShipmentAsync(
                $"APX-1234567{i}", 5.5m, "John Doe", "+61234567890", "123 Main St", "Sydney", null);
        }

        // Act
        var (items, total) = await _service.GetShipmentsAsync(1, 10, null, null);

        // Assert
        Assert.NotEmpty(items);
        Assert.True(total > 0);
    }
}
