using Microsoft.Data.SqlClient;

namespace Agristream.Web.Services;

public record Device(
    string DeviceId,
    string FieldId,
    string DeviceType,
    string Name,
    string Status,
    Guid? LastCommandId,
    DateTime? LastCommandUtc,
    DateTime LastUpdatedUtc);

public interface IDeviceStore
{
    Task<IReadOnlyList<Device>> ListAsync(string? fieldId = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListFieldsAsync(CancellationToken ct = default);
}

public class SqlDeviceStore : IDeviceStore
{
    private readonly SqlConnectionFactory _sql;

    public SqlDeviceStore(SqlConnectionFactory sql)
    {
        _sql = sql;
    }

    public async Task<IReadOnlyList<Device>> ListAsync(string? fieldId = null, CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand(
            @"SELECT DeviceId, FieldId, DeviceType, Name, Status, LastCommandId, LastCommandUtc, LastUpdatedUtc
              FROM dbo.Devices
              WHERE (@field IS NULL OR FieldId = @field)
              ORDER BY FieldId, DeviceId", connection);
        command.Parameters.AddWithValue("@field", (object?)fieldId ?? DBNull.Value);

        var devices = new List<Device>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            devices.Add(new Device(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.GetDateTime(7)));
        }

        return devices;
    }

    public async Task<IReadOnlyList<string>> ListFieldsAsync(CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT DISTINCT FieldId FROM dbo.Devices ORDER BY FieldId", connection);

        var fields = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            fields.Add(reader.GetString(0));
        }

        return fields;
    }
}
