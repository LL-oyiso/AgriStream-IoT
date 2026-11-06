using Azure;
using Azure.Data.Tables;

namespace Agristream.Functions.Models;

/// <summary>
/// One sensor reading. PartitionKey = "{fieldId}_{yyyyMMdd}" spreads high-frequency writes over
/// one partition per field per day; RowKey = reverse ticks + sensor so the newest rows sort first.
/// </summary>
public class SensorReadingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string FieldId { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public double SoilMoisturePct { get; set; }
    public double SoilTempC { get; set; }
    public double Ph { get; set; }
    public double BatteryPct { get; set; }
    public bool IsAnomaly { get; set; }
    public DateTimeOffset ReadingUtc { get; set; }
}

/// <summary>Audit row written by the blob-triggered function for every drone image.</summary>
public class ImageAuditEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BlobName { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset UploadedUtc { get; set; }
    public DateTimeOffset ProcessedUtc { get; set; }
    public long DetectionLagMs { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Irrigation event written by the queue-triggered function.</summary>
public class IrrigationEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CommandId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset ProcessedUtc { get; set; }
}
