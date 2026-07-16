using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record PlaybackSettingsDto(string PlayerPath, bool UseSystemDefault);

public sealed class PlaybackSettingsService(string databasePath, string legacyConfigDatabasePath)
{
    public async Task<PlaybackSettingsDto> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key='playback.playerPath'";
        string raw = (await command.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "\"\"";
        string path = Deserialize(raw);
        if (string.IsNullOrWhiteSpace(path))
            path = await ReadLegacyPlayerAsync(cancellationToken) ?? "";
        return new(path, string.IsNullOrWhiteSpace(path));
    }

    public async Task<PlaybackSettingsDto> SaveAsync(
        PlaybackSettingsDto input,
        CancellationToken cancellationToken = default)
    {
        string path = input.UseSystemDefault || string.IsNullOrWhiteSpace(input.PlayerPath)
            ? ""
            : Path.GetFullPath(input.PlayerPath.Trim());
        if (path.Length > 0 && (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("播放器路径必须指向现有的 Windows 可执行文件。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt)
            VALUES('playback.playerPath',$value,'string',$at)
            ON CONFLICT(Key) DO UPDATE SET
                ValueJson=excluded.ValueJson,
                ValueType=excluded.ValueType,
                UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(path));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new(path, path.Length == 0);
    }

    private async Task<string?> ReadLegacyPlayerAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(legacyConfigDatabasePath))
            return null;
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = legacyConfigDatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT ConfigValue FROM app_configs WHERE ConfigName='WindowConfig.Settings' LIMIT 1";
            string? json = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
            JsonNode? node = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
            return node?["VideoPlayerPath"]?.GetValue<string>();
        }
        catch (SqliteException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
        }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static string Deserialize(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(raw) ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
