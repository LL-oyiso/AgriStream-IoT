namespace Agristream.Web.Services;

/// <summary>
/// Creates the 'admin' user on startup if the SeedAdminPassword app setting is set and the user
/// does not exist yet. Never throws: a missing database must not stop the site from starting.
/// </summary>
public class AdminSeeder : IHostedService
{
    private readonly IConfiguration _config;
    private readonly IUserStore _users;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(IConfiguration config, IUserStore users, ILogger<AdminSeeder> logger)
    {
        _config = config;
        _users = users;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var password = _config[SettingNames.SeedAdminPassword];
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(_config[SettingNames.Sql]))
        {
            return;
        }

        try
        {
            if (await _users.FindByUsernameAsync("admin", cancellationToken) is null)
            {
                await _users.CreateAsync("admin", PasswordHasher.Hash(password), Roles.Admin, cancellationToken);
                _logger.LogInformation("Seeded the 'admin' user.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin seeding skipped (is the SQL schema created?).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
