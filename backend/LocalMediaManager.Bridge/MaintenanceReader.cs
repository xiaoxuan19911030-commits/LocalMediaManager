using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace LocalMediaManager.Bridge;

public sealed record MaintenanceStatsDto(long TotalMovies, long HealthyMovies, long ProblemMovies, long DuplicateMovies,
    long MissingImages, long MissingNfo, long MissingMetadata, long OrphanFiles, long EmptyDirectories, long CacheProblems);
public sealed record MaintenanceIssueDto(string Category, string Severity, string Title, string Detail, long? MovieId, string? Path);
public sealed record MaintenancePathDto(string Kind, string Path, string Reason);
public sealed record MaintenanceReportDto(MaintenanceStatsDto Stats, IReadOnlyList<MaintenanceIssueDto> Issues,
    IReadOnlyList<MaintenancePathDto> OrphanFiles, IReadOnlyList<MaintenancePathDto> Directories, DuplicateResultsDto Duplicates,
    int Limit, int Offset);

public static class MaintenanceReader
{
    private static readonly string[] VideoExtensions = [".mp4",".mkv",".avi",".wmv",".mov",".ts",".m2ts",".flv",".webm",".vob",".mpg",".mpeg"];
    private static readonly string[] SidecarExtensions = [".jpg",".jpeg",".png",".webp",".gif",".nfo",".srt",".ass",".ssa",".vtt"];
    private const int FileSystemScanBudgetMs = 3500;
    private static string ActiveImageSql(string alias) => $"COALESCE({alias}.SourceProvider,'')<>'LegacyFile' AND NOT (COALESCE({alias}.FilePath,'') LIKE '%JVDIO%' OR COALESCE({alias}.FilePath,'') LIKE '%Jvedio%' OR COALESCE({alias}.FilePath,'') LIKE '%BigPic%' OR COALESCE({alias}.FilePath,'') LIKE '%SmallPic%' OR COALESCE({alias}.FilePath,'') LIKE '%ExtraPic%')";

    public static async Task<MaintenanceReportDto> ReadAsync(string databasePath, string imageRoot, string bridgeUrl, int limit, int offset)
    {
        await using var connection = await OpenAsync(databasePath);
        var issues = new List<MaintenanceIssueDto>();
        var fileSystemBudget = Stopwatch.StartNew();
        long total = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies");
        await AddMovieIssuesAsync(connection, issues);
        var orphans = await ReadOrphanFilesAsync(connection, imageRoot, limit, offset, fileSystemBudget);
        var directories = await ReadDirectoryIssuesAsync(connection, imageRoot, limit, offset, fileSystemBudget);
        long cacheProblems = await CountInvalidCacheAsync(connection, fileSystemBudget);
        foreach (var cache in await ReadInvalidCacheAsync(connection, limit, fileSystemBudget))
            issues.Add(new("图片缓存失效", "warning", "图片缓存失效", "缓存记录对应的文件不存在或超出缓存目录。", null, cache));
        DuplicateResultsDto duplicates = await ProductReader.ReadDuplicateResultsAsync(databasePath, "all", 100);
        long duplicateMovies = duplicates.Groups.SelectMany(group => group.Items.Select(item => item.MovieId)).Distinct().LongCount();
        long missingImages = await ScalarAsync(connection, $"SELECT COUNT(*) FROM Movies m WHERE NOT EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND {ActiveImageSql("i")})");
        long missingNfo = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE trim(COALESCE(NfoPath,''))=''");
        long missingMetadata = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE COALESCE(IsScraped,0)=0 OR trim(COALESCE(Description,''))=''");
        long problemMovies = issues.Where(item => item.MovieId.HasValue).Select(item => item.MovieId!.Value).Distinct().LongCount();
        var stats = new MaintenanceStatsDto(total, Math.Max(0, total - problemMovies), problemMovies, duplicateMovies,
            missingImages, missingNfo, missingMetadata, orphans.Count, directories.Count, cacheProblems);
        return new(stats, issues.Skip(offset).Take(limit).ToList(), orphans, directories, duplicates, limit, offset);
    }

    private static async Task AddMovieIssuesAsync(SqliteConnection connection, List<MaintenanceIssueDto> issues)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.Id,COALESCE(m.Code,''),COALESCE(m.Title,''),COALESCE(f.FilePath,''),COALESCE(f.ExistsState,''),
                   COALESCE(m.NfoPath,''),COALESCE(m.IsScraped,0),COALESCE(m.Description,''),
                   EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND {ActiveImageSql("i")} AND i.ImageType IN ('Poster','GeneratedCard','Thumbnail')),
                   EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND {ActiveImageSql("i")} AND i.ImageType IN ('Fanart','BigPic')),
                   EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND {ActiveImageSql("i")} AND i.ImageType IN ('Preview','ExtraPic','Screenshot')),
                   EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id),
                   EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id)
              FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
             ORDER BY m.Id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long id = reader.GetInt64(0);
            string name = reader.GetString(1); if (string.IsNullOrWhiteSpace(name)) name = reader.GetString(2);
            string path = reader.GetString(3);
            if (path.Length == 0) issues.Add(new("数据库记录不存在", "error", "缺少主视频文件记录", name, id, null));
            else if (reader.GetString(4) == "Missing") issues.Add(new("视频文件不存在", "error", "视频文件不存在", name, id, path));
            if (reader.GetString(5).Length == 0) issues.Add(new("NFO 缺失", "warning", "NFO 缺失", name, id, path));
            if (reader.GetInt64(6) == 0 || reader.GetString(7).Trim().Length == 0) issues.Add(new("Metadata 状态异常", "warning", "Metadata 状态异常", name, id, path));
            if (reader.GetInt64(8) == 0) issues.Add(new("封面缺失", "warning", "封面缺失", name, id, path));
            if (reader.GetInt64(9) == 0) issues.Add(new("背景图缺失", "warning", "背景图缺失", name, id, path));
            if (reader.GetInt64(10) == 0) issues.Add(new("预览图缺失", "warning", "预览图缺失", name, id, path));
            if (reader.GetInt64(11) == 0) issues.Add(new("演员缺失", "warning", "演员缺失", name, id, path));
            if (reader.GetInt64(12) == 0) issues.Add(new("标签缺失", "warning", "标签缺失", name, id, path));
        }
        if (await TableExistsAsync(connection, "Actors"))
            foreach (var issue in await ActorAvatarIssuesAsync(connection)) issues.Add(issue);
    }

    private static async Task<IReadOnlyList<MaintenanceIssueDto>> ActorAvatarIssuesAsync(SqliteConnection connection)
    {
        var result = new List<MaintenanceIssueDto>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT a.Id,a.Name FROM Actors a WHERE NOT EXISTS(SELECT 1 FROM Images i WHERE i.ActorId=a.Id AND i.ImageType='ActorAvatar' AND {ActiveImageSql("i")}) LIMIT 200";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new("演员头像缺失", "info", "演员头像缺失", reader.GetString(1), null, null));
        return result;
    }

    private static async Task<IReadOnlyList<MaintenancePathDto>> ReadOrphanFilesAsync(SqliteConnection connection, string imageRoot, int limit, int offset, Stopwatch budget)
    {
        var knownCodes = await ReadSetAsync(connection, "SELECT lower(Code) FROM Movies WHERE trim(COALESCE(Code,''))<>''");
        var knownPaths = await ReadSetAsync(connection, "SELECT lower(FilePath) FROM MediaFiles");
        var result = new List<MaintenancePathDto>();
        foreach (string root in CandidateRoots(connection, imageRoot).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!Directory.Exists(root)) continue;
            foreach (string file in EnumerateFilesBounded(root, budget, maxDirectories: 400, maxFiles: 5000)) {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!SidecarExtensions.Contains(ext) || knownPaths.Contains(file.ToLowerInvariant())) continue;
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!knownCodes.Any(code => name.Contains(code))) result.Add(new("孤立文件", file, "没有匹配的影片番号或媒体记录"));
            }
        }
        return result.Skip(offset).Take(limit).ToList();
    }

    private static async Task<IReadOnlyList<MaintenancePathDto>> ReadDirectoryIssuesAsync(SqliteConnection connection, string imageRoot, int limit, int offset, Stopwatch budget)
    {
        var result = new List<MaintenancePathDto>();
        foreach (string root in CandidateRoots(connection, imageRoot).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!Directory.Exists(root)) continue;
            foreach (string directory in EnumerateDirectoriesBounded(root, budget, maxDirectories: 3000)) {
                string[] files;
                try { files = Directory.GetFiles(directory); } catch { continue; }
                if (files.Length == 0) result.Add(new("空目录", directory, "空目录"));
                else if (!files.Any(file => VideoExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))) {
                    bool image = files.Any(file => new[] { ".jpg",".jpeg",".png",".webp" }.Contains(Path.GetExtension(file).ToLowerInvariant()));
                    bool nfo = files.Any(file => Path.GetExtension(file).Equals(".nfo", StringComparison.OrdinalIgnoreCase));
                    bool sub = files.Any(file => new[] { ".srt",".ass",".ssa",".vtt" }.Contains(Path.GetExtension(file).ToLowerInvariant()));
                    result.Add(new("无视频目录", directory, image ? "只有图片或附属文件，没有视频" : nfo ? "只有 NFO，没有视频" : sub ? "只有字幕，没有视频" : "没有视频"));
                }
            }
        }
        return result.Skip(offset).Take(limit).ToList();
    }

    private static IEnumerable<string> EnumerateFilesBounded(string root, Stopwatch budget, int maxDirectories, int maxFiles)
    {
        int fileCount = 0;
        foreach (string directory in EnumerateDirectoriesBounded(root, budget, maxDirectories)) {
            if (budget.ElapsedMilliseconds > FileSystemScanBudgetMs || fileCount >= maxFiles) yield break;
            string[] files;
            try { files = Directory.GetFiles(directory); } catch { continue; }
            foreach (string file in files) {
                if (budget.ElapsedMilliseconds > FileSystemScanBudgetMs || fileCount++ >= maxFiles) yield break;
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesBounded(string root, Stopwatch budget, int maxDirectories)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        int visited = 0;
        while (pending.Count > 0 && visited < maxDirectories && budget.ElapsedMilliseconds <= FileSystemScanBudgetMs) {
            string directory = pending.Dequeue();
            visited++;
            yield return directory;
            string[] children;
            try { children = Directory.GetDirectories(directory); } catch { continue; }
            foreach (string child in children) {
                if (visited + pending.Count >= maxDirectories || budget.ElapsedMilliseconds > FileSystemScanBudgetMs) break;
                pending.Enqueue(child);
            }
        }
    }

    private static IEnumerable<string> CandidateRoots(SqliteConnection connection, string imageRoot)
    {
        if (!string.IsNullOrWhiteSpace(imageRoot)) yield return imageRoot;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FolderPath FROM LibraryFolders WHERE IsEnabled=1 LIMIT 16";
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return reader.GetString(0);
    }

    private static async Task<long> CountInvalidCacheAsync(SqliteConnection connection, Stopwatch budget) => (await ReadInvalidCacheAsync(connection, 200, budget)).Count;
    private static async Task<IReadOnlyList<string>> ReadInvalidCacheAsync(SqliteConnection connection, int limit, Stopwatch budget)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CachePath FROM ImageCacheEntries LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            if (budget.ElapsedMilliseconds > FileSystemScanBudgetMs) break;
            string path = reader.GetString(0);
            if (!File.Exists(path)) result.Add(path);
        }
        return result;
    }

    private static async Task<HashSet<string>> ReadSetAsync(SqliteConnection connection, string sql)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L) > 0;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
