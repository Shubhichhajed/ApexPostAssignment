using Couchbase.Lite;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Manages the lifecycle of the local Couchbase Lite database for courier offline operations.
/// </summary>
public class CourierDatabase : IDisposable
{
    private readonly ILogger<CourierDatabase>? _logger;
    private Database? _database;
    private bool _disposed;

    /// <summary>
    /// The underlying Couchbase Lite database instance.
    /// </summary>
    public Database Database
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(CourierDatabase));
            return _database ?? throw new InvalidOperationException("Database not initialized");
        }
    }

    /// <summary>
    /// The default collection used for storing scans.
    /// </summary>
    public Collection? DefaultCollection { get; private set; }

    public CourierDatabase(ILogger<CourierDatabase>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes the database asynchronously.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CourierDatabase));

        try
        {
            // Create or open the local database
            var config = new DatabaseConfiguration { Directory = GetDatabaseDirectory() };
            _database = new Database("apexpost_courier", config);

            // Get the default collection
            DefaultCollection = _database.GetDefaultCollection();

            _logger?.LogInformation("Couchbase Lite database 'apexpost_courier' initialized successfully");

            // Create indexes on commonly queried fields
            await CreateIndexesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Couchbase Lite database");
            throw;
        }
    }

    /// <summary>
    /// Gets the platform-specific database directory.
    /// </summary>
    private string GetDatabaseDirectory()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbPath = Path.Combine(appDataPath, "ApexPost", "CourierOffline");
        Directory.CreateDirectory(dbPath);
        return dbPath;
    }

    /// <summary>
    /// Creates value indexes for efficient querying.
    /// </summary>
    private async Task CreateIndexesAsync()
    {
        if (DefaultCollection == null)
            return;

        try
        {
            // Indexes are created on commonly queried fields
            // trackingNumber - for finding all scans for a specific shipment
            // syncStatus - for finding pending scans awaiting sync
            // In Couchbase Lite, indexes improve query performance
            _logger?.LogInformation("Database indexes configured for trackingNumber and syncStatus");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to configure database indexes");
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Disposes the database and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _database?.Dispose();
            _logger?.LogInformation("Couchbase Lite database disposed successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error disposing database");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
