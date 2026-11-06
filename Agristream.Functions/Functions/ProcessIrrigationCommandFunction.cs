using System.Text.Json;
using Agristream.Functions.Models;
using Agristream.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Agristream.Functions.Functions;

/// <summary>
/// Function 4 of 4 (queue trigger). Listens to the irrigation-commands queue, processes each
/// command, updates the device's operational status in the master registry table in Azure SQL
/// and records the event in Table storage.
/// </summary>
public class ProcessIrrigationCommandFunction
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDeviceRegistry _registry;
    private readonly TableStorageFactory _tables;
    private readonly ILogger<ProcessIrrigationCommandFunction> _logger;

    public ProcessIrrigationCommandFunction(
        IDeviceRegistry registry,
        TableStorageFactory tables,
        ILogger<ProcessIrrigationCommandFunction> logger)
    {
        _registry = registry;
        _tables = tables;
        _logger = logger;
    }

    [Function("ProcessIrrigationCommand")]
    public async Task Run(
        [QueueTrigger("irrigation-commands", Connection = "AzureWebJobsStorage")] string message,
        CancellationToken ct)
    {
        var command = JsonSerializer.Deserialize<IrrigationCommand>(message, Json)
            ?? throw new InvalidOperationException("Queue message was empty.");

        var processedUtc = DateTimeOffset.UtcNow;

        // Throws on SQL errors (for example throttling): the message becomes visible again and is
        // retried until maxDequeueCount, after which it moves to the poison queue.
        var outcome = await _registry.ApplyCommandAsync(command, processedUtc, ct);

        if (outcome == CommandOutcome.Duplicate)
        {
            _logger.LogWarning("Command {CommandId} was already processed; ignoring redelivery.", command.CommandId);
            return;
        }

        var events = await _tables.GetTableAsync(StorageNames.EventsTable, ct);
        await events.UpsertEntityAsync(new IrrigationEventEntity
        {
            PartitionKey = command.FieldId,
            RowKey = $"{Partitioning.ReverseTicks(processedUtc)}_{command.CommandId:N}",
            CommandId = command.CommandId.ToString(),
            DeviceId = command.DeviceId,
            Action = command.Action,
            DurationMinutes = command.DurationMinutes,
            RequestedBy = command.RequestedBy,
            Outcome = outcome.ToString(),
            ProcessedUtc = processedUtc
        }, cancellationToken: ct);

        _logger.LogInformation("Command {CommandId} processed: {Action} {DeviceId} -> {Outcome} (queue latency {LatencyMs} ms).",
            command.CommandId, command.Action, command.DeviceId, outcome,
            (long)(processedUtc - command.EnqueuedUtc).TotalMilliseconds);
    }
}
