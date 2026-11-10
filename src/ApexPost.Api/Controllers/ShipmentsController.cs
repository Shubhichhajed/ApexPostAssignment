using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Api.Models;
using ApexPost.Api.Services;

namespace ApexPost.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/shipments")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Produces("application/json")]
[Consumes("application/json")]
public class ShipmentsController : ControllerBase
{
    private readonly IShipmentService _shipmentService;
    private readonly ILogger<ShipmentsController> _logger;

    public ShipmentsController(IShipmentService shipmentService, ILogger<ShipmentsController> logger)
    {
        _shipmentService = shipmentService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PaginatedShipmentsResponse<ShipmentResponseV1>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [EndpointSummary("List Shipments")]
    [Tags("Shipments")]
    public async Task<ActionResult<PaginatedShipmentsResponse<ShipmentResponseV1>>> GetShipments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? city = null)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(new ProblemDetails { Title = "Invalid pagination" });

        try
        {
            var (items, total) = await _shipmentService.GetShipmentsAsync(page, pageSize, status, city);
            var totalPages = (int)Math.Ceiling((double)total / pageSize);
            return Ok(new PaginatedShipmentsResponse<ShipmentResponseV1>
            {
                Items = items.Select(MapToResponseV1).ToList(),
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving shipments");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpGet("{trackingNumber}", Name = "GetShipment")]
    [Authorize]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Get Shipment")]
    [Tags("Shipments")]
    public async Task<ActionResult<ShipmentResponseV1>> GetShipment(string trackingNumber)
    {
        try
        {
            var shipment = await _shipmentService.GetShipmentAsync(trackingNumber);
            if (shipment == null)
                return NotFound(new ProblemDetails { Title = "Not Found" });
            return Ok(MapToResponseV1(shipment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpPost(Name = "CreateShipment")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Create Shipment")]
    [Tags("Shipments")]
    public async Task<ActionResult<ShipmentResponseV1>> CreateShipment([FromBody] CreateShipmentRequest request)
    {
        try
        {
            var shipment = await _shipmentService.CreateShipmentAsync(
                request.TrackingNumber, request.Weight, request.RecipientName,
                request.RecipientPhone, request.RecipientAddress, request.RecipientCity, request.Notes);

            return CreatedAtAction("GetShipment", new { trackingNumber = request.TrackingNumber }, MapToResponseV1(shipment));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpPut("{trackingNumber}", Name = "UpdateShipment")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Update Shipment")]
    [Tags("Shipments")]
    public async Task<ActionResult<ShipmentResponseV1>> UpdateShipment(
        string trackingNumber, [FromBody] UpdateShipmentRequest request)
    {
        try
        {
            var shipment = await _shipmentService.UpdateShipmentAsync(
                trackingNumber, request.Weight, request.RecipientName,
                request.RecipientPhone, request.RecipientAddress, request.RecipientCity, request.Notes);
            return Ok(MapToResponseV1(shipment));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpPatch("{trackingNumber}/status", Name = "UpdateShipmentStatus")]
    [Authorize(Roles = "Courier")]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Update Status")]
    [Tags("Shipments")]
    public async Task<ActionResult<ShipmentResponseV1>> UpdateShipmentStatus(
        string trackingNumber, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var shipment = await _shipmentService.UpdateShipmentStatusAsync(trackingNumber, request.Status);
            return Ok(MapToResponseV1(shipment));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpDelete("{trackingNumber}", Name = "DeleteShipment")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Delete Shipment")]
    [Tags("Shipments")]
    public async Task<IActionResult> DeleteShipment(string trackingNumber)
    {
        try
        {
            await _shipmentService.DeleteShipmentAsync(trackingNumber);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpPost("{trackingNumber}/scans", Name = "UploadScans")]
    [Authorize(Roles = "Courier")]
    [ProducesResponseType(typeof(ScanUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Upload Scans")]
    [Tags("Scans")]
    public async Task<ActionResult<ScanUploadResponse>> UploadScans(
        string trackingNumber, [FromBody] ScanUploadRequest request)
    {
        try
        {
            var scans = request.Scans.Select(s => new OfflineScanUpload
            {
                ScanId = s.ScanId,
                ScanType = s.ScanType,
                Location = s.Location,
                ScannedAtUtc = s.ScannedAtUtc,
                Notes = s.Notes
            }).ToList();

            var result = await _shipmentService.UploadScansAsync(trackingNumber, scans);
            return Ok(new ScanUploadResponse
            {
                Accepted = result.Accepted,
                DuplicatesSkipped = result.DuplicatesSkipped,
                Rejected = result.Rejected
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    [HttpGet("{trackingNumber}/scans", Name = "GetScanHistory")]
    [Authorize]
    [ProducesResponseType(typeof(ScanHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Get Scan History")]
    [Tags("Scans")]
    public async Task<ActionResult<ScanHistoryResponse>> GetScanHistory(string trackingNumber)
    {
        try
        {
            var scans = await _shipmentService.GetScansAsync(trackingNumber);
            return Ok(new ScanHistoryResponse
            {
                TrackingNumber = trackingNumber,
                Scans = scans.Select(s => new ScanInfo
                {
                    Id = s.Id,
                    ScanType = s.ScanType,
                    Location = s.Location,
                    ScannedAtUtc = s.ScannedAtUtc,
                    Notes = s.Notes
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving");
            return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
        }
    }

    private ShipmentResponseV1 MapToResponseV1(ShipmentDTO dto)
    {
        return new ShipmentResponseV1
        {
            TrackingNumber = dto.TrackingNumber,
            Status = dto.Status,
            Weight = dto.Weight,
            RecipientName = dto.RecipientName,
            RecipientPhone = dto.RecipientPhone,
            RecipientAddress = dto.RecipientAddress,
            RecipientCity = dto.RecipientCity,
            Notes = dto.Notes,
            CreatedAtUtc = dto.CreatedAtUtc
        };
    }
}
