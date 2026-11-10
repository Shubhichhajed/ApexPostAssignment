using Couchbase.Lite;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Couchbase Lite implementation of the scan repository.
/// </summary>
public class CouchbaseScanRepository : IScanRepository
{
    private readonly CourierDatabase _database;
    private readonly ILogger<CouchbaseScanRepository>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public CouchbaseScanRepository(CourierDatabase database, ILogger<CouchbaseScanRepository>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<ShipmentScan> CreateAsync(ShipmentScan scan)
    {
        if (scan == null)
            throw new ArgumentNullException(nameof(scan));

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var doc = new MutableDocument(scan.Id, DocumentToDict(scan));
            collection.Save(doc);

            _logger?.LogInformation("Created scan {ScanId} for tracking {TrackingNumber}",
                scan.Id, scan.TrackingNumber);

            return await Task.FromResult(scan);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create scan {ScanId}", scan.Id);
            throw;
        }
    }

    public async Task<ShipmentScan?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var doc = collection.GetDocument(id);
            return doc != null ? DictToDocument(doc.ToDictionary()) : null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve scan {ScanId}", id);
            throw;
        }
    }

    public async Task<ShipmentScan> UpdateAsync(ShipmentScan scan)
    {
        if (scan == null)
            throw new ArgumentNullException(nameof(scan));

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var doc = collection.GetDocument(scan.Id);
            if (doc == null)
                throw new KeyNotFoundException($"Scan {scan.Id} not found");

            var mutableDoc = doc.ToMutable();
            var dict = DocumentToDict(scan);

            foreach (var kvp in dict)
            {
                mutableDoc.SetValue(kvp.Key, kvp.Value);
            }

            collection.Save(mutableDoc);

            _logger?.LogInformation("Updated scan {ScanId}", scan.Id);

            return await Task.FromResult(scan);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update scan {ScanId}", scan.Id);
            throw;
        }
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var doc = collection.GetDocument(id);
            if (doc != null)
            {
                collection.Delete(doc);
                _logger?.LogInformation("Deleted scan {ScanId}", id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete scan {ScanId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<ShipmentScan>> ListAllAsync()
    {
        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var query = _database.Database.CreateQuery(
                "SELECT * FROM _");

            var results = query.Execute();
            var scans = new List<ShipmentScan>();

            foreach (var result in results)
            {
                var dict = result.GetDictionary(0);
                if (dict != null)
                {
                    scans.Add(DictToDocument(dict.ToDictionary()));
                }
            }

            return await Task.FromResult(scans.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to list all scans");
            throw;
        }
    }

    public async Task<IEnumerable<ShipmentScan>> GetScansByTrackingNumberAsync(string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
            return Enumerable.Empty<ShipmentScan>();

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            // SQL++ query with parameter: all scans for a tracking number, ordered by scanned time
            var query = _database.Database.CreateQuery(
                "SELECT * FROM _ WHERE trackingNumber = ? ORDER BY scannedAtUtc ASC");

            query.SetParameter("trackingNumber", trackingNumber);
            var results = query.Execute();

            var scans = new List<ShipmentScan>();
            foreach (var result in results)
            {
                var dict = result.GetDictionary(0);
                if (dict != null)
                {
                    scans.Add(DictToDocument(dict.ToDictionary()));
                }
            }

            _logger?.LogDebug("Retrieved {Count} scans for tracking {TrackingNumber}",
                scans.Count, trackingNumber);

            return await Task.FromResult(scans.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve scans for tracking {TrackingNumber}", trackingNumber);
            throw;
        }
    }

    public async Task<IEnumerable<ShipmentScan>> GetPendingScansAsync()
    {
        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            // SQL++ query: all scans where syncStatus = 'Pending'
            var query = _database.Database.CreateQuery(
                "SELECT * FROM _ WHERE syncStatus = ? ORDER BY scannedAtUtc ASC");

            query.SetParameter("status", nameof(SyncStatus.Pending));
            var results = query.Execute();

            var scans = new List<ShipmentScan>();
            foreach (var result in results)
            {
                var dict = result.GetDictionary(0);
                if (dict != null)
                {
                    scans.Add(DictToDocument(dict.ToDictionary()));
                }
            }

            _logger?.LogDebug("Retrieved {Count} pending scans", scans.Count);

            return await Task.FromResult(scans.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve pending scans");
            throw;
        }
    }

    public async Task<Dictionary<ScanType, int>> GetScanCountByCourierAsync(string courierId)
    {
        if (string.IsNullOrWhiteSpace(courierId))
            return new Dictionary<ScanType, int>();

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            // SQL++ query: count of scans per scanType for one courier
            var query = _database.Database.CreateQuery(
                @"SELECT scanType, COUNT(*) as count
                  FROM _ WHERE courierId = ?
                  GROUP BY scanType");

            query.SetParameter("courierId", courierId);
            var results = query.Execute();

            var counts = new Dictionary<ScanType, int>();
            foreach (var result in results)
            {
                var scanTypeStr = result.GetString("scanType");
                var count = result.GetInt("count");

                if (Enum.TryParse<ScanType>(scanTypeStr, out var scanType))
                {
                    counts[scanType] = count;
                }
            }

            _logger?.LogDebug("Retrieved scan counts by type for courier {CourierId}", courierId);

            return await Task.FromResult(counts);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get scan counts for courier {CourierId}", courierId);
            throw;
        }
    }

    public async Task<IEnumerable<ShipmentScan>> GetScansByCourierAsync(string courierId)
    {
        if (string.IsNullOrWhiteSpace(courierId))
            return Enumerable.Empty<ShipmentScan>();

        var collection = _database.DefaultCollection
            ?? throw new InvalidOperationException("Database not initialized");

        try
        {
            var query = _database.Database.CreateQuery(
                "SELECT * FROM _ WHERE courierId = ? ORDER BY scannedAtUtc DESC");

            query.SetParameter("courierId", courierId);
            var results = query.Execute();

            var scans = new List<ShipmentScan>();
            foreach (var result in results)
            {
                var dict = result.GetDictionary(0);
                if (dict != null)
                {
                    scans.Add(DictToDocument(dict.ToDictionary()));
                }
            }

            return await Task.FromResult(scans.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve scans for courier {CourierId}", courierId);
            throw;
        }
    }

    private Dictionary<string, object?> DocumentToDict(ShipmentScan scan)
    {
        return new Dictionary<string, object?>
        {
            { "id", scan.Id },
            { "trackingNumber", scan.TrackingNumber },
            { "scanType", scan.ScanType.ToString() },
            { "courierId", scan.CourierId },
            { "location", scan.Location },
            { "scannedAtUtc", scan.ScannedAtUtc.ToString("O") },
            { "notes", scan.Notes },
            { "syncStatus", scan.SyncStatus.ToString() }
        };
    }

    private ShipmentScan DictToDocument(Dictionary<string, object?> dict)
    {
        return new ShipmentScan
        {
            Id = dict.GetValueOrDefault("id")?.ToString() ?? string.Empty,
            TrackingNumber = dict.GetValueOrDefault("trackingNumber")?.ToString() ?? string.Empty,
            ScanType = Enum.Parse<ScanType>(dict.GetValueOrDefault("scanType")?.ToString() ?? "Pickup"),
            CourierId = dict.GetValueOrDefault("courierId")?.ToString() ?? string.Empty,
            Location = dict.GetValueOrDefault("location")?.ToString() ?? string.Empty,
            ScannedAtUtc = DateTime.Parse(dict.GetValueOrDefault("scannedAtUtc")?.ToString() ?? DateTime.UtcNow.ToString("O")),
            Notes = dict.GetValueOrDefault("notes")?.ToString(),
            SyncStatus = Enum.Parse<SyncStatus>(dict.GetValueOrDefault("syncStatus")?.ToString() ?? "Pending")
        };
    }
}

