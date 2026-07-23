using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record FfmpegPluginSettingsDto(
    string ExecutablePath,
    int ThreadCount,
    bool AutoScreenshotAfterLocalImport,
    bool SkipWhenScreenshotsExist,
    int CandidateCount,
    int RetainedCount,
    int MaximumAttempts,
    double SkipStartValue,
    string SkipStartUnit,
    double SkipEndValue,
    string SkipEndUnit,
    bool FilterNoPerson,
    bool FilterBlackFrames,
    bool FilterDarkFrames,
    bool FilterBlurredFrames,
    bool FilterDuplicateFrames)
{
    public static FfmpegPluginSettingsDto Default => new("", Math.Max(1, Environment.ProcessorCount / 2), false, true,
        12, 8, 30, 5, "Percent", 10, "Percent", true, true, true, true, true);
}

public sealed class FfmpegPluginSettingsService(string databasePath)
{
    private const string Key = "plugins.ffmpeg.settings";

    public async Task<FfmpegPluginSettingsDto> ReadAsync(CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key=$key";
        command.Parameters.AddWithValue("$key", Key);
        string? raw = Convert.ToString(await command.ExecuteScalarAsync(token));
        if (string.IsNullOrWhiteSpace(raw)) return FfmpegPluginSettingsDto.Default;
        try { return Normalize(JsonSerializer.Deserialize<FfmpegPluginSettingsDto>(raw) ?? FfmpegPluginSettingsDto.Default); }
        catch (JsonException) { return FfmpegPluginSettingsDto.Default; }
    }

    public async Task<FfmpegPluginSettingsDto> SaveAsync(FfmpegPluginSettingsDto input, CancellationToken token = default)
    {
        FfmpegPluginSettingsDto clean = Normalize(input);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,'json',$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType='json',UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", Key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(clean));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
        return clean;
    }

    internal static FfmpegPluginSettingsDto Normalize(FfmpegPluginSettingsDto input)
    {
        string path = string.IsNullOrWhiteSpace(input.ExecutablePath) ? "" : Path.GetFullPath(input.ExecutablePath.Trim());
        string startUnit = NormalizeUnit(input.SkipStartUnit);
        string endUnit = NormalizeUnit(input.SkipEndUnit);
        double start = ClampSkip(input.SkipStartValue, startUnit);
        double end = ClampSkip(input.SkipEndValue, endUnit);
        return input with {
            ExecutablePath = path,
            ThreadCount = Math.Clamp(input.ThreadCount, 1, 64),
            CandidateCount = Math.Clamp(input.CandidateCount, 6, 30),
            RetainedCount = Math.Clamp(input.RetainedCount, 1, 30),
            MaximumAttempts = Math.Clamp(input.MaximumAttempts, 6, 100),
            SkipStartValue = start,
            SkipStartUnit = startUnit,
            SkipEndValue = end,
            SkipEndUnit = endUnit,
        };
    }

    private static string NormalizeUnit(string? value) => value?.Equals("Minutes", StringComparison.OrdinalIgnoreCase) == true ? "Minutes" : "Percent";
    private static double ClampSkip(double value, string unit) => double.IsFinite(value) ? Math.Clamp(value, 0, unit == "Minutes" ? 240 : 95) : 0;

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync(token);
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(token);
        return connection;
    }
}
