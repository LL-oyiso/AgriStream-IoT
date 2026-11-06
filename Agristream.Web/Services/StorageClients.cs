using System.Collections.Concurrent;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace Agristream.Web.Services;

/// <summary>
/// Builds storage clients from the StorageConnectionString app setting when first used, and
/// creates containers and tables on first use. Missing settings surface as a clear exception that
/// callers turn into a page warning.
/// </summary>
public class StorageClients
{
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, TableClient> _tables = new();
    private readonly object _containerLock = new();
    private BlobContainerClient? _images;

    public StorageClients(IConfiguration config)
    {
        _config = config;
    }

    private string ConnectionString =>
        _config[SettingNames.Storage] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{SettingNames.Storage} is not configured.");

    public BlobContainerClient Images()
    {
        if (_images is not null)
        {
            return _images;
        }

        lock (_containerLock)
        {
            if (_images is null)
            {
                var container = new BlobContainerClient(ConnectionString, StorageNames.ImagesContainer);
                container.CreateIfNotExists();
                _images = container;
            }
        }

        return _images;
    }

    public TableClient Table(string name) =>
        _tables.GetOrAdd(name, tableName =>
        {
            var table = new TableClient(ConnectionString, tableName);
            table.CreateIfNotExists();
            return table;
        });

    /// <summary>Read-only queue client used for backlog statistics (the web app never enqueues).</summary>
    public QueueClient Queue(string name) => new(ConnectionString, name);
}
