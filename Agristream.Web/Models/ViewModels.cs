using Agristream.Web.Services;

namespace Agristream.Web.Models;

public class LoginViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Farmer;
}

public class DashboardViewModel
{
    public string FieldId { get; set; } = "field-01";
    public IReadOnlyList<string> Fields { get; set; } = [];
    public TelemetryResult? Telemetry { get; set; }
    public IReadOnlyList<Device> Devices { get; set; } = [];
    public string? DeviceError { get; set; }
    public bool CanTriggerIrrigation { get; set; }
    public string InstanceId { get; set; } = string.Empty;
}

public class ImagesViewModel
{
    public IReadOnlyList<ImageItem> Images { get; set; } = [];
    public IReadOnlyList<string> Fields { get; set; } = [];
    public string? Error { get; set; }
}

public class UsersViewModel
{
    public IReadOnlyList<UserAccount> Users { get; set; } = [];
    public string? Error { get; set; }
}

public class SimulatorViewModel
{
    public IReadOnlyList<string> Fields { get; set; } = [];
    public TelemetrySurgeResult? LastSurge { get; set; }
    public DroneBurstResult? LastBurst { get; set; }
}

// ---- Evidence dashboard ----

public record RoleCount(string Role, int Users);
public record StatusCount(string Status, int Devices);
public record FieldReadingCount(string FieldId, int Readings, bool Capped);

public record CommandStats(int Total, double AvgLatencyMs, int MaxLatencyMs, int LastFiveMinutes, DateTime? LastProcessedUtc);

public record CommandRow(Guid CommandId, string DeviceId, string Action, string RequestedBy, string Outcome, DateTime ProcessedUtc, int LatencyMs);

public record MinuteRow(DateTime MinuteUtc, int Commands, double AvgLatencyMs, int MaxLatencyMs);

public record AuditRow(string BlobName, string FieldId, string Status, long SizeBytes, DateTimeOffset ProcessedUtc, long DetectionLagMs);

public class EvidenceViewModel
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string InstanceId { get; set; } = string.Empty;

    // Storage Queue
    public long? QueueBacklog { get; set; }
    public long? PoisonMessages { get; set; }

    // Blob and Table storage
    public int? BlobCount { get; set; }
    public bool BlobCountCapped { get; set; }
    public List<FieldReadingCount> ReadingsByField { get; set; } = [];
    public List<AuditRow> RecentAudits { get; set; } = [];
    public double? AvgAuditLagMs { get; set; }

    // Azure SQL
    public List<RoleCount> UsersByRole { get; set; } = [];
    public List<StatusCount> DevicesByStatus { get; set; } = [];
    public CommandStats? Commands { get; set; }
    public List<CommandRow> RecentCommands { get; set; } = [];
    public List<MinuteRow> PerMinute { get; set; } = [];

    public List<string> Errors { get; set; } = [];
}
