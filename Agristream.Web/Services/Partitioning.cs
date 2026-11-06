namespace Agristream.Web.Services;

/// <summary>Key helpers so the web app and the functions agree on Table storage keys.</summary>
public static class Partitioning
{
    public static string SensorPartition(string fieldId, DateTimeOffset utc) =>
        $"{fieldId}_{utc.UtcDateTime:yyyyMMdd}";

    public static string ReverseTicks(DateTimeOffset utc) =>
        (DateTime.MaxValue.Ticks - utc.UtcDateTime.Ticks).ToString("D19");

    public static string AuditPartition(DateTimeOffset utc) => utc.UtcDateTime.ToString("yyyyMMdd");
}
