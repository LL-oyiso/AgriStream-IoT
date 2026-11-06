using System.Text.Json;
using System.Text.RegularExpressions;
using Agristream.Functions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Agristream.Functions.Functions;

/// <summary>
/// Function 3 of 4 (HTTP POST). The MVC app calls this when a sensor reports a problem or a
/// user clicks "Trigger irrigation". It validates the request and places an irrigation command
/// on the irrigation-commands storage queue using a queue output binding.
/// POST /api/irrigation
/// </summary>
public partial class QueueIrrigationCommandFunction
{
    [GeneratedRegex("^[a-zA-Z0-9-]{1,40}$")]
    private static partial Regex IdPattern();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger<QueueIrrigationCommandFunction> _logger;

    public QueueIrrigationCommandFunction(ILogger<QueueIrrigationCommandFunction> logger)
    {
        _logger = logger;
    }

    [Function("QueueIrrigationCommand")]
    public async Task<IrrigationOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "irrigation")] HttpRequest req,
        CancellationToken ct)
    {
        IrrigationRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<IrrigationRequest>(Json, ct);
        }
        catch (JsonException)
        {
            body = null;
        }

        var error = Validate(body);
        if (error is not null || body is null)
        {
            _logger.LogWarning("Irrigation request rejected: {Error}", error);
            return new IrrigationOutput
            {
                Http = new BadRequestObjectResult(new { error })
            };
        }

        var command = new IrrigationCommand(
            Guid.NewGuid(),
            body.DeviceId,
            body.FieldId,
            body.Action.ToLowerInvariant(),
            body.Action.Equals("stop", StringComparison.OrdinalIgnoreCase) ? 0 : body.DurationMinutes,
            string.IsNullOrWhiteSpace(body.RequestedBy) ? "system" : body.RequestedBy,
            body.Reason,
            DateTimeOffset.UtcNow);

        _logger.LogInformation("Queuing irrigation command {CommandId}: {Action} {DeviceId} in {FieldId} for {Minutes} min.",
            command.CommandId, command.Action, command.DeviceId, command.FieldId, command.DurationMinutes);

        return new IrrigationOutput
        {
            Message = JsonSerializer.Serialize(command, Json),
            Http = new AcceptedResult(string.Empty, new { commandId = command.CommandId, status = "Queued", command.EnqueuedUtc })
        };
    }

    private static string? Validate(IrrigationRequest? body)
    {
        if (body is null) return "A JSON body is required.";
        if (!IdPattern().IsMatch(body.DeviceId ?? string.Empty)) return "deviceId is required (letters, digits, hyphens).";
        if (!IdPattern().IsMatch(body.FieldId ?? string.Empty)) return "fieldId is required (letters, digits, hyphens).";
        if (body.Action is null || (!body.Action.Equals("start", StringComparison.OrdinalIgnoreCase)
                                    && !body.Action.Equals("stop", StringComparison.OrdinalIgnoreCase)))
            return "action must be 'start' or 'stop'.";
        if (body.Action.Equals("start", StringComparison.OrdinalIgnoreCase) && (body.DurationMinutes < 1 || body.DurationMinutes > 240))
            return "durationMinutes must be between 1 and 240.";
        if (body.Reason is { Length: > 200 }) return "reason must be 200 characters or fewer.";
        return null;
    }
}

/// <summary>One HTTP response plus one queue message (null message = nothing is queued).</summary>
public class IrrigationOutput
{
    [QueueOutput("irrigation-commands", Connection = "AzureWebJobsStorage")]
    public string? Message { get; set; }

    [HttpResult]
    public IActionResult? Http { get; set; }
}
