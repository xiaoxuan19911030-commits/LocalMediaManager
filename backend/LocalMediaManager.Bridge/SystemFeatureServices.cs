using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record LogFileDto(string Name, string Path, long Bytes, string LastWriteTime, bool Active, bool Eligible, string Reason);
public sealed record LogCleanupPreviewDto(string LogDirectory, int RetentionDays, int FileCount, long TotalBytes, int DeletableCount,
    long DeletableBytes, string? OldestLogTime, IReadOnlyList<string> ActiveLogs, IReadOnlyList<LogFileDto> Files, string ConfirmationToken);
public sealed record LogCleanupCommand(int? RetentionDays = null, bool IncludeAllHistory = false, string? ConfirmationToken = null);
public sealed record LogCleanupResult(int DeletedFiles, long FreedBytes, int FailedFiles, IReadOnlyList<string> Failures, string Message);

public sealed record UpdateCheckResult(string CurrentVersion, string Status, string Message, string? LatestVersion,
    string? ReleaseUrl, string? ReleaseNotes, string CheckedAt);

public sealed class LogMaintenanceService(string? logRoot = null)
{
    private static readonly HashSet<string> ActiveNames = new(StringComparer.OrdinalIgnoreCase) { "bridge.log", "migration.log" };
    private string LogRoot => logRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalMediaManager", "logs");

    public LogCleanupPreviewDto Preview(int retentionDays, bool includeAllHistory = false)
    {
        int days = NormalizeRetentionDays(retentionDays);
        Directory.CreateDirectory(LogRoot);
        DateTime? cutoff = days == 0 ? null : DateTime.UtcNow.AddDays(-days);
        List<LogFileDto> files = EnumerateLogFiles(days, includeAllHistory, cutoff).ToList();
        List<LogFileDto> deletable = files.Where(file => file.Eligible).ToList();
        return new(LogRoot, days, files.Count, files.Sum(file => file.Bytes), deletable.Count, deletable.Sum(file => file.Bytes),
            files.Count == 0 ? null : files.Min(file => DateTime.Parse(file.LastWriteTime).ToUniversalTime()).ToString("O"),
            files.Where(file => file.Active).Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(),
            files, ConfirmationToken(days, includeAllHistory, deletable));
    }

    public LogCleanupResult Cleanup(LogCleanupCommand command)
    {
        int days = NormalizeRetentionDays(command.RetentionDays ?? 30);
        bool includeAll = command.IncludeAllHistory;
        LogCleanupPreviewDto preview = Preview(days, includeAll);
        if (string.IsNullOrWhiteSpace(command.ConfirmationToken) || command.ConfirmationToken != preview.ConfirmationToken)
            throw new UnauthorizedAccessException("日志清理预览已过期，请重新预览。");

        int deleted = 0;
        long freed = 0;
        var failures = new List<string>();
        foreach (LogFileDto file in preview.Files.Where(file => file.Eligible))
        {
            try
            {
                long bytes = File.Exists(file.Path) ? new FileInfo(file.Path).Length : 0;
                File.Delete(file.Path);
                deleted++;
                freed += bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{file.Name}: {error.Message}");
            }
        }

        return new(deleted, freed, failures.Count, failures, failures.Count == 0
            ? $"已删除 {deleted} 个历史日志文件。"
            : $"已删除 {deleted} 个历史日志文件，{failures.Count} 个文件删除失败。");
    }

    private IEnumerable<LogFileDto> EnumerateLogFiles(int retentionDays, bool includeAllHistory, DateTime? cutoff)
    {
        if (!Directory.Exists(LogRoot)) yield break;
        foreach (string path in Directory.EnumerateFiles(LogRoot, "*.log*", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch { continue; }
            bool active = ActiveNames.Contains(info.Name);
            bool oldEnough = cutoff.HasValue && info.LastWriteTimeUtc < cutoff.Value;
            bool eligible = !active && (includeAllHistory || oldEnough);
            string reason = active ? "当前活动日志，保留" : eligible ? "可安全清理" : retentionDays == 0 ? "永久保留" : $"保留 {retentionDays} 天内日志";
            yield return new(info.Name, info.FullName, info.Exists ? info.Length : 0, info.LastWriteTimeUtc.ToString("O"), active, eligible, reason);
        }
    }

    public static int NormalizeRetentionDays(int days) => days is 0 or 7 or 14 or 30 or 90 ? days : 30;

    private static string ConfirmationToken(int days, bool includeAll, IReadOnlyList<LogFileDto> files)
    {
        string state = JsonSerializer.Serialize(new
        {
            days,
            includeAll,
            files = files.Select(file => new { file.Path, file.Bytes, file.LastWriteTime })
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("lmm-log-cleanup|" + state))).ToLowerInvariant();
    }
}

public sealed class UpdateCheckService(HttpClient http, string databasePath)
{
    private const string CurrentVersion = "0.7.5";
    private const string LatestReleaseUrl = "https://api.github.com/repos/xiaoxuan19911030-commits/LocalMediaManager/releases/latest";
    private const string ReleasesPage = "https://github.com/xiaoxuan19911030-commits/LocalMediaManager/releases";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken token = default)
    {
        string checkedAt = DateTimeOffset.UtcNow.ToString("O");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("LocalMediaManager/0.7.5");
            using HttpResponseMessage response = await http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return await SaveResultAsync(new(CurrentVersion, "network-error", $"无法连接更新服务：HTTP {(int)response.StatusCode}", null, ReleasesPage, null, checkedAt), token);
            GitHubRelease? release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: token);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return await SaveResultAsync(new(CurrentVersion, "invalid-response", "更新信息无效。", null, ReleasesPage, null, checkedAt), token);
            string latest = release.TagName.Trim().TrimStart('v', 'V');
            int comparison = CompareVersions(CurrentVersion, latest);
            string status = comparison < 0 ? "update-available" : comparison > 0 ? "current-newer" : "up-to-date";
            string message = status switch
            {
                "update-available" => $"发现新版本 {release.TagName}。",
                "current-newer" => $"当前版本 {CurrentVersion} 高于发布版本 {release.TagName}。",
                _ => "已是最新版本。"
            };
            return await SaveResultAsync(new(CurrentVersion, status, message, latest, release.HtmlUrl ?? ReleasesPage, release.Body, checkedAt), token);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return await SaveResultAsync(new(CurrentVersion, "network-error", $"无法连接更新服务：{error.Message}", null, ReleasesPage, null, checkedAt), CancellationToken.None);
        }
    }

    private async Task<UpdateCheckResult> SaveResultAsync(UpdateCheckResult result, CancellationToken token)
    {
        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync(token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt)
            VALUES('updates.lastCheckedAt',$checked,'string',$at),
                  ('updates.lastResult',$result,'json',$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$checked", JsonSerializer.Serialize(result.CheckedAt));
        command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
        return result;
    }

    public static int CompareVersions(string current, string latest)
    {
        static int[] Parts(string value) => value.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out int number) ? number : 0).ToArray();
        int[] a = Parts(current);
        int[] b = Parts(latest);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int left = i < a.Length ? a[i] : 0;
            int right = i < b.Length ? b[i] : 0;
            int compare = left.CompareTo(right);
            if (compare != 0) return compare;
        }
        return 0;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("body")] string? Body);
}
