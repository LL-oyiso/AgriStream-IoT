namespace Agristream.Functions.Models;

/// <summary>Body of the POST request sent by the MVC app.</summary>
public class IrrigationRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    /// <summary>"start" or "stop".</summary>
    public string Action { get; set; } = "start";
    public int DurationMinutes { get; set; } = 30;
    public string RequestedBy { get; set; } = "system";
    public string? Reason { get; set; }
}

/// <summary>The message placed on the irrigation-commands queue.</summary>
public record IrrigationCommand(
    Guid CommandId,
    string DeviceId,
    string FieldId,
    string Action,
    int DurationMinutes,
    string RequestedBy,
    string? Reason,
    DateTimeOffset EnqueuedUtc);

public enum CommandOutcome
{
    Applied,
    UnknownDevice,
    Duplicate
}
