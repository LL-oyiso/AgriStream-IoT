using System.Diagnostics;
using Agristream.Web.Models;
using Azure.Data.Tables;

namespace Agristream.Web.Services;

public record TelemetrySurgeResult(int Readings, int Anomalies, int Batches, long ElapsedMs, int IrrigationQueued, int IrrigationFailed);

public record DroneBurstResult(int Uploaded, long ElapsedMs);

/// <summary>
/// Traffic simulator. A sensor surge writes batched readings straight into Table storage (the
/// pipeline has exactly four functions, so there is no ingest function), optionally calling the
/// POST function for low-moisture anomalies. A drone burst uploads many tiny images to Blob storage
/// so the blob-triggered function has real work to do.
/// </summary>
public class SimulatorService(StorageClients storage, IDeviceStore devices, IFunctionsClient functions, ImageService images)
{
    // A valid 1x1 PNG, so every simulated drone image is a real image file.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private const int MaxSensors = 500;
    private const int MaxReadingsPerSensor = 200;
    private const int MaxDroneImages = 200;

    public async Task<TelemetrySurgeResult> RunTelemetrySurgeAsync(
        string fieldId, int sensors, int readingsPerSensor, bool autoIrrigate, int maxIrrigation, string actor, CancellationToken ct)
    {
        sensors = Math.Clamp(sensors, 1, MaxSensors);
        readingsPerSensor = Math.Clamp(readingsPerSensor, 1, MaxReadingsPerSensor);

        var stopwatch = Stopwatch.StartNew();
        var table = storage.Table(StorageNames.SensorTable);
        var now = DateTimeOffset.UtcNow;
        var random = Random.Shared;
        var entities = new List<SensorReadingEntity>(sensors * readingsPerSensor);

        for (var s = 1; s <= sensors; s++)
        {
            var sensorId = $"S-{fieldId}-{s:D3}";
            var baseMoisture = 15 + random.NextDouble() * 50;

            for (var r = 0; r < readingsPerSensor; r++)
            {
                var readingUtc = now.AddSeconds(-r * 10);
                var moisture = Math.Clamp(baseMoisture + (random.NextDouble() - 0.5) * 6, 5, 80);

                entities.Add(new SensorReadingEntity
                {
                    PartitionKey = Partitioning.SensorPartition(fieldId, readingUtc),
                    RowKey = $"{Partitioning.ReverseTicks(readingUtc)}_{sensorId}_{r:D3}",
                    FieldId = fieldId,
                    SensorId = sensorId,
                    SoilMoisturePct = Math.Round(moisture, 1),
                    SoilTempC = Math.Round(14 + random.NextDouble() * 16, 1),
                    Ph = Math.Round(5.5 + random.NextDouble() * 2.0, 2),
                    BatteryPct = Math.Round(40 + random.NextDouble() * 60, 0),
                    IsAnomaly = moisture < 20,
                    ReadingUtc = readingUtc
                });
            }
        }

        // Table transactions: same partition, at most 100 entities each; run several in parallel.
        var batches = entities
            .GroupBy(e => e.PartitionKey)
            .SelectMany(g => g.Chunk(100))
            .ToList();

        using var gate = new SemaphoreSlim(8);
        var tasks = batches.Select(async batch =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await table.SubmitTransactionAsync(
                    batch.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)), ct);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        var anomalies = entities.Count(e => e.IsAnomaly);
        var queued = 0;
        var failed = 0;

        if (autoIrrigate && anomalies > 0 && maxIrrigation > 0)
        {
            var valves = (await devices.ListAsync(fieldId, ct)).Where(d => d.DeviceType == "Valve").ToList();
            if (valves.Count > 0)
            {
                var commands = Enumerable.Range(0, Math.Min(maxIrrigation, anomalies))
                    .Select(i => valves[i % valves.Count])
                    .Select(v => functions.TriggerIrrigationAsync(new IrrigationRequest(
                        v.DeviceId, fieldId, "start", 30, actor, "Low soil moisture detected by sensor surge"), ct));

                foreach (var result in await Task.WhenAll(commands))
                {
                    if (result.Success) queued++; else failed++;
                }
            }
        }

        stopwatch.Stop();
        return new TelemetrySurgeResult(entities.Count, anomalies, batches.Count, stopwatch.ElapsedMilliseconds, queued, failed);
    }

    public async Task<DroneBurstResult> RunDroneBurstAsync(string fieldId, int count, string actor, CancellationToken ct)
    {
        count = Math.Clamp(count, 1, MaxDroneImages);
        var stopwatch = Stopwatch.StartNew();

        using var gate = new SemaphoreSlim(8);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var tasks = Enumerable.Range(1, count).Select(async i =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var name = $"{fieldId}-{stamp}-sim{i:D4}-{Guid.NewGuid().ToString("N")[..6]}.png";
                await images.UploadBytesAsync(name, TinyPng, "image/png", fieldId, actor, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        stopwatch.Stop();
        return new DroneBurstResult(count, stopwatch.ElapsedMilliseconds);
    }
}
