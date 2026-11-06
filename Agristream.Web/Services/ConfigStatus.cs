namespace Agristream.Web.Services;

/// <summary>
/// Reports which required app settings are missing, so the layout can show a warning instead of
/// the site crashing at startup (partial deployments stay debuggable).
/// </summary>
public class ConfigStatus
{
    private static readonly string[] Required =
    [
        SettingNames.Sql,
        SettingNames.Storage,
        SettingNames.FunctionBaseUrl,
        SettingNames.FunctionKey
    ];

    private readonly IConfiguration _config;

    public ConfigStatus(IConfiguration config)
    {
        _config = config;
    }

    public IReadOnlyList<string> Missing =>
        Required.Where(name => string.IsNullOrWhiteSpace(_config[name])).ToList();

    /// <summary>Short id of the App Service instance serving this request (shows scale-out).</summary>
    public static string InstanceId
    {
        get
        {
            var id = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName;
            return id.Length > 8 ? id[..8] : id;
        }
    }
}
