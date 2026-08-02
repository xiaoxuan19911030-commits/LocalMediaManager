using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed record GeneratedCoverSettingsDto(bool Enabled = true, bool CheckOnStartup = true, bool BackgroundGeneration = true, string Scope = "Recent");

public sealed class GeneratedCoverSettingsService(string databasePath)
{
    private const string Key = "movieWall.generatedCover.settings";
    public async Task<GeneratedCoverSettingsDto> ReadAsync(CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key=$key";
        command.Parameters.AddWithValue("$key", Key);
        string? value = (await command.ExecuteScalarAsync(token))?.ToString();
        try { return Normalize(string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<GeneratedCoverSettingsDto>(value)); }
        catch (JsonException) { return new(); }
    }
    public async Task<GeneratedCoverSettingsDto> SaveAsync(GeneratedCoverSettingsDto value, CancellationToken token = default)
    {
        GeneratedCoverSettingsDto clean = Normalize(value);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,'json',$at) ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,UpdatedAt=excluded.UpdatedAt";
        command.Parameters.AddWithValue("$key", Key); command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(clean)); command.Parameters.AddWithValue("$at", Now());
        await command.ExecuteNonQueryAsync(token); return clean;
    }
    private static GeneratedCoverSettingsDto Normalize(GeneratedCoverSettingsDto? value) => new(value?.Enabled ?? true, value?.CheckOnStartup ?? true, value?.BackgroundGeneration ?? true,
        string.Equals(value?.Scope, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "Recent");
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token) { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared }.ToString()); await c.OpenAsync(token); return c; }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
}

public sealed class GeneratedCoverTaskService(
    string databasePath, string imageRoot, CoverResolver covers, GeneratedCoverSettingsService settings,
    FfmpegLocator ffmpegLocator, TaskLogService logs, IFaceDetectionService faces) : BackgroundService
{
    private const string TaskType = "GeneratedCover";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            GeneratedCoverSettingsDto initial = await settings.ReadAsync(stoppingToken);
            if (initial.Enabled && initial.CheckOnStartup && initial.BackgroundGeneration) await EnqueueEligibleAsync(initial, stoppingToken);
            while (!stoppingToken.IsCancellationRequested) {
                GeneratedCoverSettingsDto current = await settings.ReadAsync(stoppingToken);
                if (!current.Enabled || !current.BackgroundGeneration) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                long? task = await ClaimAsync(stoppingToken);
                if (task is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                await RunAsync(task.Value, stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<int> EnqueueEligibleAsync(GeneratedCoverSettingsDto? configured = null, CancellationToken token = default)
    {
        GeneratedCoverSettingsDto value = configured ?? await settings.ReadAsync(token);
        if (!value.Enabled || !value.BackgroundGeneration) return 0;
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT m.Id FROM Movies m WHERE EXISTS(SELECT 1 FROM MediaFiles f WHERE f.MovieId=m.Id AND f.MediaType='Video' AND f.ExistsState<>'Missing') ORDER BY COALESCE(m.ImportedAt,m.CreatedAt) DESC,m.Id DESC LIMIT {(value.Scope == "All" ? 10000 : 100)}";
        var movies = new List<long>(); await using (SqliteDataReader reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) movies.Add(reader.GetInt64(0));
        int queued = 0;
        foreach (long movieId in movies) {
            token.ThrowIfCancellationRequested();
            if (await covers.HasFormalPosterAsync(movieId, token) || !await NeedsGenerationAsync(connection, movieId, token)) continue;
            long active = await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE TaskType=$type AND CurrentMovieId=$movie AND Status IN ('Pending','Preparing','Running')", token, ("$type", TaskType), ("$movie", movieId));
            if (active > 0) continue;
            string at = Now(); await ExecuteAsync(connection, "INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CurrentMovieId,CreatedAt,UpdatedAt) VALUES($type,'Pending','Pending',0,1,0,$payload,$movie,$at,$at)", token,
                ("$type", TaskType), ("$payload", JsonSerializer.Serialize(new { MovieId = movieId })), ("$movie", movieId), ("$at", at)); queued++;
        }
        return queued;
    }

    private async Task RunAsync(long taskId, CancellationToken token)
    {
        long movieId = await ScalarAsync(await OpenAsync(SqliteOpenMode.ReadOnly, token), "SELECT CurrentMovieId FROM Tasks WHERE Id=$id", token, ("$id", taskId));
        try {
            if (movieId <= 0 || await covers.HasFormalPosterAsync(movieId, token)) { await CompleteAsync(taskId, "已存在正式海报，跳过备用封面生成。", token); return; }
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
            string? video = await TextAsync(connection, "SELECT FilePath FROM MediaFiles WHERE MovieId=$movie AND MediaType='Video' AND ExistsState<>'Missing' ORDER BY IsPrimary DESC,Id LIMIT 1", token, ("$movie", movieId));
            if (string.IsNullOrWhiteSpace(video) || !File.Exists(video)) throw new FileNotFoundException("影片文件不存在，无法生成备用封面。", video);
            FfmpegLookupResult lookup = ffmpegLocator.Locate(); if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.Path)) throw new FileNotFoundException(lookup.Message);
            await MarkRunningAsync(taskId, token); await logs.WriteAsync(taskId, "Info", "开始生成影片墙备用封面：最多分析 9 张视频截图。", token);
            TimeSpan duration = await ProbeDurationAsync(lookup.Path, video, token) ?? TimeSpan.FromMinutes(1);
            string directory = Path.Combine(imageRoot, "Cache", "GeneratedCovers", movieId.ToString(CultureInfo.InvariantCulture)); Directory.CreateDirectory(directory);
            Candidate? best = null;
            for (int index = 1; index <= 9; index++) {
                double seconds = Math.Max(.1, duration.TotalSeconds * index / 10d);
                string candidate = Path.Combine(directory, $".candidate-{Guid.NewGuid():N}.jpg");
                try {
                    await CaptureAsync(lookup.Path, video, candidate, seconds, token);
                    ScreenshotQuality quality = ScreenshotQualityAnalyzer.Analyze(candidate);
                    if (quality.BlackRatio >= .85) { TryDelete(candidate); continue; }
                    FaceDetectionResult detected = await faces.DetectAsync(candidate, token);
                    double score = Score(quality, detected);
                    if (best is null || score > best.Score) { if (best is not null) TryDelete(best.Path); best = new(candidate, seconds, score); }
                    else TryDelete(candidate);
                } catch { TryDelete(candidate); }
            }
            if (best is null) throw new InvalidOperationException("9 张候选截图均为黑屏、损坏或不可用。");
            string final = Path.Combine(directory, $"cover-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg"); File.Move(best.Path, final);
            FileInfo source = new(video); await SaveAsync(movieId, source, final, best.Seconds, best.Score, token);
            await CompleteAsync(taskId, $"备用封面已生成，评分 {best.Score:0.0}，取样位置 {best.Seconds:0.0} 秒。", token);
        } catch (Exception error) when (error is not OperationCanceledException) { await FailAsync(taskId, error.Message, token); }
    }

    internal static double Score(ScreenshotQuality quality, FaceDetectionResult faces)
    {
        double brightness = Math.Max(0, 1 - Math.Abs(quality.Brightness - .5) / .5) * 25;
        double sharpness = Math.Clamp(quality.Sharpness / .10, 0, 1) * 30;
        FaceRectangle? face = faces.LargestFace;
        double faceScore = face is null ? 0 : Math.Clamp(face.Area / .18, 0, 1) * 25 + Math.Clamp(face.Confidence, 0, 1) * 12 + Math.Min(faces.Faces.Count - 1, 3) * 2;
        return brightness + sharpness + faceScore;
    }

    private async Task<bool> NeedsGenerationAsync(SqliteConnection connection, long movieId, CancellationToken token)
    {
        string? video = await TextAsync(connection, "SELECT FilePath FROM MediaFiles WHERE MovieId=$movie AND MediaType='Video' AND ExistsState<>'Missing' ORDER BY IsPrimary DESC,Id LIMIT 1", token, ("$movie", movieId));
        if (string.IsNullOrWhiteSpace(video) || !File.Exists(video)) return false;
        FileInfo file = new(video); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT SourceVideoPath,SourceVideoSize,SourceVideoModifiedTime,GeneratedCoverPath FROM GeneratedCoverInfo WHERE MovieId=$movie"; command.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) return true;
        return !string.Equals(Path.GetFullPath(video), Path.GetFullPath(reader.GetString(0)), StringComparison.OrdinalIgnoreCase) || file.Length != reader.GetInt64(1) || file.LastWriteTimeUtc.Ticks.ToString() != reader.GetString(2) || !File.Exists(reader.GetString(3));
    }
    private async Task SaveAsync(long movieId, FileInfo video, string cover, double seconds, double score, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token); string at = Now();
        await ExecuteAsync(connection, "INSERT INTO GeneratedCoverInfo(MovieId,SourceVideoPath,SourceVideoSize,SourceVideoModifiedTime,GeneratedCoverPath,FramePosition,Score,CreatedAt) VALUES($movie,$video,$size,$modified,$cover,$position,$score,$at) ON CONFLICT(MovieId) DO UPDATE SET SourceVideoPath=excluded.SourceVideoPath,SourceVideoSize=excluded.SourceVideoSize,SourceVideoModifiedTime=excluded.SourceVideoModifiedTime,GeneratedCoverPath=excluded.GeneratedCoverPath,FramePosition=excluded.FramePosition,Score=excluded.Score,CreatedAt=excluded.CreatedAt", token,
            ("$movie", movieId), ("$video", video.FullName), ("$size", video.Length), ("$modified", video.LastWriteTimeUtc.Ticks.ToString()), ("$cover", cover), ("$position", seconds), ("$score", score), ("$at", at));
    }
    private async Task<long?> ClaimAsync(CancellationToken token) { await using SqliteConnection c = await OpenAsync(SqliteOpenMode.ReadWrite, token); long id = await ScalarAsync(c, "SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType=$type AND Status='Pending'", token, ("$type", TaskType)); if (id == 0) return null; await ExecuteAsync(c, "UPDATE Tasks SET Status='Preparing',Stage='Preparing',UpdatedAt=$at WHERE Id=$id AND Status='Pending'", token, ("$id", id), ("$at", Now())); return id; }
    private async Task MarkRunningAsync(long id, CancellationToken token) { await using SqliteConnection c = await OpenAsync(SqliteOpenMode.ReadWrite, token); await ExecuteAsync(c, "UPDATE Tasks SET Status='Running',Stage='Running',Progress=20,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id", token, ("$id", id), ("$at", Now())); }
    private async Task CompleteAsync(long id, string summary, CancellationToken token) { await using SqliteConnection c = await OpenAsync(SqliteOpenMode.ReadWrite, token); await ExecuteAsync(c, "UPDATE Tasks SET Status='Completed',Stage='Completed',Progress=100,CompletedItems=1,ResultSummary=$summary,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id", token, ("$id", id), ("$summary", summary), ("$at", Now())); await logs.WriteAsync(id, "Info", summary, token); }
    private async Task FailAsync(long id, string error, CancellationToken token) { await using SqliteConnection c = await OpenAsync(SqliteOpenMode.ReadWrite, token); await ExecuteAsync(c, "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id", token, ("$id", id), ("$error", error), ("$at", Now())); await logs.WriteAsync(id, "Warning", $"备用封面生成失败：{error}", token); }
    private static async Task CaptureAsync(string ffmpeg, string video, string path, double seconds, CancellationToken token) { using Process p = new() { StartInfo = new ProcessStartInfo { FileName = ffmpeg, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } }; foreach (string arg in new[] { "-y", "-threads", "1", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", video, "-frames:v", "1", "-q:v", "2", path }) p.StartInfo.ArgumentList.Add(arg); p.Start(); await p.WaitForExitAsync(token); if (p.ExitCode != 0 || !File.Exists(path)) throw new InvalidOperationException("FFmpeg 未生成有效截图。"); }
    private static async Task<TimeSpan?> ProbeDurationAsync(string ffmpeg, string video, CancellationToken token) { string probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe"); if (!File.Exists(probe)) return null; using Process p = new() { StartInfo = new ProcessStartInfo { FileName = probe, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } }; foreach (string arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", video }) p.StartInfo.ArgumentList.Add(arg); p.Start(); string value = await p.StandardOutput.ReadToEndAsync(token); await p.WaitForExitAsync(token); return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null; }
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token) { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared }.ToString()); await c.OpenAsync(token); return c; }
    private static async Task ExecuteAsync(SqliteConnection c, string sql, CancellationToken token, params (string Name, object? Value)[] values) { await using SqliteCommand command = c.CreateCommand(); command.CommandText = sql; foreach ((string n, object? v) in values) command.Parameters.AddWithValue(n, v ?? DBNull.Value); await command.ExecuteNonQueryAsync(token); }
    private static async Task<long> ScalarAsync(SqliteConnection c, string sql, CancellationToken token, params (string Name, object? Value)[] values) { await using SqliteCommand command = c.CreateCommand(); command.CommandText = sql; foreach ((string n, object? v) in values) command.Parameters.AddWithValue(n, v ?? DBNull.Value); return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L); }
    private static async Task<string?> TextAsync(SqliteConnection c, string sql, CancellationToken token, params (string Name, object? Value)[] values) { await using SqliteCommand command = c.CreateCommand(); command.CommandText = sql; foreach ((string n, object? v) in values) command.Parameters.AddWithValue(n, v ?? DBNull.Value); return (await command.ExecuteScalarAsync(token))?.ToString(); }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private sealed record Candidate(string Path, double Seconds, double Score);
}
