using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace LocalMediaManager.Bridge;

public sealed record MetadataHealthField(string Key, long Missing, bool Required);
public sealed record MetadataHealthSummary(long TotalMovies, long CompleteMovies, long IncompleteMovies, double CompleteRate, IReadOnlyList<MetadataHealthField> Fields, long AnalysisDurationMs, string AnalyzedAt);

public static class MetadataHealthReader
{
    private const string Active = "EXISTS(SELECT 1 FROM MediaFiles f WHERE f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' AND COALESCE(f.ExistsState,'')<>'Missing')";
    private const string OfficialTags = "EXISTS(SELECT 1 FROM MovieTags x JOIN Tags t ON t.Id=x.TagId WHERE x.MovieId=m.Id AND COALESCE(t.Source,'User')<>'User')";
    private const string Poster = "EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Poster','GeneratedCard','Thumbnail') AND trim(COALESCE(i.FilePath,''))<>'')";
    private const string Fanart = "EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Fanart','BigPic') AND trim(COALESCE(i.FilePath,''))<>'')";

    public static async Task<MetadataHealthSummary> ReadAsync(string path, IProgress<(string Stage, int Completed, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        Stopwatch timer = Stopwatch.StartNew();
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await db.OpenAsync(cancellationToken);
        const int queryCount = 17;
        int completedQueries = 0;
        async Task<long> Count(string stage, string sql) {
            cancellationToken.ThrowIfCancellationRequested();
            long value = await Scalar(db, sql, cancellationToken);
            completedQueries++;
            progress?.Report((stage, completedQueries, queryCount));
            return value;
        }
        long total = await Count("读取影片", $"SELECT COUNT(*) FROM Movies m WHERE {Active}");
        var fields = new List<MetadataHealthField>();
        async Task Add(string key, string predicate, bool required) => fields.Add(new(key, await Count($"分析 {key}", $"SELECT COUNT(*) FROM Movies m WHERE {Active} AND NOT({predicate})"), required));
        await Add("title", "trim(COALESCE(m.Title,''))<>''", true); await Add("code", "trim(COALESCE(m.Code,''))<>''", true);
        await Add("releaseDate", "trim(COALESCE(m.ReleaseDate,''))<>''", true); await Add("description", "trim(COALESCE(m.Description,''))<>''", true);
        await Add("actors", "EXISTS(SELECT 1 FROM MovieActors x WHERE x.MovieId=m.Id)", true); await Add("officialTags", OfficialTags, true);
        await Add("poster", Poster, true); await Add("fanart", Fanart, true); await Add("nfo", "trim(COALESCE(m.NfoPath,''))<>''", true);
        await Add("duration", "COALESCE(m.DurationSeconds,0)>0", false); await Add("director", "EXISTS(SELECT 1 FROM MovieDirectors x WHERE x.MovieId=m.Id)", false);
        await Add("studio", "EXISTS(SELECT 1 FROM MovieStudios x WHERE x.MovieId=m.Id)", false); await Add("series", "EXISTS(SELECT 1 FROM MovieSeries x WHERE x.MovieId=m.Id)", false);
        await Add("preview", "EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Preview','ExtraPic') AND trim(COALESCE(i.FilePath,''))<>'')", false);
        await Add("screenshot", "EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType='Screenshot' AND trim(COALESCE(i.FilePath,''))<>'')", false);
        string missingRequired = $"trim(COALESCE(m.Title,''))='' OR trim(COALESCE(m.Code,''))='' OR trim(COALESCE(m.ReleaseDate,''))='' OR trim(COALESCE(m.Description,''))='' OR NOT EXISTS(SELECT 1 FROM MovieActors x WHERE x.MovieId=m.Id) OR NOT({OfficialTags}) OR NOT({Poster}) OR NOT({Fanart}) OR trim(COALESCE(m.NfoPath,''))=''";
        long incomplete = await Count("汇总", $"SELECT COUNT(*) FROM Movies m WHERE {Active} AND ({missingRequired})");
        long complete = Math.Max(0, total - incomplete);
        timer.Stop();
        return new(total, complete, incomplete, total == 0 ? 100 : complete * 100d / total, fields, timer.ElapsedMilliseconds, DateTimeOffset.UtcNow.ToString("O"));
    }

    private static async Task<long> Scalar(SqliteConnection db, string sql, CancellationToken cancellationToken) { await using var command = db.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L); }
}

public sealed record MetadataHealthAnalysisState(
    bool Running, bool Invalidated, string Stage, int CompletedSteps, int TotalSteps,
    double Percent, long ElapsedMilliseconds, MetadataHealthSummary? Result, string? Error);

public sealed class MetadataHealthAnalysisService(string databasePath)
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private MetadataHealthSummary? result;
    private bool invalidated = true;
    private string stage = "尚未分析";
    private int completedSteps;
    private int totalSteps = 17;
    private Stopwatch? timer;
    private string? error;
    private long analyzedStorageStamp;

    public async Task<MetadataHealthSummary> GetAsync(CancellationToken token = default)
    {
        MetadataHealthSummary? current;
        lock (gate) current = result;
        if (current is not null) return current;
        current = await MetadataHealthReader.ReadAsync(databasePath, cancellationToken: token);
        lock (gate) { result = current; invalidated = false; analyzedStorageStamp = StorageStamp(); stage = "已完成"; completedSteps = totalSteps; }
        return current;
    }

    public MetadataHealthAnalysisState Start()
    {
        CancellationTokenSource source;
        lock (gate) {
            if (cancellation is not null) return StateLocked();
            cancellation = source = new CancellationTokenSource();
            timer = Stopwatch.StartNew(); error = null; stage = "准备"; completedSteps = 0; totalSteps = 17;
        }
        _ = Task.Run(() => RunAsync(source));
        return GetState();
    }

    public MetadataHealthAnalysisState Cancel()
    {
        lock (gate) cancellation?.Cancel();
        return GetState();
    }

    public void Invalidate()
    {
        lock (gate) invalidated = true;
    }

    public MetadataHealthAnalysisState GetState() { lock (gate) return StateLocked(); }

    private async Task RunAsync(CancellationTokenSource source)
    {
        try {
            var progress = new Progress<(string Stage, int Completed, int Total)>(value => {
                lock (gate) { stage = value.Stage; completedSteps = value.Completed; totalSteps = value.Total; }
            });
            MetadataHealthSummary next = await MetadataHealthReader.ReadAsync(databasePath, progress, source.Token);
            lock (gate) { result = next; invalidated = false; analyzedStorageStamp = StorageStamp(); stage = "已完成"; completedSteps = totalSteps; }
        } catch (OperationCanceledException) {
            lock (gate) stage = "已取消，保留上次结果";
        } catch (Exception exception) {
            lock (gate) { stage = "分析失败"; error = exception.Message; }
        } finally {
            lock (gate) { timer?.Stop(); cancellation?.Dispose(); cancellation = null; }
        }
    }

    private MetadataHealthAnalysisState StateLocked()
    {
        if (result is not null && StorageStamp() > analyzedStorageStamp) invalidated = true;
        bool running = cancellation is not null;
        long elapsed = timer?.ElapsedMilliseconds ?? result?.AnalysisDurationMs ?? 0;
        return new(running, invalidated, stage, completedSteps, totalSteps,
            totalSteps == 0 ? 0 : completedSteps * 100d / totalSteps, elapsed, result, error);
    }

    private long StorageStamp() => new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
        .Where(File.Exists).Select(File.GetLastWriteTimeUtc).Select(value => value.Ticks).DefaultIfEmpty(0).Max();
}
