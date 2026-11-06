using System.Collections.Concurrent;
using Azure.Data.Tables;

namespace Agristream.Functions.Services;

/// <summary>
/// Creates TableClients from the AzureWebJobsStorage setting (the same storage account the
/// triggers use). Tables are created on first use and cached, so a function instance only
/// pays the CreateIfNotExists cost once.
/// </summary>
public class TableStorageFactory
{
    private readonly TableServiceClient _service;
    private readonly ConcurrentDictionary<string, TableClient> _tables = new();

    public TableStorageFactory(string connectionString)
    {
        _service = new TableServiceClient(connectionString);
    }

    public async Task<TableClient> GetTableAsync(string name, CancellationToken ct = default)
    {
        if (_tables.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var table = _service.GetTableClient(name);
        await table.CreateIfNotExistsAsync(ct);
        _tables[name] = table;
        return table;
    }
}
