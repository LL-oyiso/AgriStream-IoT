using Microsoft.Data.SqlClient;

namespace Agristream.Web.Services;

public class SqlConnectionFactory
{
    private readonly IConfiguration _config;

    public SqlConnectionFactory(IConfiguration config)
    {
        _config = config;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connectionString = _config[SettingNames.Sql];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{SettingNames.Sql} is not configured.");
        }

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
