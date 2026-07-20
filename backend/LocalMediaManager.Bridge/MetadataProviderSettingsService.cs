using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetaTubeSettingsDto(
    bool Enabled,
    string BaseUrl,
    int TimeoutSeconds,
    bool DownloadImages,
    bool WriteNfo,
    bool AutoExecute,
    bool NonDestructive);

public sealed record JavBusSettingsDto(
    bool Enabled,
    int Priority,
    string BaseUrl,
    int TimeoutSeconds,
    int RetryCount,
    string Cookie,
    bool DownloadImages,
    bool FillMissingOnly);

public sealed record MetadataProviderContext(MetaTubeSettingsDto MetaTube, JavBusSettingsDto JavBus, string? PreferredSource = null)
{
    public int TimeoutSeconds(string provider) =>
        provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? JavBus.TimeoutSeconds : MetaTube.TimeoutSeconds;

    public bool DownloadImages(string provider) =>
        provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? JavBus.DownloadImages : MetaTube.DownloadImages;
}

public sealed record ProviderConnectionResult(bool Success, string Provider, string Message, long ElapsedMilliseconds);

public sealed class MetadataProviderSettingsService(string databasePath)
{
    public async Task<MetaTubeSettingsDto> ReadMetaTubeAsync()
    {
        MetaTubeSettingsDto defaults = SettingsDefaults.Unified.MetaTube;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.metatube.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return new(
            Bool(values, "metadata.metatube.enabled", defaults.Enabled),
            NormalizeBaseUrl(Text(values, "metadata.metatube.baseUrl", defaults.BaseUrl)),
            Math.Clamp(Int(values, "metadata.metatube.timeoutSeconds", defaults.TimeoutSeconds), 15, 180),
            Bool(values, "metadata.metatube.downloadImages", defaults.DownloadImages),
            true,
            Bool(values, "metadata.metatube.autoExecute", defaults.AutoExecute),
            Bool(values, "metadata.metatube.nonDestructive", defaults.NonDestructive));
    }

    public async Task<JavBusSettingsDto> ReadJavBusAsync()
    {
        JavBusSettingsDto defaults = SettingsDefaults.JavBus;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.javbus.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return new(
            Bool(values, "metadata.javbus.enabled", defaults.Enabled),
            Math.Clamp(Int(values, "metadata.javbus.priority", defaults.Priority), 1, 99),
            NormalizeBaseUrl(Text(values, "metadata.javbus.baseUrl", defaults.BaseUrl)),
            Math.Clamp(Int(values, "metadata.javbus.timeoutSeconds", defaults.TimeoutSeconds), 10, 180),
            Math.Clamp(Int(values, "metadata.javbus.retryCount", defaults.RetryCount), 0, 3),
            Text(values, "metadata.javbus.cookie", defaults.Cookie),
            Bool(values, "metadata.javbus.downloadImages", defaults.DownloadImages),
            true);
    }

    public async Task<MetaTubeSettingsDto> SaveMetaTubeAsync(MetaTubeSettingsDto input)
    {
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("MetaTube 地址必须是有效的 HTTP 或 HTTPS URL。");
        var clean = input with {
            BaseUrl = NormalizeBaseUrl(uri.ToString()),
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 15, 180),
            WriteNfo = true,
            NonDestructive = true,
        };
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await using var transaction = await connection.BeginTransactionAsync();
        await StoreAsync(connection, transaction, "metadata.metatube.enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.baseUrl", clean.BaseUrl, "string");
        await StoreAsync(connection, transaction, "metadata.metatube.timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, "metadata.metatube.downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.writeNfo", clean.WriteNfo, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.autoExecute", clean.AutoExecute, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.nonDestructive", true, "boolean");
        await transaction.CommitAsync();
        return clean;
    }

    public async Task<JavBusSettingsDto> SaveJavBusAsync(JavBusSettingsDto input)
    {
        JavBusSettingsDto clean = NormalizeJavBus(input);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await using var transaction = await connection.BeginTransactionAsync();
        await StoreJavBusAsync(connection, transaction, clean);
        await transaction.CommitAsync();
        return clean;
    }

    public static JavBusSettingsDto NormalizeJavBus(JavBusSettingsDto input)
    {
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("JavBus 地址必须是有效的 HTTP 或 HTTPS URL。");
        return input with {
            Priority = Math.Clamp(input.Priority, 1, 99),
            BaseUrl = NormalizeBaseUrl(uri.ToString()),
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 10, 180),
            RetryCount = Math.Clamp(input.RetryCount, 0, 3),
            Cookie = input.Cookie?.Trim() ?? "",
            FillMissingOnly = true,
        };
    }

    public static async Task StoreJavBusAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, JavBusSettingsDto clean)
    {
        await StoreAsync(connection, transaction, "metadata.javbus.enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, "metadata.javbus.priority", clean.Priority, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.baseUrl", clean.BaseUrl, "string");
        await StoreAsync(connection, transaction, "metadata.javbus.timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.retryCount", clean.RetryCount, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.cookie", clean.Cookie, "secret");
        await StoreAsync(connection, transaction, "metadata.javbus.downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, "metadata.javbus.fillMissingOnly", true, "boolean");
    }

    public static async Task StoreAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        string key, object value, string type)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static string NormalizeBaseUrl(string value) => value.Trim().TrimEnd('/') + "/";
    private static bool Bool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out string? raw) && JsonSerializer.Deserialize<bool>(raw) is bool value ? value : fallback;
    private static int Int(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out string? raw) && int.TryParse(raw, out int value) ? value : fallback;
    private static string Text(IReadOnlyDictionary<string, string> values, string key, string fallback) {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<string>(raw) ?? fallback; } catch (JsonException) { return fallback; }
    }
}
