using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Interface for uploading synced scans to the server.
/// </summary>
public interface IShipmentUploader
{
    /// <summary>
    /// Uploads scans to the server.
    /// Returns a result indicating accepted, skipped, and failed count.
    /// </summary>
    Task<UploadResult> UploadScansAsync(IEnumerable<ShipmentScan> scans, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a scan upload attempt.
/// </summary>
public class UploadResult
{
    public int AcceptedCount { get; set; }
    public int DuplicatesSkippedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<string> RejectionReasons { get; set; } = new();

    public int TotalProcessed => AcceptedCount + DuplicatesSkippedCount + RejectedCount;
}

/// <summary>
/// Manages the outbox of pending scans for synchronization with the server.
/// Implements the Outbox Pattern for offline persistence.
/// </summary>
public class OutboxService
{
    private readonly IScanRepository _repository;
    private readonly IShipmentUploader _uploader;
    private readonly ILogger<OutboxService>? _logger;

    public OutboxService(IScanRepository repository, IShipmentUploader uploader, ILogger<OutboxService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _logger = logger;
    }

    /// <summary>
    /// Processes all pending scans and attempts to sync them with the server.
    /// </summary>
    public async Task<UploadResult> SyncPendingScansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingScans = await _repository.GetPendingScansAsync();
            var pendingList = pendingScans.ToList();

            if (!pendingList.Any())
            {
                _logger?.LogInformation("No pending scans to sync");
                return new UploadResult();
            }

            _logger?.LogInformation("Syncing {Count} pending scans", pendingList.Count);

            // Upload pending scans
            var uploadResult = await _uploader.UploadScansAsync(pendingList, cancellationToken);

            // Update sync status based on result
            var acceptedScans = pendingList.Take(uploadResult.AcceptedCount);
            var skippedScans = pendingList.Skip(uploadResult.AcceptedCount).Take(uploadResult.DuplicatesSkippedCount);
            var failedScans = pendingList.Skip(uploadResult.AcceptedCount + uploadResult.DuplicatesSkippedCount);

            // Mark accepted and skipped as Synced
            foreach (var scan in acceptedScans.Union(skippedScans))
            {
                scan.SyncStatus = SyncStatus.Synced;
                await _repository.UpdateAsync(scan);
                _logger?.LogDebug("Marked scan {ScanId} as Synced", scan.Id);
            }

            // Mark failed as Failed
            foreach (var scan in failedScans)
            {
                scan.SyncStatus = SyncStatus.Failed;
                await _repository.UpdateAsync(scan);
                _logger?.LogWarning("Marked scan {ScanId} as Failed", scan.Id);
            }

            _logger?.LogInformation(
                "Sync completed: {Accepted} accepted, {Skipped} skipped, {Failed} failed",
                uploadResult.AcceptedCount, uploadResult.DuplicatesSkippedCount, uploadResult.RejectedCount);

            return uploadResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during pending scan sync");
            throw;
        }
    }

    /// <summary>
    /// Gets the count of pending scans awaiting synchronization.
    /// </summary>
    public async Task<int> GetPendingScanCountAsync()
    {
        try
        {
            var pendingScans = await _repository.GetPendingScansAsync();
            return pendingScans.Count();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting pending scan count");
            return 0;
        }
    }

    /// <summary>
    /// Retries a scan that previously failed to sync.
    /// </summary>
    public async Task<bool> RetryScanAsync(string scanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var scan = await _repository.GetByIdAsync(scanId);
            if (scan == null)
            {
                _logger?.LogWarning("Scan {ScanId} not found for retry", scanId);
                return false;
            }

            if (scan.SyncStatus == SyncStatus.Failed)
            {
                scan.SyncStatus = SyncStatus.Pending;
                await _repository.UpdateAsync(scan);
                _logger?.LogInformation("Reset scan {ScanId} status to Pending for retry", scanId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrying scan {ScanId}", scanId);
            return false;
        }
    }
}
