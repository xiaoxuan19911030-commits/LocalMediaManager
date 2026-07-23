using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record RenameSettingsDto(bool TrimTitle, bool RenameAfterFavorite, string InformationSeparator,
    string ListSeparator, string Template)
{
    public static RenameSettingsDto Default => new(true, false, " - ", " - ", "{VID}+{Label}+{ActorNames}");
}

public sealed class RenameSettingsService(string databasePath)
{
    private const string Key = "rename.settings";
    private static readonly string[] AllowedSeparators = [" - ", "-", "_", " ", "·", ",", "，"];

    public async Task<RenameSettingsDto> ReadAsync(CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key=$key";
        command.Parameters.AddWithValue("$key", Key);
        string? raw = Convert.ToString(await command.ExecuteScalarAsync(token));
        try { return Normalize(string.IsNullOrWhiteSpace(raw) ? RenameSettingsDto.Default : JsonSerializer.Deserialize<RenameSettingsDto>(raw) ?? RenameSettingsDto.Default); }
        catch (JsonException) { return RenameSettingsDto.Default; }
    }

    public async Task<RenameSettingsDto> SaveAsync(RenameSettingsDto input, CancellationToken token = default)
    {
        RenameSettingsDto clean = Normalize(input);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,'json',$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", Key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(clean));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
        return clean;
    }

    private static RenameSettingsDto Normalize(RenameSettingsDto input)
    {
        string info = AllowedSeparators.Contains(input.InformationSeparator) ? input.InformationSeparator : " - ";
        string list = AllowedSeparators.Contains(input.ListSeparator) ? input.ListSeparator : " - ";
        string template = string.IsNullOrWhiteSpace(input.Template) ? RenameSettingsDto.Default.Template : input.Template.Trim();
        return input with { InformationSeparator = info, ListSeparator = list, Template = template };
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
}
