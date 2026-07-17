using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record AppearanceSettingsDto(string ThemeMode);

public sealed record UnifiedSettingsDto(
    MetaTubeSettingsDto MetaTube,
    NfoSettingsDto Nfo,
    PlaybackSettingsDto Playback,
    RatingRetentionSettingsDto RatingRetention,
    AppearanceSettingsDto Appearance);

public sealed record UnifiedSettingsSaveResult(UnifiedSettingsDto Settings, IReadOnlyList<string> ChangedFields, string Message);

public sealed class SettingsSaveCoordinator(
    string databasePath,
    MetadataProviderSettingsService metadata,
    NfoService nfo,
    PlaybackSettingsService playback,
    RatingHistoryService ratings)
{
    public async Task<UnifiedSettingsDto> ReadAsync(CancellationToken token = default)
    {
        AppearanceSettingsDto appearance = await ReadAppearanceAsync(token);
        return new(
            await metadata.ReadMetaTubeAsync(),
            await nfo.ReadSettingsAsync(token),
            await playback.ReadAsync(token),
            await ratings.ReadSettingsAsync(token),
            appearance);
    }

    public static UnifiedSettingsDto Defaults() => SettingsDefaults.Unified;

    public async Task<UnifiedSettingsSaveResult> SaveAsync(UnifiedSettingsDto input, CancellationToken token = default)
    {
        UnifiedSettingsDto clean = Normalize(input);
        UnifiedSettingsDto before = await ReadAsync(token);
        IReadOnlyList<string> changed = ChangedFields(before, clean);
        if (changed.Count == 0) return new(clean, [], "设置没有变化。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await StoreAsync(connection, transaction, "metadata.metatube.enabled", clean.MetaTube.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.baseUrl", clean.MetaTube.BaseUrl, "string", token);
        await StoreAsync(connection, transaction, "metadata.metatube.timeoutSeconds", clean.MetaTube.TimeoutSeconds, "integer", token);
        await StoreAsync(connection, transaction, "metadata.metatube.downloadImages", clean.MetaTube.DownloadImages, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.writeNfo", clean.MetaTube.WriteNfo, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.autoExecute", clean.MetaTube.AutoExecute, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.nonDestructive", true, "boolean", token);
        await StoreAsync(connection, transaction, "nfo.export.policy", clean.Nfo.ExportPolicy, "string", token);
        await StoreAsync(connection, transaction, "nfo.export.outputDirectory", clean.Nfo.OutputDirectory, "string", token);
        await StoreAsync(connection, transaction, "nfo.import.fillEmptyOnly", true, "boolean", token);
        await StoreAsync(connection, transaction, "nfo.export.includeImages", clean.Nfo.IncludeImages, "boolean", token);
        await StoreAsync(connection, transaction, "playback.playerPath", clean.Playback.UseSystemDefault ? "" : clean.Playback.PlayerPath, "string", token);
        await StoreAsync(connection, transaction, "ratingHistory.enabled", clean.RatingRetention.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "appearance.themeMode", clean.Appearance.ThemeMode, "string", token);
        await transaction.CommitAsync(token);
        return new(clean, changed, "设置已保存。");
    }

    private UnifiedSettingsDto Normalize(UnifiedSettingsDto input)
    {
        if (!Uri.TryCreate(input.MetaTube.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("MetaTube 地址必须是有效的 HTTP 或 HTTPS URL。", nameof(input.MetaTube.BaseUrl));
        string nfoPolicy = input.Nfo.ExportPolicy is "SkipExisting" or "SeparateFile" ? input.Nfo.ExportPolicy : "SkipExisting";
        string nfoOutput = NormalizeDirectory(input.Nfo.OutputDirectory, "NFO 输出目录");
        string player = "";
        bool useSystemDefault = input.Playback.UseSystemDefault || string.IsNullOrWhiteSpace(input.Playback.PlayerPath);
        if (!useSystemDefault)
        {
            player = Path.GetFullPath(input.Playback.PlayerPath.Trim());
            if (!File.Exists(player) || !player.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("播放器路径必须指向现有的 Windows 可执行文件。", nameof(input.Playback.PlayerPath));
        }
        string theme = string.Equals(input.Appearance.ThemeMode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        return new(
            input.MetaTube with
            {
                BaseUrl = uri.ToString().Trim().TrimEnd('/') + "/",
                TimeoutSeconds = Math.Clamp(input.MetaTube.TimeoutSeconds, 15, 180),
                NonDestructive = true,
            },
            new(nfoPolicy, nfoOutput, true, input.Nfo.IncludeImages),
            new(player, useSystemDefault),
            new(input.RatingRetention.Enabled),
            new(theme));
    }

    private static string NormalizeDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string path = Path.GetFullPath(value.Trim());
        string? parent = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new ArgumentException($"{label}的上级目录不存在，无法创建或写入。", label);
        return path;
    }

    private async Task<AppearanceSettingsDto> ReadAppearanceAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='appearance.themeMode'", token);
        string theme = SettingsDefaults.Unified.Appearance.ThemeMode;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { theme = JsonSerializer.Deserialize<string>(raw) ?? theme; }
            catch (JsonException) { }
        }
        return new(string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark");
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

    private static async Task StoreAsync(SqliteConnection connection, SqliteTransaction transaction, string key, object value, string type, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt)
            VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private static IReadOnlyList<string> ChangedFields(UnifiedSettingsDto before, UnifiedSettingsDto after)
    {
        var changed = new List<string>();
        if (before.MetaTube != after.MetaTube) changed.Add("metaTube");
        if (before.Nfo != after.Nfo) changed.Add("nfo");
        if (before.Playback != after.Playback) changed.Add("playback");
        if (before.RatingRetention != after.RatingRetention) changed.Add("ratingRetention");
        if (before.Appearance != after.Appearance) changed.Add("appearance");
        return changed;
    }
}

public static class SettingsDefaults
{
    public static UnifiedSettingsDto Unified { get; } = new(
        new(true, "http://127.0.0.1:8080/", 30, true, false, true, true),
        new("SkipExisting", "", true, true),
        new("", true),
        new(true),
        new("dark"));
}
