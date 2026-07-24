using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetadataHealthField(string Key, long Missing, bool Required);
public sealed record MetadataHealthScope(long AllMovies, long StandardMovies, long LocalMovies, long UnassignedMovies);
public sealed record MetadataResourceCoverage(long DatabaseMovies, long PhysicalMovies, long MissingFileRecords, long UnregisteredFiles);
public sealed record MetadataNfoCoverage(long DatabaseMovies, long ValidPathMovies, long PhysicalMovies, long MissingFileRecords, long UnregisteredFiles);
public sealed record MetadataHealthCoverage(
    long StandardNumberMovies,
    long TitleMovies,
    long ReleaseDateMovies,
    long StudioMovies,
    long ActorMovies,
    long ProviderTagMovies,
    long UserTagMovies,
    MetadataResourceCoverage Poster,
    MetadataResourceCoverage Fanart,
    MetadataResourceCoverage Preview,
    MetadataResourceCoverage Screenshot,
    MetadataNfoCoverage Nfo,
    long MissingCoreImageMovies,
    long InvalidResourceRecords,
    long UnregisteredResources,
    bool ResourceInventoryComplete);
public sealed record MetadataHealthSummary(
    long TotalMovies,
    long CompleteMovies,
    long IncompleteMovies,
    double CompleteRate,
    IReadOnlyList<MetadataHealthField> Fields,
    long AnalysisDurationMs,
    string AnalyzedAt,
    MetadataHealthScope Scope,
    MetadataHealthCoverage Coverage,
    IReadOnlyList<string> CompleteRule);

public static class MetadataHealthDefinition
{
    public static readonly IReadOnlyList<string> CompleteRule = [
        "标准化番号", "标题", "发行日期", "片商", "演员", "Provider 标签", "Poster 实体", "Fanart 实体", "NFO 实体",
    ];

    public static string ActiveMovie(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MediaFiles hf WHERE hf.MovieId={movieAlias}.Id AND hf.IsPrimary=1 AND hf.MediaType='Video' AND COALESCE(hf.ExistsState,'')<>'Missing')";

    public static string StandardMovie(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MediaFiles hf JOIN Libraries hl ON hl.Id=hf.LibraryId AND hl.IsEnabled=1 AND hl.LibraryType='Standard' WHERE hf.MovieId={movieAlias}.Id AND hf.IsPrimary=1 AND hf.MediaType='Video' AND COALESCE(hf.ExistsState,'')<>'Missing')";

    public static string LocalMovie(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MediaFiles hf JOIN Libraries hl ON hl.Id=hf.LibraryId AND hl.IsEnabled=1 AND hl.LibraryType='Local' WHERE hf.MovieId={movieAlias}.Id AND hf.IsPrimary=1 AND hf.MediaType='Video' AND COALESCE(hf.ExistsState,'')<>'Missing')";

    public static string UnassignedMovie(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MediaFiles hf WHERE hf.MovieId={movieAlias}.Id AND hf.LibraryId IS NULL AND hf.IsPrimary=1 AND hf.MediaType='Video' AND COALESCE(hf.ExistsState,'')<>'Missing')";

    public static string Number(string movieAlias) =>
        $"trim(COALESCE({movieAlias}.Code,''))<>'' AND trim(COALESCE({movieAlias}.Code,''))=upper(trim(COALESCE({movieAlias}.Code,'')))";

    public static string Title(string movieAlias) => $"trim(COALESCE({movieAlias}.Title,''))<>''";
    public static string ReleaseDate(string movieAlias) => $"trim(COALESCE({movieAlias}.ReleaseDate,''))<>''";
    public static string Description(string movieAlias) => $"trim(COALESCE({movieAlias}.Description,''))<>''";
    public static string Duration(string movieAlias) => $"COALESCE({movieAlias}.DurationSeconds,0)>0";
    public static string NfoDatabase(string movieAlias, bool hasNfoDocuments = false) => NfoPredicate(
        movieAlias, hasNfoDocuments, path => $"trim(COALESCE({path},''))<>''");

    public static string NfoValidPath(string movieAlias, bool hasNfoDocuments = false) => NfoPredicate(
        movieAlias, hasNfoDocuments, path => $"lmm_nfo_path_valid({path})=1");

    public static string NfoPhysical(string movieAlias, bool hasNfoDocuments = false) => NfoPredicate(
        movieAlias, hasNfoDocuments, path => $"lmm_nfo_exists({path})=1");

    public static string Actors(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MovieActors hma JOIN Actors ha ON ha.Id=hma.ActorId AND trim(COALESCE(ha.Name,''))<>'' WHERE hma.MovieId={movieAlias}.Id)";

    public static string Studios(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MovieStudios hms JOIN Studios hs ON hs.Id=hms.StudioId AND trim(COALESCE(hs.Name,''))<>'' WHERE hms.MovieId={movieAlias}.Id)";

    public static string ProviderTags(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MovieGenres hmg JOIN Genres hg ON hg.Id=hmg.GenreId AND trim(COALESCE(hg.Name,''))<>'' WHERE hmg.MovieId={movieAlias}.Id)";

    public static string UserTags(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MovieTags hmt JOIN Tags ht ON ht.Id=hmt.TagId AND trim(COALESCE(ht.Name,''))<>'' WHERE hmt.MovieId={movieAlias}.Id)";

    public static string Directors(string movieAlias, bool hasDirectors) => hasDirectors
        ? $"EXISTS(SELECT 1 FROM MovieDirectors hmd JOIN Directors hd ON hd.Id=hmd.DirectorId AND trim(COALESCE(hd.Name,''))<>'' WHERE hmd.MovieId={movieAlias}.Id)"
        : "0=1";

    public static string Series(string movieAlias) =>
        $"EXISTS(SELECT 1 FROM MovieSeries hmsr JOIN Series hsr ON hsr.Id=hmsr.SeriesId AND trim(COALESCE(hsr.Name,''))<>'' WHERE hmsr.MovieId={movieAlias}.Id)";

    public static string ImageDatabase(string movieAlias, string type) =>
        $"EXISTS(SELECT 1 FROM Images hi WHERE hi.MovieId={movieAlias}.Id AND {ImageTypePredicate("hi", type)})";

    public static string ImagePhysical(string movieAlias, string type) =>
        $"EXISTS(SELECT 1 FROM Images hi WHERE hi.MovieId={movieAlias}.Id AND {ImageTypePredicate("hi", type)} AND lmm_file_exists(hi.FilePath)=1)";

    public static string Complete(string movieAlias, bool hasNfoDocuments = false) => string.Join(" AND ", [
        $"({Number(movieAlias)})", $"({Title(movieAlias)})", $"({ReleaseDate(movieAlias)})", $"({Studios(movieAlias)})",
        $"({Actors(movieAlias)})", $"({ProviderTags(movieAlias)})", $"({ImagePhysical(movieAlias, "Poster")})",
        $"({ImagePhysical(movieAlias, "Fanart")})", $"({NfoPhysical(movieAlias, hasNfoDocuments)})",
    ]);

    public static void RegisterFileFunctions(SqliteConnection connection)
    {
        connection.CreateFunction("lmm_file_exists", (string? path) => FileExists(path) ? 1L : 0L);
        connection.CreateFunction("lmm_nfo_path_valid", (string? path) => ValidNfoPath(path) ? 1L : 0L);
        connection.CreateFunction("lmm_nfo_exists", (string? path) => ValidNfoPath(path) && FileExists(path) ? 1L : 0L);
    }

    public static bool FileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return File.Exists(Path.GetFullPath(path)); }
        catch (Exception) { return false; }
    }

    public static bool ValidNfoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".nfo", StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    private static string ImageTypePredicate(string alias, string type) => type switch
    {
        "Fanart" => $"{alias}.ImageType IN ('Fanart','BigPic')",
        "Preview" => $"{alias}.ImageType IN ('Preview','ExtraPic')",
        "Screenshot" => $"{alias}.ImageType='Screenshot'",
        _ => $"{alias}.ImageType='Poster'",
    };

    private static string NfoPredicate(string movieAlias, bool hasNfoDocuments, Func<string, string> predicate)
    {
        string moviePath = predicate($"{movieAlias}.NfoPath");
        return hasNfoDocuments
            ? $"({moviePath} OR EXISTS(SELECT 1 FROM NfoDocuments hnd WHERE hnd.MovieId={movieAlias}.Id AND {predicate("hnd.FilePath")}))"
            : moviePath;
    }
}

public static class MetadataHealthReader
{
    private const int AnalysisSteps = 7;

    public static async Task<MetadataHealthSummary> ReadAsync(
        string databasePath,
        MediaStorageSettingsDto storage,
        IProgress<(string Stage, int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default,
        bool includeStorageInventory = true)
    {
        Stopwatch timer = Stopwatch.StartNew();
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        MetadataHealthDefinition.RegisterFileFunctions(db);
        await db.OpenAsync(cancellationToken);
        int completedSteps = 0;
        void Report(string stage) => progress?.Report((stage, ++completedSteps, AnalysisSteps));

        long all = await ScalarAsync(db, $"SELECT COUNT(*) FROM Movies m WHERE {MetadataHealthDefinition.ActiveMovie("m")}", cancellationToken);
        long standard = await ScalarAsync(db, $"SELECT COUNT(*) FROM Movies m WHERE {MetadataHealthDefinition.StandardMovie("m")}", cancellationToken);
        long local = await ScalarAsync(db, $"SELECT COUNT(*) FROM Movies m WHERE {MetadataHealthDefinition.LocalMovie("m")}", cancellationToken);
        long unassigned = await ScalarAsync(db, $"SELECT COUNT(*) FROM Movies m WHERE {MetadataHealthDefinition.UnassignedMovie("m")}", cancellationToken);
        Report("读取统计范围");

        bool hasDirectors = await TableExistsAsync(db, "Directors", cancellationToken)
            && await TableExistsAsync(db, "MovieDirectors", cancellationToken);
        bool hasNfoDocuments = await TableExistsAsync(db, "NfoDocuments", cancellationToken);
        var states = await ReadMovieStatesAsync(db, hasDirectors, hasNfoDocuments, cancellationToken);
        Report("分析字段覆盖");

        var imageRecords = await ReadImageRecordsAsync(db, cancellationToken);
        Report("读取图片记录");

        var registered = includeStorageInventory
            ? await ReadRegisteredPathsAsync(db, cancellationToken)
            : ResourceDictionary();
        Report("读取资源登记");

        Dictionary<long, MovieHealthState> statesById = states.ToDictionary(state => state.Id);
        Dictionary<string, long> missingImageRecords;
        Dictionary<string, long> unregistered;
        if (includeStorageInventory) {
            StorageFileIndex storageFiles = await Task.Run(
                () => EnumerateStorageFiles(storage, cancellationToken), cancellationToken);
            Report("扫描媒体存储");
            foreach (ImageRecord image in imageRecords) {
                if (statesById.TryGetValue(image.MovieId, out MovieHealthState? state)) state.AddImage(image.Type, storageFiles.Exists(image.Path));
            }
            missingImageRecords = imageRecords
                .Where(item => !storageFiles.Exists(item.Path))
                .GroupBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.LongCount(), StringComparer.OrdinalIgnoreCase);
            unregistered = storageFiles.Files.ToDictionary(
                item => item.Key,
                item => item.Value.LongCount(path => !registered.TryGetValue(item.Key, out HashSet<string>? paths) || !paths.Contains(path)),
                StringComparer.OrdinalIgnoreCase);
        } else {
            Report("跳过网络盘完整盘点");
            Parallel.ForEach(imageRecords.GroupBy(item => (item.MovieId, item.Type)),
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 16 }, group => {
                    if (statesById.TryGetValue(group.Key.MovieId, out MovieHealthState? state))
                        state.AddImage(group.Key.Type, group.Any(item => MetadataHealthDefinition.FileExists(item.Path)));
                });
            missingImageRecords = new(StringComparer.OrdinalIgnoreCase);
            unregistered = ResourceDictionary().ToDictionary(item => item.Key, _ => 0L, StringComparer.OrdinalIgnoreCase);
        }
        Report("验证资源实体");

        long Count(Func<MovieHealthState, bool> predicate) => states.LongCount(predicate);
        MetadataResourceCoverage Resource(string type, Func<MovieHealthState, bool> database, Func<MovieHealthState, bool> physical) => new(
            Count(database), Count(physical), missingImageRecords.GetValueOrDefault(type), unregistered.GetValueOrDefault(type));

        MetadataResourceCoverage poster = Resource("Poster", state => state.PosterDatabase, state => state.PosterPhysical);
        MetadataResourceCoverage fanart = Resource("Fanart", state => state.FanartDatabase, state => state.FanartPhysical);
        MetadataResourceCoverage preview = Resource("Preview", state => state.PreviewDatabase, state => state.PreviewPhysical);
        MetadataResourceCoverage screenshot = Resource("Screenshot", state => state.ScreenshotDatabase, state => state.ScreenshotPhysical);
        long missingNfoRecords = Count(state => state.NfoDatabase && !state.NfoPhysical);
        var nfo = new MetadataNfoCoverage(Count(state => state.NfoDatabase), Count(state => state.NfoValidPath),
            Count(state => state.NfoPhysical), missingNfoRecords, unregistered.GetValueOrDefault("NFO"));

        long complete = Count(state => state.Complete);
        long actorMovies = Count(state => state.Actors);
        long providerTagMovies = Count(state => state.ProviderTags);
        long userTagMovies = Count(state => state.UserTags);
        var coverage = new MetadataHealthCoverage(
            Count(state => state.Number), Count(state => state.Title), Count(state => state.ReleaseDate), Count(state => state.Studios),
            actorMovies, providerTagMovies, userTagMovies, poster, fanart, preview, screenshot, nfo,
            Count(state => !state.PosterPhysical || !state.FanartPhysical),
            missingImageRecords.Values.Sum() + missingNfoRecords,
            unregistered.Values.Sum(),
            includeStorageInventory);
        var fields = new List<MetadataHealthField> {
            new("code", standard - coverage.StandardNumberMovies, true),
            new("title", standard - coverage.TitleMovies, true),
            new("releaseDate", standard - coverage.ReleaseDateMovies, true),
            new("studio", standard - coverage.StudioMovies, true),
            new("actors", standard - coverage.ActorMovies, true),
            new("officialTags", standard - coverage.ProviderTagMovies, true),
            new("poster", standard - coverage.Poster.PhysicalMovies, true),
            new("fanart", standard - coverage.Fanart.PhysicalMovies, true),
            new("nfo", standard - coverage.Nfo.PhysicalMovies, true),
            new("userTags", standard - coverage.UserTagMovies, false),
            new("description", standard - Count(state => state.Description), false),
            new("duration", standard - Count(state => state.Duration), false),
            new("director", standard - Count(state => state.Directors), false),
            new("series", standard - Count(state => state.Series), false),
            new("preview", standard - coverage.Preview.PhysicalMovies, false),
            new("screenshot", standard - coverage.Screenshot.PhysicalMovies, false),
        };
        Report("完成统计");
        timer.Stop();
        return new(standard, complete, Math.Max(0, standard - complete), standard == 0 ? 100 : complete * 100d / standard,
            fields, timer.ElapsedMilliseconds, DateTimeOffset.UtcNow.ToString("O"), new(all, standard, local, unassigned), coverage,
            MetadataHealthDefinition.CompleteRule);
    }

    private static async Task<List<MovieHealthState>> ReadMovieStatesAsync(
        SqliteConnection db,
        bool hasDirectors,
        bool hasNfoDocuments,
        CancellationToken token)
    {
        await using SqliteCommand command = db.CreateCommand();
        command.CommandText = $"""
            SELECT m.Id,
                CASE WHEN {MetadataHealthDefinition.Number("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Title("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.ReleaseDate("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Studios("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Actors("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.ProviderTags("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.UserTags("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.NfoDatabase("m", hasNfoDocuments)} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.NfoValidPath("m", hasNfoDocuments)} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.NfoPhysical("m", hasNfoDocuments)} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Description("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Duration("m")} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Directors("m", hasDirectors)} THEN 1 ELSE 0 END,
                CASE WHEN {MetadataHealthDefinition.Series("m")} THEN 1 ELSE 0 END
              FROM Movies m
             WHERE {MetadataHealthDefinition.StandardMovie("m")}
            """;
        var result = new List<MovieHealthState>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) {
            token.ThrowIfCancellationRequested();
            bool B(int index) => reader.GetInt64(index) == 1;
            result.Add(new(reader.GetInt64(0), B(1), B(2), B(3), B(4), B(5), B(6), B(7),
                B(8), B(9), B(10), B(11), B(12), B(13), B(14)));
        }
        return result;
    }

    private static async Task<List<ImageRecord>> ReadImageRecordsAsync(SqliteConnection db, CancellationToken token)
    {
        await using SqliteCommand command = db.CreateCommand();
        command.CommandText = $"""
            SELECT i.MovieId,i.ImageType,i.FilePath
              FROM Images i JOIN Movies m ON m.Id=i.MovieId
             WHERE {MetadataHealthDefinition.StandardMovie("m")}
               AND i.ImageType IN ('Poster','Fanart','BigPic','Preview','ExtraPic','Screenshot')
            """;
        var result = new List<ImageRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) {
            string type = MediaStoragePathResolver.NormalizeResourceType(reader.GetString(1));
            result.Add(new(reader.GetInt64(0), type, reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return result;
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadRegisteredPathsAsync(SqliteConnection db, CancellationToken token)
    {
        var result = ResourceDictionary();
        await using (SqliteCommand images = db.CreateCommand()) {
            images.CommandText = "SELECT ImageType,FilePath FROM Images WHERE FilePath IS NOT NULL AND ImageType IN ('Poster','Fanart','BigPic','Preview','ExtraPic','Screenshot')";
            await using SqliteDataReader reader = await images.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) {
                string type = MediaStoragePathResolver.NormalizeResourceType(reader.GetString(0));
                if (TryNormalizePath(reader.GetString(1), out string? path)) result[type].Add(path);
            }
        }
        await using (SqliteCommand nfo = db.CreateCommand()) {
            nfo.CommandText = "SELECT NfoPath FROM Movies WHERE trim(COALESCE(NfoPath,''))<>''";
            await using SqliteDataReader reader = await nfo.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) if (TryNormalizePath(reader.GetString(0), out string? path)) result["NFO"].Add(path);
        }
        if (await TableExistsAsync(db, "NfoDocuments", token)) await using (SqliteCommand documents = db.CreateCommand()) {
            documents.CommandText = "SELECT FilePath FROM NfoDocuments WHERE trim(COALESCE(FilePath,''))<>''";
            await using SqliteDataReader reader = await documents.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) if (TryNormalizePath(reader.GetString(0), out string? path)) result["NFO"].Add(path);
        }
        return result;
    }

    private static StorageFileIndex EnumerateStorageFiles(MediaStorageSettingsDto storage, CancellationToken token)
    {
        Dictionary<string, HashSet<string>> result = ResourceDictionary();
        string storageRoot;
        try { storageRoot = Path.GetFullPath(storage.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception) { storageRoot = string.Empty; }
        var resources = new[] {
            ("Poster", storage.PostersDirectory), ("Fanart", storage.FanartDirectory), ("Preview", storage.PreviewsDirectory),
            ("Screenshot", storage.ScreenshotsDirectory), ("NFO", storage.NfoDirectory),
        };
        Parallel.ForEach(resources, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = resources.Length }, resource => {
            (string type, string directory) = resource;
            string root;
            try { root = Path.GetFullPath(Path.Combine(storage.RootPath, directory)); }
            catch (Exception) { return; }
            foreach (string file in EnumerateResourceFiles(root, token))
                if (TryNormalizePath(file, out string? path)) result[type].Add(path);
        });
        return new(storageRoot, result);
    }

    private static IReadOnlyList<string> EnumerateResourceFiles(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) return [];
        FileSystemInfo[] entries;
        try { entries = new DirectoryInfo(root).EnumerateFileSystemInfos().ToArray(); }
        catch (Exception) { return []; }
        var files = new System.Collections.Concurrent.ConcurrentBag<string>(entries.Where(entry => entry is not DirectoryInfo).Select(entry => entry.FullName));
        DirectoryInfo[] directories = entries.OfType<DirectoryInfo>().ToArray();
        Parallel.ForEach(directories, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 12 }, directory => {
            foreach (string file in EnumerateFilesSafe(directory.FullName, token)) files.Add(file);
        });
        return files.ToArray();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0) {
            token.ThrowIfCancellationRequested();
            FileSystemInfo[] entries;
            try { entries = pending.Pop().EnumerateFileSystemInfos().ToArray(); }
            catch (Exception) { continue; }
            foreach (FileSystemInfo entry in entries) {
                if (entry is DirectoryInfo directory) pending.Push(directory);
                else yield return entry.FullName;
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ResourceDictionary() => new(StringComparer.OrdinalIgnoreCase) {
        ["Poster"] = new(StringComparer.OrdinalIgnoreCase), ["Fanart"] = new(StringComparer.OrdinalIgnoreCase),
        ["Preview"] = new(StringComparer.OrdinalIgnoreCase), ["Screenshot"] = new(StringComparer.OrdinalIgnoreCase),
        ["NFO"] = new(StringComparer.OrdinalIgnoreCase),
    };

    private static bool TryNormalizePath(string? value, out string path)
    {
        try { path = string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value); return path.Length > 0; }
        catch (Exception) { path = string.Empty; return false; }
    }

    private static async Task<long> ScalarAsync(SqliteConnection db, string sql, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection db, string table, CancellationToken token)
    {
        await using SqliteCommand command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) > 0;
    }

    private sealed record ImageRecord(long MovieId, string Type, string? Path);

    private sealed class MovieHealthState(long id, bool number, bool title, bool releaseDate, bool studios, bool actors,
        bool providerTags, bool userTags, bool nfoDatabase, bool nfoValidPath, bool nfoPhysical,
        bool description, bool duration, bool directors, bool series)
    {
        public long Id { get; } = id;
        public bool Number { get; } = number;
        public bool Title { get; } = title;
        public bool ReleaseDate { get; } = releaseDate;
        public bool Studios { get; } = studios;
        public bool Actors { get; } = actors;
        public bool ProviderTags { get; } = providerTags;
        public bool UserTags { get; } = userTags;
        public bool Description { get; } = description;
        public bool Duration { get; } = duration;
        public bool Directors { get; } = directors;
        public bool Series { get; } = series;
        public bool PosterDatabase { get; private set; }
        public bool PosterPhysical { get; private set; }
        public bool FanartDatabase { get; private set; }
        public bool FanartPhysical { get; private set; }
        public bool PreviewDatabase { get; private set; }
        public bool PreviewPhysical { get; private set; }
        public bool ScreenshotDatabase { get; private set; }
        public bool ScreenshotPhysical { get; private set; }
        public bool NfoDatabase { get; } = nfoDatabase;
        public bool NfoValidPath { get; } = nfoValidPath;
        public bool NfoPhysical { get; } = nfoPhysical;
        public bool Complete => Number && Title && ReleaseDate && Studios && Actors && ProviderTags && PosterPhysical && FanartPhysical && NfoPhysical;

        public void AddImage(string type, bool physical)
        {
            switch (type) {
                case "Poster": PosterDatabase = true; PosterPhysical |= physical; break;
                case "Fanart": FanartDatabase = true; FanartPhysical |= physical; break;
                case "Preview": PreviewDatabase = true; PreviewPhysical |= physical; break;
                case "Screenshot": ScreenshotDatabase = true; ScreenshotPhysical |= physical; break;
            }
        }

    }

    private sealed record StorageFileIndex(string RootPath, IReadOnlyDictionary<string, HashSet<string>> Files)
    {
        private HashSet<string> AllFiles { get; } = Files.Values.SelectMany(paths => paths).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string? value)
        {
            if (!TryNormalizePath(value, out string path)) return false;
            if (!string.IsNullOrWhiteSpace(RootPath)
                && (path.Equals(RootPath, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                return AllFiles.Contains(path);
            return MetadataHealthDefinition.FileExists(path);
        }
    }
}

public sealed record MetadataHealthAnalysisState(
    bool Running, bool Invalidated, string Stage, int CompletedSteps, int TotalSteps,
    double Percent, long ElapsedMilliseconds, MetadataHealthSummary? Result, string? Error);

public sealed class MetadataHealthAnalysisService(string databasePath, MediaStoragePathResolver pathResolver)
{
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromMinutes(15);
    private const int AnalysisSteps = 7;
    private readonly object gate = new();
    private readonly SemaphoreSlim analysisLock = new(1, 1);
    private CancellationTokenSource? cancellation;
    private MetadataHealthSummary? result;
    private bool invalidated = true;
    private string stage = "尚未分析";
    private int completedSteps;
    private int totalSteps = AnalysisSteps;
    private Stopwatch? timer;
    private string? error;
    private long analyzedDatabaseStamp;

    public async Task<MetadataHealthSummary> GetAsync(CancellationToken token = default)
    {
        MetadataHealthSummary? current;
        lock (gate) {
            RefreshInvalidationLocked();
            current = result;
        }
        if (current is not null && (!invalidated || cancellation is not null)) return current;
        await analysisLock.WaitAsync(token);
        try {
            lock (gate) {
                RefreshInvalidationLocked();
                if (result is not null && !invalidated) return result;
            }
            bool deferredInventory = await UsesNetworkStorageAsync(token);
            MetadataHealthSummary next = await AnalyzeAsync(null, token, !deferredInventory);
            lock (gate) {
                StoreResultLocked(next);
                if (deferredInventory) invalidated = true;
            }
            if (deferredInventory) Start();
            return next;
        } finally { analysisLock.Release(); }
    }

    public MetadataHealthAnalysisState Start()
    {
        CancellationTokenSource source;
        lock (gate) {
            if (cancellation is not null) return StateLocked();
            cancellation = source = new CancellationTokenSource();
            invalidated = true;
            timer = Stopwatch.StartNew();
            error = null;
            stage = "准备";
            completedSteps = 0;
            totalSteps = AnalysisSteps;
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

    public MetadataHealthAnalysisState GetState()
    {
        lock (gate) {
            RefreshInvalidationLocked();
            return StateLocked();
        }
    }

    private async Task RunAsync(CancellationTokenSource source)
    {
        try {
            await analysisLock.WaitAsync(source.Token);
            try {
                var progress = new Progress<(string Stage, int Completed, int Total)>(value => {
                    lock (gate) { stage = value.Stage; completedSteps = value.Completed; totalSteps = value.Total; }
                });
                MetadataHealthSummary next = await AnalyzeAsync(progress, source.Token, true);
                lock (gate) StoreResultLocked(next);
            } finally { analysisLock.Release(); }
        } catch (OperationCanceledException) {
            lock (gate) stage = "已取消，保留上次结果";
        } catch (Exception exception) {
            lock (gate) { stage = "分析失败"; error = exception.Message; }
        } finally {
            lock (gate) {
                timer?.Stop();
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }

    private async Task<MetadataHealthSummary> AnalyzeAsync(IProgress<(string Stage, int Completed, int Total)>? progress, CancellationToken token, bool includeStorageInventory)
    {
        MediaStorageSettingsDto storage = await pathResolver.GetSettingsAsync(token);
        return await MetadataHealthReader.ReadAsync(databasePath, storage, progress, token, includeStorageInventory);
    }

    private async Task<bool> UsesNetworkStorageAsync(CancellationToken token)
    {
        MediaStorageSettingsDto storage = await pathResolver.GetSettingsAsync(token);
        try {
            if (storage.RootPath.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            string? root = Path.GetPathRoot(Path.GetFullPath(storage.RootPath));
            return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).DriveType == DriveType.Network;
        } catch (Exception) { return false; }
    }

    private void StoreResultLocked(MetadataHealthSummary next)
    {
        result = next;
        invalidated = false;
        analyzedDatabaseStamp = DatabaseStamp();
        stage = "已完成";
        completedSteps = totalSteps;
        error = null;
    }

    private void RefreshInvalidationLocked()
    {
        if (result is null) return;
        if (DatabaseStamp() != analyzedDatabaseStamp || DateTimeOffset.UtcNow - DateTimeOffset.Parse(result.AnalyzedAt) >= MaxCacheAge)
            invalidated = true;
    }

    private MetadataHealthAnalysisState StateLocked()
    {
        bool running = cancellation is not null;
        long elapsed = timer?.ElapsedMilliseconds ?? result?.AnalysisDurationMs ?? 0;
        return new(running, invalidated, stage, completedSteps, totalSteps,
            totalSteps == 0 ? 0 : completedSteps * 100d / totalSteps, elapsed, result, error);
    }

    private long DatabaseStamp() => new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
        .Where(File.Exists).Select(File.GetLastWriteTimeUtc).Select(value => value.Ticks).DefaultIfEmpty(0).Max();
}
