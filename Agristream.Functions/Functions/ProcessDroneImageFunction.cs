using Agristream.Functions.Models;
using Agristream.Functions.Services;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Agristream.Functions.Functions;

/// <summary>
/// Function 1 of 4 (blob trigger). Runs when a drone image lands in the drone-images container:
/// logs the upload details and writes a processing-status audit entry to Table storage.
/// </summary>
public class ProcessDroneImageFunction
{
    private readonly TableStorageFactory _tables;
    private readonly ILogger<ProcessDroneImageFunction> _logger;

    public ProcessDroneImageFunction(TableStorageFactory tables, ILogger<ProcessDroneImageFunction> logger)
    {
        _tables = tables;
        _logger = logger;
    }

    [Function("ProcessDroneImage")]
    public async Task Run(
        [BlobTrigger("drone-images/{name}", Connection = "AzureWebJobsStorage")] BlobClient blob,
        string name,
        CancellationToken ct)
    {
        var processedUtc = DateTimeOffset.UtcNow;
        var properties = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value;

        properties.Metadata.TryGetValue("fieldId", out var fieldId);
        properties.Metadata.TryGetValue("uploadedBy", out var uploadedBy);
        var uploadedUtc = properties.CreatedOn;

        _logger.LogInformation(
            "Drone image received: {BlobName}, {SizeBytes} bytes, {ContentType}, field {FieldId}, uploaded by {UploadedBy}.",
            name, properties.ContentLength, properties.ContentType, fieldId ?? "unknown", uploadedBy ?? "unknown");

        var audit = new ImageAuditEntity
        {
            PartitionKey = Partitioning.AuditPartition(processedUtc),
            RowKey = $"{Partitioning.ReverseTicks(processedUtc)}_{Guid.NewGuid():N}",
            BlobName = name,
            FieldId = fieldId ?? "unknown",
            UploadedBy = uploadedBy ?? "unknown",
            SizeBytes = properties.ContentLength,
            ContentType = properties.ContentType ?? string.Empty,
            UploadedUtc = uploadedUtc,
            ProcessedUtc = processedUtc,
            DetectionLagMs = (long)Math.Max(0, (processedUtc - uploadedUtc).TotalMilliseconds),
            Status = "Processed"
        };

        var table = await _tables.GetTableAsync(StorageNames.AuditTable, ct);
        await table.AddEntityAsync(audit, ct);

        _logger.LogInformation("Audit entry written for {BlobName} (status {Status}, lag {LagMs} ms).",
            name, audit.Status, audit.DetectionLagMs);
    }
}
