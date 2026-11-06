using Microsoft.Data.SqlClient;

namespace Agristream.Web.Services;

public record UserAccount(int UserId, string Username, string PasswordHash, string Role, DateTime CreatedUtc);

public interface IUserStore
{
    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken ct = default);
    /// <summary>Returns false when the username already exists.</summary>
    Task<bool> CreateAsync(string username, string passwordHash, string role, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default);
    Task UpdateRoleAsync(int userId, string role, CancellationToken ct = default);
}

public class SqlUserStore : IUserStore
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    private readonly SqlConnectionFactory _sql;

    public SqlUserStore(SqlConnectionFactory sql)
    {
        _sql = sql;
    }

    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT UserId, Username, PasswordHash, Role, CreatedUtc FROM dbo.Users WHERE Username = @username", connection);
        command.Parameters.AddWithValue("@username", username);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<bool> CreateAsync(string username, string passwordHash, string role, CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand(
            "INSERT INTO dbo.Users (Username, PasswordHash, Role) VALUES (@username, @hash, @role)", connection);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@hash", passwordHash);
        command.Parameters.AddWithValue("@role", role);

        try
        {
            await command.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT UserId, Username, PasswordHash, Role, CreatedUtc FROM dbo.Users ORDER BY UserId", connection);

        var users = new List<UserAccount>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            users.Add(Map(reader));
        }

        return users;
    }

    public async Task UpdateRoleAsync(int userId, string role, CancellationToken ct = default)
    {
        await using var connection = await _sql.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.Users SET Role = @role WHERE UserId = @id", connection);
        command.Parameters.AddWithValue("@role", role);
        command.Parameters.AddWithValue("@id", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static UserAccount Map(SqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4));
}
