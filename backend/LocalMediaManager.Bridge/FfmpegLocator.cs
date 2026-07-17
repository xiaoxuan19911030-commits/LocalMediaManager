using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record FfmpegLookupResult(bool Found, string? Path, string Source, string Message);

public sealed class FfmpegLocator(string databasePath, string appRoot)
{
    private static readonly string LegacyFallback = @"D:\JvedioNext\data\Administrator\tools\ffmpeg\ffmpeg.exe";

    public FfmpegLookupResult Locate()
    {
        foreach ((string source, string? candidate) in Candidates()) {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return new(true, candidate, source, $"FFmpeg 已找到：{source}");
        }
        return new(false, null, "Missing", "FFmpeg 不存在。请在设置中配置 FFmpeg 路径，或将 ffmpeg.exe 加入系统 PATH。");
    }

    private IEnumerable<(string Source, string? Path)> Candidates()
    {
        yield return ("Bundled", Path.Combine(appRoot, "tools", "ffmpeg", "ffmpeg.exe"));
        yield return ("Settings", ReadConfiguredPath());
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            if (!string.IsNullOrWhiteSpace(folder))
                yield return ("PATH", Path.Combine(folder.Trim(), "ffmpeg.exe"));
        }
        yield return ("LegacyFallback", LegacyFallback);
    }

    private string? ReadConfiguredPath()
    {
        string? env = Environment.GetEnvironmentVariable("LMM_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        try {
            if (!File.Exists(databasePath)) return null;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key IN ('ffmpeg.path','metadata.ffmpeg.path') ORDER BY Key LIMIT 1";
            string? raw = command.ExecuteScalar()?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try { return JsonSerializer.Deserialize<string>(raw) ?? raw; }
            catch (JsonException) { return raw; }
        }
        catch {
            return null;
        }
    }
}
