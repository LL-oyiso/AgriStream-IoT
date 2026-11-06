using Agristream.Web.Models;
using Microsoft.Data.SqlClient;

namespace Agristream.Web.Services;

/// <summary>
/// Gathers the evidence shown on the Evidence dashboard: queue backlog, storage counts, SQL results
/// and latency. It replaces Application Insights (not available on this subscription) and produces
/// the screenshots for the load-test sections. Each section fails independently.
/// </summary>
public class EvidenceService(SqlConnectionFactory sql, StorageClients storage, IDeviceStore devices)
{
    private const int ReadingCountCap = 10_000;
    private const int BlobCountCap = 5_000;

    public async Task<EvidenceViewModel> GetAsync(CancellationToken ct)
    {
        var model = new EvidenceViewModel { InstanceId = ConfigStatus.InstanceId };

        await Section(model, "Queue", async () =>
        {
            var queue = storage.Queue(StorageNames.IrrigationQueue);
            model.QueueBacklog = (await queue.GetPropertiesAsync(ct)).Value.ApproximateMessagesCount;
        });

        await Section(model, "Poison queue", async () =>
        {
            var poison = storage.Queue(StorageNames.IrrigationPoisonQueue);
            model.PoisonMessages = await poison.ExistsAsync(ct)
                ? (await poison.GetPropertiesAsync(ct)).Value.ApproximateMessagesCount
                : 0;
        });

        await Section(model, "Blob storage", async () =>
        {
            var count = 0;
            await foreach (var _ in storage.Images().GetBlobsAsync(cancellationToken: ct))
            {
                count++;
                if (count >= BlobCountCap)
                {
                    model.BlobCountCapped = true;
                    break;
                }
            }

            model.BlobCount = count;
        });

        await Section(model, "Sensor readings", async () =>
        {
            var table = storage.Table(StorageNames.SensorTable);
            foreach (var field in await devices.ListFieldsAsync(ct))
            {
                var partition = Partitioning.SensorPartition(field, DateTimeOffset.UtcNow);
                var count = 0;
                await foreach (var _ in table.QueryAsync<SensorReadingEntity>(
                    filter: $"PartitionKey eq '{partition}'", maxPerPage: 1000, select: ["RowKey"], cancellationToken: ct))
                {
                    count++;
                    if (count >= ReadingCountCap) break;
                }

                model.ReadingsByField.Add(new FieldReadingCount(field, count, count >= ReadingCountCap));
            }
        });

        await Section(model, "Image audit", async () =>
        {
            var table = storage.Table(StorageNames.AuditTable);
            var partition = Partitioning.AuditPartition(DateTimeOffset.UtcNow);
            var rows = new List<ImageAuditEntity>();
            await foreach (var row in table.QueryAsync<ImageAuditEntity>(
                filter: $"PartitionKey eq '{partition}'", maxPerPage: 200, cancellationToken: ct))
            {
                rows.Add(row);
                if (rows.Count >= 200) break;
            }

            model.RecentAudits = rows.Take(10)
                .Select(r => new AuditRow(r.BlobName, r.FieldId, r.Status, r.SizeBytes, r.ProcessedUtc, r.DetectionLagMs))
                .ToList();
            model.AvgAuditLagMs = rows.Count > 0 ? rows.Average(r => r.DetectionLagMs) : null;
        });

        await Section(model, "Azure SQL", async () =>
        {
            await using var connection = await sql.OpenAsync(ct);

            await using (var cmd = new SqlCommand("SELECT Role, COUNT(*) FROM dbo.Users GROUP BY Role ORDER BY Role", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    model.UsersByRole.Add(new RoleCount(reader.GetString(0), reader.GetInt32(1)));
            }

            await using (var cmd = new SqlCommand("SELECT Status, COUNT(*) FROM dbo.Devices GROUP BY Status ORDER BY Status", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    model.DevicesByStatus.Add(new StatusCount(reader.GetString(0), reader.GetInt32(1)));
            }

            await using (var cmd = new SqlCommand(
                @"SELECT COUNT(*), AVG(CAST(LatencyMs AS FLOAT)), MAX(LatencyMs),
                         SUM(CASE WHEN ProcessedUtc > DATEADD(MINUTE, -5, SYSUTCDATETIME()) THEN 1 ELSE 0 END),
                         MAX(ProcessedUtc)
                  FROM dbo.IrrigationCommandLog", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    var total = reader.GetInt32(0);
                    model.Commands = new CommandStats(
                        total,
                        total == 0 ? 0 : reader.GetDouble(1),
                        total == 0 ? 0 : reader.GetInt32(2),
                        total == 0 ? 0 : reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetDateTime(4));
                }
            }

            await using (var cmd = new SqlCommand(
                @"SELECT TOP 10 CommandId, DeviceId, Action, RequestedBy, Outcome, ProcessedUtc, LatencyMs
                  FROM dbo.IrrigationCommandLog ORDER BY ProcessedUtc DESC", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    model.RecentCommands.Add(new CommandRow(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetDateTime(5), reader.GetInt32(6)));
            }

            await using (var cmd = new SqlCommand(
                @"SELECT TOP 10 DATEADD(MINUTE, DATEDIFF(MINUTE, 0, ProcessedUtc), 0) AS MinuteUtc,
                         COUNT(*), AVG(CAST(LatencyMs AS FLOAT)), MAX(LatencyMs)
                  FROM dbo.IrrigationCommandLog
                  GROUP BY DATEADD(MINUTE, DATEDIFF(MINUTE, 0, ProcessedUtc), 0)
                  ORDER BY MinuteUtc DESC", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    model.PerMinute.Add(new MinuteRow(reader.GetDateTime(0), reader.GetInt32(1), reader.GetDouble(2), reader.GetInt32(3)));
            }
        });

        return model;
    }

    private static async Task Section(EvidenceViewModel model, string name, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            model.Errors.Add($"{name}: {ex.Message}");
        }
    }
}
