using Agristream.Functions.Models;
using Microsoft.Data.SqlClient;

namespace Agristream.Functions.Services;

public interface IDeviceRegistry
{
    /// <summary>
    /// Applies an irrigation command to the master device registry and records it in the
    /// command log, in one transaction. A redelivered message (same CommandId) is ignored.
    /// </summary>
    Task<CommandOutcome> ApplyCommandAsync(IrrigationCommand command, DateTimeOffset processedUtc, CancellationToken ct);
}

public class SqlDeviceRegistry : IDeviceRegistry
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    private readonly string _connectionString;

    public SqlDeviceRegistry(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<CommandOutcome> ApplyCommandAsync(IrrigationCommand command, DateTimeOffset processedUtc, CancellationToken ct)
    {
        var newStatus = command.Action.Equals("start", StringComparison.OrdinalIgnoreCase) ? "Irrigating" : "Idle";
        var latencyMs = (int)Math.Max(0, (processedUtc - command.EnqueuedUtc).TotalMilliseconds);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            // 1. Update the device's operational status in the master registry.
            int rows;
            await using (var update = new SqlCommand(
                @"UPDATE dbo.Devices
                  SET Status = @status, LastCommandId = @commandId,
                      LastCommandUtc = @processed, LastUpdatedUtc = @processed
                  WHERE DeviceId = @deviceId", connection, transaction))
            {
                update.Parameters.AddWithValue("@status", newStatus);
                update.Parameters.AddWithValue("@commandId", command.CommandId);
                update.Parameters.AddWithValue("@processed", processedUtc.UtcDateTime);
                update.Parameters.AddWithValue("@deviceId", command.DeviceId);
                rows = await update.ExecuteNonQueryAsync(ct);
            }

            var outcome = rows > 0 ? CommandOutcome.Applied : CommandOutcome.UnknownDevice;

            // 2. Record the command. CommandId is the primary key, which makes the whole
            //    operation idempotent for at-least-once queue delivery.
            await using (var insert = new SqlCommand(
                @"INSERT INTO dbo.IrrigationCommandLog
                    (CommandId, DeviceId, FieldId, Action, DurationMinutes, RequestedBy, Reason,
                     EnqueuedUtc, ProcessedUtc, Outcome, LatencyMs)
                  VALUES
                    (@commandId, @deviceId, @fieldId, @action, @duration, @requestedBy, @reason,
                     @enqueued, @processed, @outcome, @latency)", connection, transaction))
            {
                insert.Parameters.AddWithValue("@commandId", command.CommandId);
                insert.Parameters.AddWithValue("@deviceId", command.DeviceId);
                insert.Parameters.AddWithValue("@fieldId", command.FieldId);
                insert.Parameters.AddWithValue("@action", command.Action);
                insert.Parameters.AddWithValue("@duration", command.DurationMinutes);
                insert.Parameters.AddWithValue("@requestedBy", command.RequestedBy);
                insert.Parameters.AddWithValue("@reason", (object?)command.Reason ?? DBNull.Value);
                insert.Parameters.AddWithValue("@enqueued", command.EnqueuedUtc.UtcDateTime);
                insert.Parameters.AddWithValue("@processed", processedUtc.UtcDateTime);
                insert.Parameters.AddWithValue("@outcome", outcome.ToString());
                insert.Parameters.AddWithValue("@latency", latencyMs);
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return outcome;
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
        {
            // Already processed (redelivery): undo the device update and report a duplicate.
            await transaction.RollbackAsync(ct);
            return CommandOutcome.Duplicate;
        }
    }
}
