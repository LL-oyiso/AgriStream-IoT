using System.Diagnostics;
using System.Text.RegularExpressions;
using Agristream.Functions.Models;
using Agristream.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Agristream.Functions.Functions;

/// <summary>
/// Function 2 of 4 (HTTP GET). The MVC app calls this when a farmer opens a field dashboard.
/// It returns the latest telemetry for the field straight from Table storage.
/// GET /api/telemetry/{fieldId}?limit=20
/// </summary>
public partial class GetLatestTelemetryFunction
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 200;

    [GeneratedRegex("^[a-zA-Z0-9-]{1,40}$")]
    private static partial Regex FieldIdPattern();

    private readonly TableStorageFactory _tables;
    private readonly ILogger<GetLatestTelemetryFunction> _logger;

    public GetLatestTelemetryFunction(TableStorageFactory tables, ILogger<GetLatestTelemetryFunction> logger)
    {
        _tables = tables;
        _logger = logger;
    }

    [Function("GetLatestTelemetry")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "telemetry/{fieldId}")] HttpRequest req,
        string fieldId,
        CancellationToken ct)
    {
        if (!FieldIdPattern().IsMatch(fieldId))
        {
            return new BadRequestObjectResult(new { error = "fieldId must be 1-40 letters, digits or hyphens." });
        }

        var limit = DefaultLimit;
        if (int.TryParse(req.Query["limit"], out var requested))
        {
            limit = Math.Clamp(requested, 1, MaxLimit);
        }

        var stopwatch = Stopwatch.StartNew();
        var table = await _tables.GetTableAsync(StorageNames.SensorTable, ct);
        var readings = new List<SensorReadingEntity>(limit);

        // Newest rows first: today's partition, then yesterday's if more rows are needed.
        var now = DateTimeOffset.UtcNow;
        foreach (var day in new[] { now, now.AddDays(-1) })
        {
            if (readings.Count >= limit)
            {
                break;
            }

            var partition = Partitioning.SensorPartition(fieldId, day);
            await foreach (var row in table.QueryAsync<SensorReadingEntity>(
                filter: $"PartitionKey eq '{partition}'", maxPerPage: limit, cancellationToken: ct))
            {
                readings.Add(row);
                if (readings.Count >= limit)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        _logger.LogInformation("Returned {Count} readings for {FieldId} in {ElapsedMs} ms.",
            readings.Count, fieldId, stopwatch.ElapsedMilliseconds);

        return new OkObjectResult(new
        {
            fieldId,
            count = readings.Count,
            anomalies = readings.Count(r => r.IsAnomaly),
            serverElapsedMs = stopwatch.ElapsedMilliseconds,
            generatedUtc = now,
            readings = readings.Select(r => new
            {
                r.SensorId,
                r.SoilMoisturePct,
                r.SoilTempC,
                r.Ph,
                r.BatteryPct,
                r.IsAnomaly,
                r.ReadingUtc
            })
        });
    }
}
