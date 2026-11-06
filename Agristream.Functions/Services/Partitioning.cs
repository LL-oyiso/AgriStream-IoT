namespace Agristream.Functions.Services;

/// <summary>Key helpers so the web app and the functions agree on Table storage keys.</summary>
public static class Partitioning
{
    /// <summary>One partition per field per day, e.g. "field-01_20261106".</summary>
    public static string SensorPartition(string fieldId, DateTimeOffset utc) =>
        $"{fieldId}_{utc.UtcDateTime:yyyyMMdd}";

    /// <summary>Reverse ticks sort ascending as newest first, so "latest N" is a cheap forward scan.</summary>
    public static string ReverseTicks(DateTimeOffset utc) =>
        (DateTime.MaxValue.Ticks - utc.UtcDateTime.Ticks).ToString("D19");

    public static string AuditPartition(DateTimeOffset utc) => utc.UtcDateTime.ToString("yyyyMMdd");
}
