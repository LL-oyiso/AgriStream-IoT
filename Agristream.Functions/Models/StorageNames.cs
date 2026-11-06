namespace Agristream.Functions.Models;

/// <summary>Fixed names of the storage resources shared with the web app.</summary>
public static class StorageNames
{
    public const string ImagesContainer = "drone-images";
    public const string SensorTable = "SensorReadings";
    public const string AuditTable = "ImageAudit";
    public const string EventsTable = "IrrigationEvents";
    public const string IrrigationQueue = "irrigation-commands";
}
