using Couchbase.Lite;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Manages collection change notifications for real-time updates.
/// </summary>
public class ChangeListenerService
{
    private readonly CourierDatabase _database;
    private readonly IScanRepository _repository;
    private readonly ILogger<ChangeListenerService>? _logger;
    private ListenerToken? _listenerToken;
    private readonly object _lockObject = new();

    public ChangeListenerService(CourierDatabase database, IScanRepository repository,
        ILogger<ChangeListenerService>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    /// <summary>
    /// Registers a change listener that logs changes and tracks pending scans count.
    /// </summary>
    public event EventHandler<ChangeEventArgs>? DocumentChanged;

    /// <summary>
    /// Represents the arguments for a change event.
    /// </summary>
    public class ChangeEventArgs : EventArgs
    {
        public IReadOnlyList<string> ChangedDocumentIds { get; set; } = new List<string>();
        public int PendingScanCount { get; set; }
    }

    /// <summary>
    /// Starts listening for database changes.
    /// </summary>
    public async Task StartListeningAsync()
    {
        lock (_lockObject)
        {
            if (_listenerToken != null)
            {
                _logger?.LogWarning("Change listener already active");
                return;
            }

            var collection = _database.DefaultCollection;
            if (collection == null)
            {
                _logger?.LogError("Default collection not available for listener");
                return;
            }

            // Register the collection change listener
            _listenerToken = collection.AddChangeListener((changes) =>
            {
                try
                {
                    var changedIds = new List<string>();
                    if (changes != null)
                    {
                        foreach (var docId in changes.Keys)
                        {
                            changedIds.Add(docId);
                        }
                    }

                    _logger?.LogInformation("Collection changed: {Count} documents modified: {Ids}",
                        changedIds.Count, string.Join(", ", changedIds));

                    // Get updated pending scans count
                    var pendingCount = GetPendingScanCountSync();

                    // Raise event
                    DocumentChanged?.Invoke(this, new ChangeEventArgs
                    {
                        ChangedDocumentIds = changedIds.AsReadOnly(),
                        PendingScanCount = pendingCount
                    });

                    _logger?.LogInformation("Current pending scans count: {Count}", pendingCount);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing change notification");
                }
            });

            _logger?.LogInformation("Change listener registered for collection");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops listening for database changes and removes the listener token.
    /// </summary>
    public void StopListening()
    {
        lock (_lockObject)
        {
            if (_listenerToken == null)
                return;

            var collection = _database.DefaultCollection;
            if (collection != null && _listenerToken != null)
            {
                try
                {
                    collection.RemoveChangeListener(_listenerToken);
                    _logger?.LogInformation("Change listener removed");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error removing change listener");
                }
            }

            _listenerToken = null;
        }
    }

    /// <summary>
    /// Gets the current count of pending scans synchronously (for use in listener callback).
    /// </summary>
    private int GetPendingScanCountSync()
    {
        try
        {
            // This is a synchronous wrapper around the async call
            // In real scenarios, this should be handled differently
            var task = _repository.GetPendingScansAsync();
            task.Wait(TimeSpan.FromSeconds(5));
            return task.Result.Count();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting pending scan count");
            return -1;
        }
    }

    /// <summary>
    /// Cleans up the listener when the service is disposed.
    /// </summary>
    public void Dispose()
    {
        StopListening();
    }
}
