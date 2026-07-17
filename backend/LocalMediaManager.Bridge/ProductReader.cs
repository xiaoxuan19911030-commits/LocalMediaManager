using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetadataCheckDto(string Key, string Label, bool Complete);
public sealed record MetadataStatusDto(string State, string Icon, string Label, IReadOnlyList<string> MissingItems, IReadOnlyList<MetadataCheckDto> Checks);
public sealed record MediaCardDto(long DataId, string Code, string Title, string Path, double Grade,
    bool Favorite, string ReleaseDate, string ImportedAt, string? CoverUrl, MetadataStatusDto MetadataStatus);
public sealed record DashboardDto(long MovieCount, long FavoriteCount, long PlayedCount, long MissingFileCount,
    long LibraryCount, long ActiveTaskCount, long CompleteMetadataCount, long PendingMetadataCount, long UnscrapedCount,
    IReadOnlyList<MediaCardDto> RecentImports, IReadOnlyList<MediaCardDto> RecentPlays);
public sealed record SearchEntityDto(long Id, string Name, long MovieCount);
public sealed record GlobalSearchDto(string Query, IReadOnlyList<MediaCardDto> Movies,
    IReadOnlyList<SearchEntityDto> Actors, IReadOnlyList<SearchEntityDto> Tags);
public sealed record LibraryFolderDto(long Id, string Path, bool Enabled, bool IncludeSubfolders, string ScanMode,
    string? LastScannedAt, IReadOnlyList<string> ExcludePatterns);
public sealed record LibraryDto(long Id, string Name, string? Description, bool Enabled, long MovieCount,
    long MissingCount, IReadOnlyList<LibraryFolderDto> Folders);
public sealed record TaskDto(long Id, string Type, string Status, string Name, double Progress, long TotalItems,
    long CompletedItems, string? ErrorMessage, string CreatedAt, string? StartedAt, string? CompletedAt,
    string? Stage, string? Provider, long RetryCount, long? CurrentMovieId, string? ResultSummary);
public sealed record TaskLogDto(long Id, string Level, string Message, string CreatedAt);
public sealed record NamedDto(long Id, string Name);
public sealed record MediaFileDto(long Id, string Path, string FileName, string? Extension, long FileSize,
    string SourceType, string ExistsState, bool Primary);
public sealed record MovieDetailDto(long Id, string? Code, string? Title, string? OriginalTitle, string? ReleaseDate,
    long DurationSeconds, string? Description, double ProviderRating, bool Scraped, string ScrapeStatus,
    string? NfoPath, string? ImportedAt, string UpdatedAt, bool Favorite, double UserRating, bool UserRatingSet, long PlayCount,
    string? LastPlayedAt, long LastPositionSeconds, string? Notes, string? CoverUrl,
    MetadataStatusDto MetadataStatus, IReadOnlyList<MediaFileDto> MediaFiles, IReadOnlyList<NamedDto> Actors, IReadOnlyList<NamedDto> Directors, IReadOnlyList<NamedDto> Tags,
    IReadOnlyList<NamedDto> Genres, IReadOnlyList<NamedDto> Studios, IReadOnlyList<NamedDto> Series);
public sealed record EntityCardDto(long Id, string Name, long MovieCount, string? ImageUrl);
public sealed record ActorDetailDto(long Id, string Name, string? Alias, int? Gender, string? BirthDate, string? Description);
public sealed record EntityPageDto(IReadOnlyList<EntityCardDto> Items, long Total, int Limit, int Offset);
public sealed record MediaPageDto(IReadOnlyList<MediaCardDto> Items, long Total, int Limit, int Offset);
public sealed record MetadataOverviewDto(long TotalMovies, long ScrapedMovies, long CompleteMovies, long PendingMovies, long UnscrapedMovies,
    long MissingTitle, long MissingCover, long MissingFanart, long MissingPreview, long MissingActors, long MissingTags,
    long MissingDescription, long MissingNfo, long MissingFiles);
public sealed record DiagnosticItemDto(string Severity, string Code, string Title, string Detail, long Count);
public sealed record DiagnosticsDto(string Integrity, long ForeignKeyErrors, IReadOnlyList<DiagnosticItemDto> Items);
public sealed record NeighborsDto(long? PreviousId, long? NextId);
public sealed record DuplicateMovieDto(long MovieId, string Code, string Title, string FilePath, string? FileHash, string ImportedAt, string Recommendation);
public sealed record DuplicateGroupDto(string Rule, string Key, long Count, IReadOnlyList<DuplicateMovieDto> Items);
public sealed record DuplicateResultsDto(long TotalGroups, long TotalMovies, long CodeGroups, long PathGroups, long HashGroups, IReadOnlyList<DuplicateGroupDto> Groups);
internal sealed record SearchCondition(string Query, bool? Favorite, bool? Watched, double? RatingMin);

public static class ProductReader
{
    public static async Task<DashboardDto> ReadDashboardAsync(string databasePath, string bridgeUrl)
    {
        await using var connection = await OpenAsync(databasePath);
        long movies = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies");
        long favorites = await ScalarAsync(connection, "SELECT COUNT(*) FROM UserMovieState WHERE IsFavorite=1");
        long played = await ScalarAsync(connection, "SELECT COUNT(*) FROM UserMovieState WHERE PlayCount>0");
        long missing = await ScalarAsync(connection, "SELECT COUNT(DISTINCT MovieId) FROM MediaFiles WHERE ExistsState='Missing'");
        long libraries = await ScalarAsync(connection, "SELECT COUNT(*) FROM Libraries WHERE IsEnabled=1");
        long tasks = await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Status NOT IN ('Completed','Failed','Cancelled')");
        (long complete, long pending, long unscraped) = await ReadMetadataCountsAsync(connection);
        var recentImports = await ReadCardsAsync(connection, bridgeUrl, "m.ImportedAt DESC, m.Id DESC", 8, false);
        var recentPlays = await ReadCardsAsync(connection, bridgeUrl, "s.LastPlayedAt DESC, m.Id DESC", 8, true);
        return new(movies, favorites, played, missing, libraries, tasks, complete, pending, unscraped, recentImports, recentPlays);
    }

    public static async Task<GlobalSearchDto> SearchAsync(string databasePath, string bridgeUrl, string query, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        string like = $"%{EscapeLike(query.Trim())}%";
        var movies = new List<MediaCardDto>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = $"""
                SELECT m.Id,COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(m.Title,''),
                       COALESCE(f.FilePath,''),COALESCE(s.UserRating,0),COALESCE(s.IsFavorite,0),
                       COALESCE(m.ReleaseDate,''),COALESCE(m.ImportedAt,m.CreatedAt,''),
                       EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id),
                       {MetadataColumnsSql(await HasDirectorsAsync(connection))}
                  FROM Movies m
                  LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
                  LEFT JOIN UserMovieState s ON s.MovieId=m.Id
                 WHERE m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\' OR m.OriginalTitle LIKE $like ESCAPE '\'
                 ORDER BY CASE WHEN m.Code=$exact THEN 0 WHEN m.Code LIKE $prefix ESCAPE '\' THEN 1 ELSE 2 END,m.Id DESC
                 LIMIT $limit
                """;
            command.Parameters.AddWithValue("$like", like); command.Parameters.AddWithValue("$exact", query.Trim());
            command.Parameters.AddWithValue("$prefix", EscapeLike(query.Trim()) + "%"); command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) movies.Add(Card(reader, bridgeUrl));
        }
        var actors = await ReadEntitiesAsync(connection, "Actors", "MovieActors", "ActorId", like, limit);
        var tags = await ReadEntitiesAsync(connection, "Tags", "MovieTags", "TagId", like, limit);
        return new(query.Trim(), movies, actors, tags);
    }

    public static async Task<IReadOnlyList<LibraryDto>> ReadLibrariesAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);
        var libraries = new List<(long Id,string Name,string? Description,bool Enabled,long Movies,long Missing)>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT l.Id,l.Name,l.Description,l.IsEnabled,
                       COUNT(DISTINCT f.MovieId),COUNT(DISTINCT CASE WHEN f.ExistsState='Missing' THEN f.MovieId END)
                  FROM Libraries l LEFT JOIN MediaFiles f ON f.LibraryId=l.Id
                 GROUP BY l.Id ORDER BY l.SortOrder,l.Name
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) libraries.Add((reader.GetInt64(0),reader.GetString(1),Text(reader,2),reader.GetInt64(3)==1,reader.GetInt64(4),reader.GetInt64(5)));
        }
        var result = new List<LibraryDto>();
        foreach (var library in libraries) {
            var folders = new List<LibraryFolderDto>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,FolderPath,IsEnabled,IncludeSubfolders,ScanMode,LastScannedAt,ExcludePatternsJson FROM LibraryFolders WHERE LibraryId=$id ORDER BY Id";
            command.Parameters.AddWithValue("$id", library.Id);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                IReadOnlyList<string> excludePatterns;
                try { excludePatterns = System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.IsDBNull(6) ? "[]" : reader.GetString(6)) ?? []; }
                catch (System.Text.Json.JsonException) { excludePatterns = []; }
                folders.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2)==1,
                    reader.GetInt64(3)==1,reader.GetString(4),Text(reader,5),excludePatterns));
            }
            result.Add(new(library.Id,library.Name,library.Description,library.Enabled,library.Movies,library.Missing,folders));
        }
        return result;
    }

    public static async Task<IReadOnlyList<TaskDto>> ReadTasksAsync(string databasePath, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        var libraryNames = new Dictionary<long, string>();
        await using (var libraries = connection.CreateCommand()) {
            libraries.CommandText = "SELECT Id,Name FROM Libraries";
            await using var libraryReader = await libraries.ExecuteReaderAsync();
            while (await libraryReader.ReadAsync()) libraryNames[libraryReader.GetInt64(0)] = libraryReader.GetString(1);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,TaskType,Status,Progress,TotalItems,CompletedItems,ErrorMessage,CreatedAt,StartedAt,CompletedAt,PayloadJson,Stage,Provider,RetryCount,CurrentMovieId,ResultSummary FROM Tasks ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        var tasks = new List<TaskDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            string type = reader.GetString(1);
            string? payload = Text(reader, 10);
            tasks.Add(new(reader.GetInt64(0),type,reader.GetString(2),TaskName(type,payload,libraryNames),reader.GetDouble(3),
                reader.GetInt64(4),reader.GetInt64(5),Text(reader,6),reader.GetString(7),Text(reader,8),Text(reader,9),
                Text(reader,11),Text(reader,12),reader.GetInt64(13),reader.IsDBNull(14)?null:reader.GetInt64(14),Text(reader,15)));
        }
        return tasks;
    }

    public static async Task<IReadOnlyList<TaskLogDto>> ReadTaskLogsAsync(string databasePath, long taskId, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        if (await ScalarAsync(connection, $"SELECT COUNT(*) FROM Tasks WHERE Id={taskId}") == 0)
            throw new KeyNotFoundException("任务不存在。");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Level,Message,CreatedAt FROM TaskLogs WHERE TaskId=$task ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<TaskLogDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));
        result.Reverse();
        return result;
    }

    private static string TaskName(string type, string? payload, IReadOnlyDictionary<long, string> libraryNames)
    {
        if (!string.IsNullOrWhiteSpace(payload)) {
            try {
                using var json = System.Text.Json.JsonDocument.Parse(payload);
                if (json.RootElement.TryGetProperty("LibraryId", out var library) && library.TryGetInt64(out long libraryId))
                    return libraryNames.GetValueOrDefault(libraryId, $"媒体库 #{libraryId}");
                if (json.RootElement.TryGetProperty("MovieId", out var movie) && movie.TryGetInt64(out long movieId))
                    return $"影片 #{movieId}";
            } catch (System.Text.Json.JsonException) { }
        }
        return type switch { "ActorRepair" => "演员关系修复", _ => type };
    }

    public static async Task<EntityPageDto> ReadEntitiesPageAsync(string databasePath, string bridgeUrl,
        string entityType, string search, string sort, int limit, int offset)
    {
        bool actors = entityType.Equals("actors", StringComparison.OrdinalIgnoreCase);
        string table = actors ? "Actors" : "Tags";
        string relation = actors ? "MovieActors" : "MovieTags";
        string key = actors ? "ActorId" : "TagId";
        string like = $"%{EscapeLike(search.Trim())}%";
        string orderBy = sort.Equals("name", StringComparison.OrdinalIgnoreCase) ? "e.Name COLLATE NOCASE" : "MovieCount DESC,e.Name COLLATE NOCASE";
        await using var connection = await OpenAsync(databasePath);
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM {table} e WHERE $search='' OR e.Name LIKE $like ESCAPE '\\'";
        count.Parameters.AddWithValue("$search", search.Trim()); count.Parameters.AddWithValue("$like", like);
        long total = Convert.ToInt64(await count.ExecuteScalarAsync() ?? 0L);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT e.Id,e.Name,COUNT(DISTINCT r.MovieId) AS MovieCount
              FROM {table} e LEFT JOIN {relation} r ON r.{key}=e.Id
             WHERE $search='' OR e.Name LIKE $like ESCAPE '\'
             GROUP BY e.Id ORDER BY {orderBy} LIMIT $limit OFFSET $offset
            """;
        command.Parameters.AddWithValue("$search", search.Trim()); command.Parameters.AddWithValue("$like", like);
        command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
        var items = new List<EntityCardDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long id = reader.GetInt64(0);
            items.Add(new(id, reader.GetString(1), reader.GetInt64(2), actors ? $"{bridgeUrl}/api/actors/{id}/image" : null));
        }
        return new(items, total, limit, offset);
    }

    public static async Task<MediaPageDto> ReadEntityMoviesAsync(string databasePath, string bridgeUrl,
        string entityType, long entityId, int limit, int offset)
    {
        bool actors = entityType.Equals("actors", StringComparison.OrdinalIgnoreCase);
        string relation = actors ? "MovieActors" : "MovieTags";
        string key = actors ? "ActorId" : "TagId";
        return await ReadFilteredCardsAsync(databasePath, bridgeUrl,
            $"EXISTS(SELECT 1 FROM {relation} er WHERE er.MovieId=m.Id AND er.{key}=$entity)",
            [("$entity", entityId)], "m.ImportedAt DESC,m.Id DESC", limit, offset);
    }

    public static async Task<MediaPageDto> ReadCollectionAsync(string databasePath, string bridgeUrl,
        string kind, int limit, int offset)
    {
        string condition = kind.Equals("favorites", StringComparison.OrdinalIgnoreCase) ? "COALESCE(s.IsFavorite,0)=1" : "COALESCE(s.PlayCount,0)>0";
        string order = kind.Equals("favorites", StringComparison.OrdinalIgnoreCase) ? "s.UpdatedAt DESC,m.Id DESC" : "s.LastPlayedAt DESC,m.Id DESC";
        return await ReadFilteredCardsAsync(databasePath, bridgeUrl, condition, [], order, limit, offset);
    }

    public static async Task<MediaPageDto> ReadVideosPageAsync(string databasePath, string bridgeUrl, string search, string sort, int limit, int offset)
    {
        string query = search.Trim();
        string condition = "$search='' OR m.Code LIKE $like ESCAPE '\\' OR m.Title LIKE $like ESCAPE '\\'";
        var parameters = new List<(string, object)> { ("$search", query), ("$like", $"%{EscapeLike(query)}%") };
        string order = sort.ToLowerInvariant() switch {
            "code" => "m.Code COLLATE NOCASE, m.Id",
            "title" => "m.Title COLLATE NOCASE, m.Id",
            "release" => "m.ReleaseDate DESC, m.Id DESC",
            "rating" => "s.UserRating DESC, m.Id DESC",
            _ => "m.Id DESC",
        };
        return await ReadFilteredCardsAsync(databasePath, bridgeUrl, condition, parameters, order, limit, offset);
    }

    public static async Task<MediaPageDto> AdvancedSearchAsync(string databasePath, string bridgeUrl,
        string query, long? actorId, long? tagId, bool? favorite, bool? watched, double ratingMin, string metadata,
        string fileStatus, string metadataStatus, long? libraryId, string sort, int limit, int offset)
    {
        var conditions = new List<string>(); var parameters = new List<(string,object)>();
        var parsed = ParseSearchCondition(query);
        string trimmed = parsed.Query;
        favorite ??= parsed.Favorite;
        watched ??= parsed.Watched;
        ratingMin = Math.Clamp(Math.Max(ratingMin, parsed.RatingMin ?? 0), 0, 5);
        bool hasDirectors = await TableExistsAsync(databasePath, "Directors") && await TableExistsAsync(databasePath, "MovieDirectors");
        if (trimmed.Length > 0) {
            string directorCondition = hasDirectors ? """ OR EXISTS(SELECT 1 FROM MovieDirectors md JOIN Directors d ON d.Id=md.DirectorId WHERE md.MovieId=m.Id AND d.Name LIKE $like ESCAPE '\')""" : "";
            conditions.Add($"""(m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\' OR m.OriginalTitle LIKE $like ESCAPE '\' OR f.FilePath LIKE $like ESCAPE '\' OR f.FileName LIKE $like ESCAPE '\' OR EXISTS(SELECT 1 FROM MovieActors ma JOIN Actors a ON a.Id=ma.ActorId WHERE ma.MovieId=m.Id AND a.Name LIKE $like ESCAPE '\'){directorCondition} OR EXISTS(SELECT 1 FROM MovieTags mt JOIN Tags t ON t.Id=mt.TagId WHERE mt.MovieId=m.Id AND t.Name LIKE $like ESCAPE '\') OR EXISTS(SELECT 1 FROM MovieTags mt JOIN Tags t ON t.Id=mt.TagId WHERE mt.MovieId=m.Id AND t.Source='User' AND t.Name LIKE $like ESCAPE '\') OR EXISTS(SELECT 1 FROM MovieStudios ms JOIN Studios st ON st.Id=ms.StudioId WHERE ms.MovieId=m.Id AND st.Name LIKE $like ESCAPE '\') OR EXISTS(SELECT 1 FROM MovieSeries mse JOIN Series se ON se.Id=mse.SeriesId WHERE mse.MovieId=m.Id AND se.Name LIKE $like ESCAPE '\'))""");
            parameters.Add(("$like", $"%{EscapeLike(trimmed)}%"));
        }
        if (actorId.HasValue) { conditions.Add("EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id AND ma.ActorId=$actor)"); parameters.Add(("$actor", actorId.Value)); }
        if (tagId.HasValue) { conditions.Add("EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id AND mt.TagId=$tag)"); parameters.Add(("$tag", tagId.Value)); }
        if (favorite.HasValue) { conditions.Add("COALESCE(s.IsFavorite,0)=$favorite"); parameters.Add(("$favorite", favorite.Value ? 1 : 0)); }
        if (watched.HasValue) conditions.Add(watched.Value ? "COALESCE(s.PlayCount,0)>0" : "COALESCE(s.PlayCount,0)=0");
        if (ratingMin > 0) { conditions.Add("COALESCE(s.UserRating,0)>=$rating"); parameters.Add(("$rating", ratingMin)); }
        if (metadata == "complete") conditions.Add("m.IsScraped=1"); else if (metadata == "missing") conditions.Add("m.IsScraped=0");
        AddMetadataStatusCondition(conditions, metadataStatus, hasDirectors);
        if (fileStatus == "missing") conditions.Add("f.ExistsState='Missing'"); else if (fileStatus == "available") conditions.Add("f.ExistsState<>'Missing'");
        if (libraryId.HasValue) { conditions.Add("f.LibraryId=$library"); parameters.Add(("$library", libraryId.Value)); }
        string order = sort switch { "code" => "m.Code COLLATE NOCASE,m.Id", "rating" => "s.UserRating DESC,m.Id DESC", "release" => "m.ReleaseDate DESC,m.Id DESC", _ => "m.ImportedAt DESC,m.Id DESC" };
        return await ReadFilteredCardsAsync(databasePath, bridgeUrl, conditions.Count == 0 ? "1=1" : string.Join(" AND ", conditions), parameters, order, limit, offset);
    }

    private static SearchCondition ParseSearchCondition(string query)
    {
        bool? favorite = null;
        bool? watched = null;
        double? ratingMin = null;
        var terms = new List<string>();
        foreach (string token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string normalized = token.Trim();
            string lower = normalized.ToLowerInvariant();
            if (normalized is "收藏" or "已收藏" || lower is "favorite" or "fav") { favorite = true; continue; }
            if (normalized is "已观看" or "看过" or "已播放" || lower is "watched" or "played") { watched = true; continue; }
            if (TryParseRatingCondition(normalized, out double rating)) { ratingMin = rating; continue; }
            terms.Add(token);
        }
        return new(string.Join(' ', terms), favorite, watched, ratingMin);
    }

    private static bool TryParseRatingCondition(string token, out double rating)
    {
        rating = 0;
        string normalized = token.Trim().Replace("＞", ">").Replace("＝", "=");
        string prefix = normalized.StartsWith("评分>=", StringComparison.OrdinalIgnoreCase) ? "评分>=" :
            normalized.StartsWith("rating>=", StringComparison.OrdinalIgnoreCase) ? "rating>=" : "";
        if (prefix.Length == 0) return false;
        return double.TryParse(normalized[prefix.Length..], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out rating);
    }

    public static async Task<MetadataOverviewDto> ReadMetadataOverviewAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);
        (long complete, long pending, long unscraped) = await ReadMetadataCountsAsync(connection);
        return new(
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE IsScraped=1"),
            complete,
            pending,
            unscraped,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE trim(COALESCE(Title,''))=''"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE " + MissingCoverSql()),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE " + MissingFanartSql()),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE " + MissingPreviewSql()),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE NOT EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id)"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE NOT EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id)"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE trim(COALESCE(Description,''))=''"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE trim(COALESCE(NfoPath,''))=''"),
            await ScalarAsync(connection, "SELECT COUNT(DISTINCT MovieId) FROM MediaFiles WHERE ExistsState='Missing'"));
    }

    public static async Task<DiagnosticsDto> ReadDiagnosticsAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var integrityCommand = connection.CreateCommand(); integrityCommand.CommandText = "PRAGMA integrity_check";
        string integrity = Convert.ToString(await integrityCommand.ExecuteScalarAsync()) ?? "unknown";
        long foreignKeys = await CountRowsAsync(connection, "PRAGMA foreign_key_check");
        var items = new List<DiagnosticItemDto> {
            new("error","MISSING_FILE","缺失媒体文件","数据库记录存在，但对应文件当前不可用。",await ScalarAsync(connection,"SELECT COUNT(*) FROM MediaFiles WHERE ExistsState='Missing'")),
            new("warning","DUPLICATE_CODE","重复番号","多个影片使用相同的非空番号。",await ScalarAsync(connection,"SELECT COUNT(*) FROM (SELECT upper(trim(Code)) c FROM Movies WHERE trim(COALESCE(Code,''))<>'' GROUP BY c HAVING COUNT(*)>1)")),
            new("warning","CORRUPT_TEXT","损坏文本","标题、演员或标签包含 Unicode 替换字符。",await ScalarAsync(connection,"SELECT (SELECT COUNT(*) FROM Movies WHERE instr(COALESCE(Title,''),'�')>0)+(SELECT COUNT(*) FROM Actors WHERE instr(Name,'�')>0)+(SELECT COUNT(*) FROM Tags WHERE instr(Name,'�')>0)")),
            new("warning","MIGRATION_WARNING","迁移警告","旧数据迁移时保留的兼容性警告。",await ScalarAsync(connection,"SELECT COUNT(*) FROM MigrationWarnings")),
            new("info","MISSING_COVER","缺少图片","尚未关联任何图片资源的影片。",await ScalarAsync(connection,"SELECT COUNT(*) FROM Movies m WHERE NOT EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id)")),
            new("info","MISSING_ACTOR","缺少演员","尚未关联演员的影片。",await ScalarAsync(connection,"SELECT COUNT(*) FROM Movies m WHERE NOT EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id)"))
        };
        return new(integrity, foreignKeys, items);
    }

    public static async Task<DuplicateResultsDto> ReadDuplicateResultsAsync(string databasePath, string rule, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        string normalizedRule = rule.ToLowerInvariant();
        if (normalizedRule is not ("all" or "code" or "path" or "hash")) normalizedRule = "all";
        var groups = new List<DuplicateGroupDto>();
        if (normalizedRule is "all" or "code")
            groups.AddRange(await ReadDuplicateGroupsAsync(connection, "code", "upper(trim(m.Code))", "Movies m", "trim(COALESCE(m.Code,''))<>''", "m.Id", limit));
        if (normalizedRule is "all" or "path")
            groups.AddRange(await ReadDuplicateGroupsAsync(connection, "path", "lower(trim(f.NormalizedPath))", "MediaFiles f JOIN Movies m ON m.Id=f.MovieId", "trim(COALESCE(f.NormalizedPath,''))<>''", "f.MovieId", limit));
        if (normalizedRule is "all" or "hash")
            groups.AddRange(await ReadDuplicateGroupsAsync(connection, "hash", "lower(trim(f.FileHash))", "MediaFiles f JOIN Movies m ON m.Id=f.MovieId", "trim(COALESCE(f.FileHash,''))<>''", "f.MovieId", limit));
        groups = groups.OrderBy(item => item.Rule).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        long totalMovies = groups.SelectMany(group => group.Items.Select(item => item.MovieId)).Distinct().LongCount();
        return new(groups.Count, totalMovies,
            groups.LongCount(group => group.Rule == "code"),
            groups.LongCount(group => group.Rule == "path"),
            groups.LongCount(group => group.Rule == "hash"),
            groups);
    }

    private static async Task<IReadOnlyList<DuplicateGroupDto>> ReadDuplicateGroupsAsync(SqliteConnection connection, string rule, string keyExpression, string from, string where, string movieIdExpression, int limit)
    {
        var keys = new List<(string Key, long Count)>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = $"""
                SELECT {keyExpression} AS DuplicateKey, COUNT(DISTINCT {movieIdExpression}) AS DuplicateCount
                  FROM {from}
                 WHERE {where}
                 GROUP BY DuplicateKey
                HAVING COUNT(DISTINCT {movieIdExpression}) > 1
                 ORDER BY DuplicateCount DESC, DuplicateKey COLLATE NOCASE
                 LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) keys.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        var groups = new List<DuplicateGroupDto>();
        foreach ((string key, long count) in keys) groups.Add(new(rule, key, count, await ReadDuplicateItemsAsync(connection, rule, key)));
        return groups;
    }

    private static async Task<IReadOnlyList<DuplicateMovieDto>> ReadDuplicateItemsAsync(SqliteConnection connection, string rule, string key)
    {
        string condition = rule switch {
            "code" => "upper(trim(m.Code))=$key",
            "path" => "lower(trim(f.NormalizedPath))=$key",
            _ => "lower(trim(f.FileHash))=$key",
        };
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.Id,COALESCE(m.Code,''),COALESCE(m.Title,''),COALESCE(f.FilePath,''),f.FileHash,COALESCE(m.ImportedAt,m.CreatedAt,''),
                   COALESCE(s.IsFavorite,0),COALESCE(s.UserRating,0),COALESCE(f.FileSize,0),COALESCE(s.PlayCount,0),COALESCE(s.LastPlayedAt,'')
              FROM Movies m
              LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND (f.IsPrimary=1 OR $rule<>'code')
              LEFT JOIN UserMovieState s ON s.MovieId=m.Id
             WHERE {condition}
             ORDER BY m.Code COLLATE NOCASE,m.Id,f.Id
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$rule", rule);
        var items = new List<DuplicateMovieDto>();
        var seen = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync();
        var candidates = new List<(DuplicateMovieDto Item, long Favorite, double Rating, long Size, long Plays, string LastPlayed)>();
        while (await reader.ReadAsync()) {
            long id = reader.GetInt64(0);
            if (!seen.Add(id)) continue;
            candidates.Add((new(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), Text(reader, 4), reader.GetString(5), "建议删除"),
                reader.GetInt64(6), reader.GetDouble(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetString(10)));
        }
        long keepId = candidates.OrderByDescending(item => item.Favorite).ThenByDescending(item => item.Rating)
            .ThenByDescending(item => item.Size).ThenByDescending(item => item.Plays)
            .ThenByDescending(item => item.LastPlayed, StringComparer.Ordinal).FirstOrDefault().Item?.MovieId ?? 0;
        foreach (var candidate in candidates)
            items.Add(candidate.Item with { Recommendation = candidate.Item.MovieId == keepId ? "推荐保留" : "建议删除" });
        return items;
    }

    public static async Task<MovieDetailDto?> ReadMovieAsync(string databasePath, string bridgeUrl, long movieId)
    {
        await using var connection = await OpenAsync(databasePath);
        (long Id,string? Code,string? Title,string? Original,string? Release,long Duration,string? Description,double Provider,bool Scraped,string Status,string? Nfo,string? Imported,string Updated,bool Favorite,double Rating,bool RatingSet,long Plays,string? LastPlayed,long Position,string? Notes,bool HasCover)? movie = null;
        await using (var command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT m.Id,m.Code,m.Title,m.OriginalTitle,m.ReleaseDate,m.DurationSeconds,m.Description,COALESCE(m.ProviderRating,0),m.IsScraped,m.ScrapeStatus,m.NfoPath,m.ImportedAt,m.UpdatedAt,
                       COALESCE(s.IsFavorite,0),COALESCE(s.UserRating,0),COALESCE(s.HasUserRating,0),COALESCE(s.PlayCount,0),s.LastPlayedAt,COALESCE(s.LastPositionSeconds,0),s.Notes,
                       EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id)
                  FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id
                """;
            command.Parameters.AddWithValue("$id", movieId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync()) movie=(reader.GetInt64(0),Text(reader,1),Text(reader,2),Text(reader,3),Text(reader,4),reader.GetInt64(5),Text(reader,6),reader.GetDouble(7),reader.GetInt64(8)==1,reader.GetString(9),Text(reader,10),Text(reader,11),reader.GetString(12),reader.GetInt64(13)==1,reader.GetDouble(14),reader.GetInt64(15)==1,reader.GetInt64(16),Text(reader,17),reader.GetInt64(18),Text(reader,19),reader.GetInt64(20)==1);
        }
        if (movie is null) return null;
        var value = movie.Value;
        MetadataStatusDto metadataStatus = await ReadMetadataStatusAsync(connection, value.Id);
        var files = new List<MediaFileDto>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText="SELECT Id,FilePath,FileName,Extension,FileSize,SourceType,ExistsState,IsPrimary FROM MediaFiles WHERE MovieId=$id ORDER BY IsPrimary DESC,Id"; command.Parameters.AddWithValue("$id",movieId);
            await using var reader=await command.ExecuteReaderAsync(); while(await reader.ReadAsync()) files.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),Text(reader,3),reader.GetInt64(4),reader.GetString(5),reader.GetString(6),reader.GetInt64(7)==1));
        }
        return new(value.Id,value.Code,value.Title,value.Original,value.Release,value.Duration,value.Description,value.Provider,value.Scraped,value.Status,value.Nfo,value.Imported,value.Updated,value.Favorite,value.Rating,value.RatingSet,value.Plays,value.LastPlayed,value.Position,value.Notes,value.HasCover?$"{bridgeUrl}/api/images/{movieId}/primary":null,
            metadataStatus,files,await ReadNamesAsync(connection,"Actors","MovieActors","ActorId",movieId),await ReadNamesIfExistsAsync(connection,"Directors","MovieDirectors","DirectorId",movieId),await ReadNamesAsync(connection,"Tags","MovieTags","TagId",movieId),
            await ReadNamesAsync(connection,"Genres","MovieGenres","GenreId",movieId),await ReadNamesAsync(connection,"Studios","MovieStudios","StudioId",movieId),await ReadNamesAsync(connection,"Series","MovieSeries","SeriesId",movieId));
    }

    public static async Task<ActorDetailDto?> ReadActorAsync(string databasePath, long actorId)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Alias,Gender,BirthDate,Description FROM Actors WHERE Id=$id";
        command.Parameters.AddWithValue("$id", actorId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new(reader.GetInt64(0), reader.GetString(1), Text(reader, 2), reader.IsDBNull(3) ? null : reader.GetInt32(3), Text(reader, 4), Text(reader, 5))
            : null;
    }

    public static async Task<NeighborsDto> ReadNeighborsAsync(string databasePath, long movieId, string search, string sort)
    {
        string orderBy = sort.ToLowerInvariant() switch {
            "code" => "m.Code COLLATE NOCASE, m.Id",
            "title" => "m.Title COLLATE NOCASE, m.Id",
            "release" => "m.ReleaseDate DESC, m.Id DESC",
            "rating" => "COALESCE(s.UserRating,0) DESC, m.Id DESC",
            _ => "m.Id DESC",
        };
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH ordered AS (
                SELECT m.Id, ROW_NUMBER() OVER (ORDER BY {orderBy}) AS rn
                FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id
                WHERE $search='' OR m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\'
            ), current AS (SELECT rn FROM ordered WHERE Id=$id)
            SELECT
                (SELECT Id FROM ordered WHERE rn=(SELECT rn-1 FROM current)),
                (SELECT Id FROM ordered WHERE rn=(SELECT rn+1 FROM current))
            """;
        command.Parameters.AddWithValue("$id", movieId);
        command.Parameters.AddWithValue("$search", search.Trim());
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(search.Trim())}%");
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new(null, null);
        return new(reader.IsDBNull(0) ? null : reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private static async Task<List<MediaCardDto>> ReadCardsAsync(SqliteConnection connection, string bridgeUrl, string orderBy, int limit, bool playedOnly)
    {
        string metadataColumns = MetadataColumnsSql(await HasDirectorsAsync(connection));
        await using var command=connection.CreateCommand();
        command.CommandText=$"""
            SELECT m.Id,COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(m.Title,''),COALESCE(f.FilePath,''),
                   COALESCE(s.UserRating,0),COALESCE(s.IsFavorite,0),COALESCE(m.ReleaseDate,''),COALESCE(m.ImportedAt,m.CreatedAt,''),EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id),
                   {metadataColumns}
              FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' LEFT JOIN UserMovieState s ON s.MovieId=m.Id
             WHERE {(playedOnly ? "COALESCE(s.PlayCount,0)>0" : "1=1")} ORDER BY {orderBy} LIMIT $limit
            """; command.Parameters.AddWithValue("$limit",limit);
        var items=new List<MediaCardDto>(); await using var reader=await command.ExecuteReaderAsync(); while(await reader.ReadAsync()) items.Add(Card(reader,bridgeUrl)); return items;
    }
    private static async Task<MediaPageDto> ReadFilteredCardsAsync(string databasePath, string bridgeUrl,
        string condition, IReadOnlyList<(string Name,object Value)> parameters, string orderBy, int limit, int offset)
    {
        await using var connection = await OpenAsync(databasePath);
        string metadataColumns = MetadataColumnsSql(await HasDirectorsAsync(connection));
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(DISTINCT m.Id) FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE {condition}";
        foreach (var parameter in parameters) count.Parameters.AddWithValue(parameter.Name, parameter.Value);
        long total = Convert.ToInt64(await count.ExecuteScalarAsync() ?? 0L);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.Id,COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(m.Title,''),COALESCE(f.FilePath,''),
                   COALESCE(s.UserRating,0),COALESCE(s.IsFavorite,0),COALESCE(m.ReleaseDate,''),COALESCE(m.ImportedAt,m.CreatedAt,''),EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id),
                   {metadataColumns}
              FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' LEFT JOIN UserMovieState s ON s.MovieId=m.Id
             WHERE {condition} ORDER BY {orderBy} LIMIT $limit OFFSET $offset
            """;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
        var items = new List<MediaCardDto>(); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) items.Add(Card(reader, bridgeUrl));
        return new(items, total, limit, offset);
    }
    private static MediaCardDto Card(SqliteDataReader reader,string bridgeUrl)=>new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDouble(4),reader.GetInt64(5)==1,reader.GetString(6),reader.GetString(7),reader.GetInt64(8)==1?$"{bridgeUrl}/api/images/{reader.GetInt64(0)}/primary?variant=thumbnail":null,MetadataStatusFromReader(reader,9));
    private static async Task<IReadOnlyList<SearchEntityDto>> ReadEntitiesAsync(SqliteConnection connection,string table,string relation,string key,string like,int limit){
        await using var command=connection.CreateCommand();command.CommandText=$"SELECT e.Id,e.Name,COUNT(r.MovieId) FROM {table} e LEFT JOIN {relation} r ON r.{key}=e.Id WHERE e.Name LIKE $like ESCAPE '\\' GROUP BY e.Id ORDER BY COUNT(r.MovieId) DESC,e.Name LIMIT $limit";command.Parameters.AddWithValue("$like",like);command.Parameters.AddWithValue("$limit",limit);
        var result=new List<SearchEntityDto>();await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2)));return result;
    }
    private static async Task<IReadOnlyList<NamedDto>> ReadNamesAsync(SqliteConnection connection,string table,string relation,string key,long movieId){
        await using var command=connection.CreateCommand();command.CommandText=$"SELECT e.Id,e.Name FROM {table} e JOIN {relation} r ON r.{key}=e.Id WHERE r.MovieId=$id ORDER BY e.Name";command.Parameters.AddWithValue("$id",movieId);
        var result=new List<NamedDto>();await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new(reader.GetInt64(0),reader.GetString(1)));return result;
    }
    private static async Task<IReadOnlyList<NamedDto>> ReadNamesIfExistsAsync(SqliteConnection connection,string table,string relation,string key,long movieId) =>
        await TableExistsAsync(connection, table) && await TableExistsAsync(connection, relation) ? await ReadNamesAsync(connection,table,relation,key,movieId) : [];
    private static async Task<long> ScalarAsync(SqliteConnection connection,string sql){await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync()??0);}
    private static async Task<long> CountRowsAsync(SqliteConnection connection,string sql){await using var command=connection.CreateCommand();command.CommandText=sql;await using var reader=await command.ExecuteReaderAsync();long count=0;while(await reader.ReadAsync())count++;return count;}
    private static async Task<SqliteConnection> OpenAsync(string path){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Cache=SqliteCacheMode.Shared}.ToString());await connection.OpenAsync();return connection;}
    private static async Task<bool> HasDirectorsAsync(SqliteConnection connection) => await TableExistsAsync(connection, "Directors") && await TableExistsAsync(connection, "MovieDirectors");
    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L) > 0;
    }
    private static async Task<bool> TableExistsAsync(string databasePath, string table)
    {
        await using var connection = await OpenAsync(databasePath);
        return await TableExistsAsync(connection, table);
    }
    private static string MissingCoverSql() => "NOT EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Poster','GeneratedCard','Thumbnail'))";
    private static string MissingFanartSql() => "NOT EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Fanart','BigPic'))";
    private static string MissingPreviewSql() => "NOT EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Preview','ExtraPic','Screenshot'))";
    private static string MissingDirectorSql(bool hasDirectors) => hasDirectors ? "NOT EXISTS(SELECT 1 FROM MovieDirectors md WHERE md.MovieId=m.Id)" : "1=1";
    private static string MetadataColumnsSql(bool hasDirectors) => $"""
        CASE WHEN COALESCE(m.IsScraped,0)=1 THEN 1 ELSE 0 END AS MetadataScraped,
        CASE WHEN {MissingCoverSql()} THEN 0 ELSE 1 END AS MetadataCover,
        CASE WHEN {MissingFanartSql()} THEN 0 ELSE 1 END AS MetadataFanart,
        CASE WHEN {MissingPreviewSql()} THEN 0 ELSE 1 END AS MetadataPreview,
        CASE WHEN trim(COALESCE(m.NfoPath,''))<>'' THEN 1 ELSE 0 END AS MetadataNfo,
        CASE WHEN EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id) THEN 1 ELSE 0 END AS MetadataActors,
        CASE WHEN {MissingDirectorSql(hasDirectors)} THEN 0 ELSE 1 END AS MetadataDirectors,
        CASE WHEN EXISTS(SELECT 1 FROM MovieSeries mse WHERE mse.MovieId=m.Id) THEN 1 ELSE 0 END AS MetadataSeries,
        CASE WHEN EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id) THEN 1 ELSE 0 END AS MetadataTags,
        CASE WHEN trim(COALESCE(m.Description,''))<>'' THEN 1 ELSE 0 END AS MetadataDescription
        """;
    private static MetadataStatusDto MetadataStatusFromReader(SqliteDataReader reader, int start)
    {
        var checks = new List<MetadataCheckDto> {
            new("cover","封面",reader.GetInt64(start + 1)==1),
            new("fanart","背景图",reader.GetInt64(start + 2)==1),
            new("preview","预览图",reader.GetInt64(start + 3)==1),
            new("nfo","NFO",reader.GetInt64(start + 4)==1),
            new("actors","演员",reader.GetInt64(start + 5)==1),
            new("directors","导演",reader.GetInt64(start + 6)==1),
            new("series","系列",reader.GetInt64(start + 7)==1),
            new("tags","标签",reader.GetInt64(start + 8)==1),
            new("description","简介",reader.GetInt64(start + 9)==1)
        };
        bool scraped = reader.GetInt64(start) == 1;
        return MetadataStatus(scraped, checks);
    }
    private static MetadataStatusDto MetadataStatus(bool scraped, IReadOnlyList<MetadataCheckDto> checks)
    {
        var required = new HashSet<string>(["cover","fanart","preview","nfo","actors","tags","description"]);
        var missing = checks.Where(item => !item.Complete && required.Contains(item.Key)).Select(item => item.Label).ToList();
        if (!scraped) return new("unscraped","✕","未刮削",missing,checks);
        if (missing.Count == 0) return new("complete","✓","已完整",missing,checks);
        return new("partial","⚠","待完善",missing,checks);
    }
    private static async Task<MetadataStatusDto> ReadMetadataStatusAsync(SqliteConnection connection, long movieId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MetadataColumnsSql(await HasDirectorsAsync(connection))} FROM Movies m WHERE m.Id=$id";
        command.Parameters.AddWithValue("$id", movieId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MetadataStatusFromReader(reader, 0) : MetadataStatus(false, []);
    }
    private static void AddMetadataStatusCondition(List<string> conditions, string metadataStatus, bool hasDirectors)
    {
        string complete = $"COALESCE(m.IsScraped,0)=1 AND NOT ({MissingCoverSql()}) AND NOT ({MissingFanartSql()}) AND NOT ({MissingPreviewSql()}) AND trim(COALESCE(m.NfoPath,''))<>'' AND EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id) AND EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id) AND trim(COALESCE(m.Description,''))<>''";
        switch (metadataStatus) {
            case "complete": conditions.Add(complete); break;
            case "unscraped": conditions.Add("COALESCE(m.IsScraped,0)=0"); break;
            case "missing-images": conditions.Add($"({MissingCoverSql()} OR {MissingFanartSql()} OR {MissingPreviewSql()})"); break;
            case "missing-nfo": conditions.Add("trim(COALESCE(m.NfoPath,''))=''"); break;
            case "missing-actors": conditions.Add("NOT EXISTS(SELECT 1 FROM MovieActors ma WHERE ma.MovieId=m.Id)"); break;
            case "missing-tags": conditions.Add("NOT EXISTS(SELECT 1 FROM MovieTags mt WHERE mt.MovieId=m.Id)"); break;
            case "missing-description": conditions.Add("trim(COALESCE(m.Description,''))=''"); break;
        }
    }
    private static async Task<(long Complete, long Pending, long Unscraped)> ReadMetadataCountsAsync(SqliteConnection connection)
    {
        bool hasDirectors = await HasDirectorsAsync(connection);
        var completeConditions = new List<string>(); AddMetadataStatusCondition(completeConditions, "complete", hasDirectors);
        long complete = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies m WHERE " + completeConditions[0]);
        long unscraped = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE COALESCE(IsScraped,0)=0");
        long total = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies");
        return (complete, Math.Max(0, total - complete), unscraped);
    }
    private static string EscapeLike(string value)=>value.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
    private static string? Text(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetString(index);
}
