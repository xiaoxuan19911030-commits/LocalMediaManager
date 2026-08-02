using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record DataSafetyOverviewDto(string DatabasePath, long DatabaseBytes, string ConfigDatabasePath,
    string BackupDirectory, string CacheDirectory, string LogDirectory, string? LastBackupAt);
public sealed record BackupCreateCommand(bool IncludeConfig = true, bool IncludeGeneratedCache = false);
public sealed record BackupResultDto(string BackupPath, long Bytes, string CreatedAt, IReadOnlyList<string> Included, IReadOnlyList<string> Warnings);
public sealed record BackupValidationDto(bool Valid, string BackupPath, long Bytes, string CreatedAt, bool HasDatabase,
    bool HasConfig, IReadOnlyList<string> Entries, IReadOnlyList<string> Errors);
public sealed record RestorePlanCommand(string BackupPath, string Mode);
public sealed record RestorePlanDto(string PlanPath, string BackupPath, string Mode, string CreatedAt, IReadOnlyList<string> Steps, IReadOnlyList<string> Warnings);
public sealed record SettingsExportDto(string ExportedAt, string Product, string Version, JsonElement Settings);
public sealed record SettingsImportPreviewDto(bool Valid, string Version, IReadOnlyList<string> Categories, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings);
public sealed record SystemDiagnosticDto(string CheckedAt, IReadOnlyList<DiagnosticCheckDto> Checks, IReadOnlyList<string> RecentLogs);
public sealed record DiagnosticCheckDto(string Key, string Label, string Status, string Detail);

public sealed class DataSafetyService(string databasePath, string configDatabasePath, string imageRoot)
{
    private string DataRoot => Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
    private string BackupRoot => Path.Combine(DataRoot, "backups");
    private string CacheRoot => Path.Combine(imageRoot, ".lmm-cache");
    private string LogRoot => Path.Combine(Path.GetDirectoryName(DataRoot) ?? DataRoot, "logs");

    public Task<DataSafetyOverviewDto> OverviewAsync()
    {
        Directory.CreateDirectory(BackupRoot);
        string? last = Directory.EnumerateFiles(BackupRoot, "*.zip").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max().ToString("O");
        if (!Directory.EnumerateFiles(BackupRoot, "*.zip").Any()) last = null;
        return Task.FromResult(new DataSafetyOverviewDto(databasePath, Size(databasePath), configDatabasePath, BackupRoot, CacheRoot, LogRoot, last));
    }

    public async Task<BackupResultDto> CreateBackupAsync(BackupCreateCommand command, CancellationToken token = default)
    {
        Directory.CreateDirectory(BackupRoot);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string temp = Path.Combine(BackupRoot, $".lmm-backup-{stamp}.tmp");
        string databaseSnapshot = Path.Combine(BackupRoot, $".lmm-database-{stamp}.sqlite");
        string final = Path.Combine(BackupRoot, $"lmm-backup-{stamp}.zip");
        var included = new List<string>();
        var warnings = new List<string>();
        if (File.Exists(temp)) File.Delete(temp);
        if (File.Exists(databaseSnapshot)) File.Delete(databaseSnapshot);
        try {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                if (File.Exists(databasePath)) {
                    await CreateConsistentDatabaseSnapshotAsync(databasePath, databaseSnapshot, token);
                    await AddFileAsync(archive, databaseSnapshot, "database/LocalMediaManager.db", token);
                    included.Add("database");
                }
                else warnings.Add("数据库文件不存在，未包含数据库。");
                if (command.IncludeConfig && File.Exists(configDatabasePath)) { await AddFileAsync(archive, configDatabasePath, "config/app_configs.sqlite", token); included.Add("config"); }
                if (command.IncludeGeneratedCache && Directory.Exists(CacheRoot)) {
                    foreach (string file in Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories).Take(5000)) {
                        string rel = Path.GetRelativePath(CacheRoot, file).Replace('\\', '/');
                        await AddFileAsync(archive, file, $"cache/{rel}", token);
                    }
                    included.Add("generated-cache");
                }
                var manifest = new {
                    product = "Local Media Manager",
                    version = "0.7.8",
                    createdAt = DateTime.UtcNow,
                    includes = included,
                    excludes = new[] { "original media files", "original images", "cookies", "tokens" },
                };
                await AddTextAsync(archive, "manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), token);
            }
            File.Move(temp, final, true);
            await PruneBackupsAsync(await ReadRetentionCountAsync(token), token);
            return new(final, Size(final), DateTime.UtcNow.ToString("O"), included, warnings);
        } catch {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
        finally {
            if (File.Exists(databaseSnapshot)) File.Delete(databaseSnapshot);
        }
    }

    private static async Task CreateConsistentDatabaseSnapshotAsync(string sourcePath, string targetPath,
        CancellationToken token)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await source.OpenAsync(token);
        await target.OpenAsync(token);
        token.ThrowIfCancellationRequested();
        source.BackupDatabase(target);
        await using SqliteCommand integrity = target.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        string result = Convert.ToString(await integrity.ExecuteScalarAsync(token)) ?? string.Empty;
        if (!result.Equals("ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite 备份完整性检查失败：{result}");
    }

    public async Task<BackupValidationDto> ValidateBackupAsync(string backupPath, CancellationToken token = default)
    {
        var entries = new List<string>();
        var errors = new List<string>();
        if (!File.Exists(backupPath))
            return new(false, backupPath, 0, "", false, false, [], ["备份文件不存在。"]);
        bool hasDatabase = false, hasConfig = false;
        try {
            using var archive = ZipFile.OpenRead(backupPath);
            foreach (var entry in archive.Entries) {
                token.ThrowIfCancellationRequested();
                entries.Add(entry.FullName);
                hasDatabase |= entry.FullName.Equals("database/LocalMediaManager.db", StringComparison.OrdinalIgnoreCase);
                hasConfig |= entry.FullName.Equals("config/app_configs.sqlite", StringComparison.OrdinalIgnoreCase);
            }
            if (!hasDatabase) errors.Add("备份中缺少数据库。");
            if (hasDatabase) await ValidateDatabaseEntryAsync(archive, errors, token);
        } catch (Exception ex) {
            errors.Add(ex.Message);
        }
        return new(errors.Count == 0, backupPath, Size(backupPath), File.GetCreationTimeUtc(backupPath).ToString("O"), hasDatabase, hasConfig, entries, errors);
    }

    public async Task<RestorePlanDto> CreateRestorePlanAsync(RestorePlanCommand command, CancellationToken token = default)
    {
        var validation = await ValidateBackupAsync(command.BackupPath, token);
        if (!validation.Valid) throw new InvalidOperationException("备份校验未通过，不能创建恢复计划。");
        string mode = NormalizeRestoreMode(command.Mode);
        string planPath = Path.Combine(BackupRoot, $"restore-plan-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(BackupRoot);
        var steps = new[] { "重启应用", "启动时创建当前状态安全备份", "校验备份包", $"按 {mode} 模式替换目标文件", "执行 SQLite 完整性检查" };
        var warnings = new[] { "数据库会被替换。", "当前未保存操作可能丢失。", "应用可能需要重启后完成恢复。" };
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new { command.BackupPath, mode, createdAt = DateTime.UtcNow, steps, warnings }, new JsonSerializerOptions { WriteIndented = true }), token);
        return new(planPath, command.BackupPath, mode, DateTime.UtcNow.ToString("O"), steps, warnings);
    }

    private static string NormalizeRestoreMode(string? mode)
    {
        return (mode ?? "").Trim().ToLowerInvariant() switch {
            "all" => "all",
            "database" or "database-only" => "database",
            "settings" or "settings-only" or "config" or "config-only" => "settings",
            _ => throw new ArgumentException("恢复模式无效。"),
        };
    }

    public async Task<SettingsExportDto> ExportSettingsAsync(MetaTubeSettingsDto metaTube)
    {
        var snapshot = await SettingsReader.ReadAsync(configDatabasePath, metaTube);
        var safe = new {
            fields = snapshot.Fields.Where(field => !field.Sensitive).Select(field => new { field.Key, field.Category, field.ValueType, field.Value, field.DefaultValue, field.Mapped }),
            metaTube = metaTube with { BaseUrl = metaTube.BaseUrl },
        };
        return new(DateTime.UtcNow.ToString("O"), "Local Media Manager", "0.7.8", JsonSerializer.SerializeToElement(safe));
    }

    public Task<SettingsImportPreviewDto> PreviewSettingsImportAsync(JsonElement payload)
    {
        var warnings = new List<string>();
        var changes = new List<string>();
        var categories = new HashSet<string>();
        if (!payload.TryGetProperty("settings", out JsonElement settings) && payload.TryGetProperty("Settings", out JsonElement upper)) settings = upper;
        if (settings.ValueKind == JsonValueKind.Undefined) settings = payload;
        if (settings.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement field in fields.EnumerateArray()) {
                string key = field.TryGetProperty("key", out JsonElement keyEl) ? keyEl.GetString() ?? "" : "";
                string category = field.TryGetProperty("category", out JsonElement catEl) ? catEl.GetString() ?? "unknown" : "unknown";
                if (!string.IsNullOrWhiteSpace(key)) changes.Add(key);
                categories.Add(category);
            }
        } else {
            warnings.Add("未发现可识别的 fields 数组。");
        }
        if (JsonSerializer.Serialize(payload).Contains("token", StringComparison.OrdinalIgnoreCase))
            warnings.Add("导入内容疑似包含敏感字段，预览不会应用这些内容。");
        return Task.FromResult(new SettingsImportPreviewDto(changes.Count > 0, "0.7.8", categories.Order().ToList(), changes.Take(100).ToList(), warnings));
    }

    public async Task<SystemDiagnosticDto> DiagnosticsAsync(CancellationToken token = default)
    {
        var checks = new List<DiagnosticCheckDto> {
            Check("database", "数据库可访问", File.Exists(databasePath), databasePath),
            Check("config", "配置可读取", File.Exists(configDatabasePath), configDatabasePath),
            Check("cache", "缓存目录可写", CanWriteDirectory(CacheRoot), CacheRoot),
            Check("logs", "日志目录可写", CanWriteDirectory(LogRoot), LogRoot),
        };
        try {
            await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await c.OpenAsync(token);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            string result = Convert.ToString(await cmd.ExecuteScalarAsync(token)) ?? "unknown";
            checks.Add(Check("integrity", "SQLite 完整性", result.Equals("ok", StringComparison.OrdinalIgnoreCase), result));
        } catch (Exception ex) {
            checks.Add(new("integrity", "SQLite 完整性", "error", ex.Message));
        }
        return new(DateTime.UtcNow.ToString("O"), checks, ReadRecentLogs());
    }

    private static async Task AddFileAsync(ZipArchive archive, string path, string entryName, CancellationToken token)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var output = entry.Open();
        await input.CopyToAsync(output, token);
    }

    private static async Task AddTextAsync(ZipArchive archive, string entryName, string text, CancellationToken token)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var output = entry.Open();
        await using var writer = new StreamWriter(output);
        await writer.WriteAsync(text.AsMemory(), token);
    }

    private static async Task ValidateDatabaseEntryAsync(ZipArchive archive, List<string> errors, CancellationToken token)
    {
        var entry = archive.GetEntry("database/LocalMediaManager.db");
        if (entry is null) return;
        string temp = Path.Combine(Path.GetTempPath(), $"lmm-validate-{Guid.NewGuid():N}.db");
        try {
            await using (var source = entry.Open())
            await using (var target = File.Create(temp))
                await source.CopyToAsync(target, token);
            await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await c.OpenAsync(token);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            string result = Convert.ToString(await cmd.ExecuteScalarAsync(token)) ?? "unknown";
            if (!result.Equals("ok", StringComparison.OrdinalIgnoreCase)) errors.Add($"数据库完整性检查失败：{result}");
        } finally {
            try { File.Delete(temp); } catch { }
        }
    }

    private static long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
    private async Task<int> ReadRetentionCountAsync(CancellationToken token)
    {
        if (!File.Exists(configDatabasePath)) return 10;
        try {
            await using var connection = new SqliteConnection($"Data Source={configDatabasePath}");
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key='dataBackup.retentionCount'";
            string? raw = Convert.ToString(await command.ExecuteScalarAsync(token));
            int count = JsonSerializer.Deserialize<int?>(raw ?? "10") ?? 10;
            return count is 5 or 10 or 20 ? count : 10;
        } catch {
            return 10;
        }
    }
    private Task PruneBackupsAsync(int retentionCount, CancellationToken token)
    {
        Directory.CreateDirectory(BackupRoot);
        foreach (FileInfo backup in new DirectoryInfo(BackupRoot)
            .EnumerateFiles("lmm-backup-*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(Math.Max(1, retentionCount))) {
            token.ThrowIfCancellationRequested();
            try { backup.Delete(); } catch { }
        }
        return Task.CompletedTask;
    }
    private static DiagnosticCheckDto Check(string key, string label, bool ok, string detail) => new(key, label, ok ? "success" : "error", detail);
    private static bool CanWriteDirectory(string path)
    {
        try {
            Directory.CreateDirectory(path);
            string test = Path.Combine(path, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(test, "ok");
            File.Delete(test);
            return true;
        } catch { return false; }
    }
    private List<string> ReadRecentLogs()
    {
        if (!Directory.Exists(LogRoot)) return [];
        return Directory.EnumerateFiles(LogRoot, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3)
            .SelectMany(path => File.ReadLines(path).TakeLast(20).Select(line => $"{Path.GetFileName(path)}: {Redact(line)}")).Take(60).ToList();
    }
    private static string Redact(string value) => value
        .Replace("token", "t***n", StringComparison.OrdinalIgnoreCase)
        .Replace("password", "p***d", StringComparison.OrdinalIgnoreCase)
        .Replace("cookie", "c***e", StringComparison.OrdinalIgnoreCase);
}
