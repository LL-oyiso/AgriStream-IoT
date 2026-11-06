namespace Agristream.Web.Services;

/// <summary>App setting names (Azure Portal: Web App -> Environment variables).</summary>
public static class SettingNames
{
    public const string Storage = "StorageConnectionString";
    public const string Sql = "SqlConnectionString";
    public const string FunctionBaseUrl = "FunctionBaseUrl";
    public const string FunctionKey = "FunctionKey";
    public const string SimulatorKey = "SimulatorKey";
    public const string SeedAdminPassword = "SeedAdminPassword";
}

/// <summary>Fixed names of the storage resources shared with the Function App.</summary>
public static class StorageNames
{
    public const string ImagesContainer = "drone-images";
    public const string SensorTable = "SensorReadings";
    public const string AuditTable = "ImageAudit";
    public const string EventsTable = "IrrigationEvents";
    public const string IrrigationQueue = "irrigation-commands";
    public const string IrrigationPoisonQueue = "irrigation-commands-poison";
}

public static class Roles
{
    public const string Farmer = "Farmer";
    public const string Agronomist = "Agronomist";
    public const string Admin = "Admin";

    public static readonly string[] All = [Farmer, Agronomist, Admin];
    public static readonly string[] SelfRegister = [Farmer, Agronomist];
}
