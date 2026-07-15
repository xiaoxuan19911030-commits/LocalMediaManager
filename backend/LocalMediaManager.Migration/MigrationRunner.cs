using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class MigrationRunner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".ts", ".m2ts", ".flv", ".webm",
        ".vob", ".mpg", ".mpeg"
    };

    public static async Task<MigrationReport> RunAsync(string legacyBusinessDb, string legacyConfigDb,
        string imageRoot, string dataRoot, bool confirmSwitch)
    {
        DateTimeOffset started = DateTimeOffset.Now;
        string runId = started.ToString("yyyyMMdd_HHmmss");
        string dataDir = Path.Combine(dataRoot, "data");
        string backupRoot = Path.Combine(dataDir, "backups");
        string legacyBackup = Path.Combine(backupRoot, "legacy", runId);
        string databaseBackup = Path.Combine(backupRoot, "database", runId);
        string migrationBackup = Path.Combine(backupRoot, "migrations", runId);
        string reportDir = Path.Combine(dataDir, "reports", runId);
        Directory.CreateDirectory(legacyBackup);
        Directory.CreateDirectory(databaseBackup);
        Directory.CreateDirectory(migrationBackup);
        Directory.CreateDirectory(reportDir);

        string officialDb = Path.Combine(dataDir, "LocalMediaManager.db");
        string tempDb = Path.Combine(migrationBackup, $"LocalMediaManager.{runId}.tmp.db");
        string sourceBusinessHash = await HashAsync(legacyBusinessDb);
        string sourceConfigHash = await HashAsync(legacyConfigDb);
        File.Copy(legacyBusinessDb, Path.Combine(legacyBackup, Path.GetFileName(legacyBusinessDb)), overwrite: false);
        File.Copy(legacyConfigDb, Path.Combine(legacyBackup, Path.GetFileName(legacyConfigDb)), overwrite: false);

        var counts = new Dictionary<string, long>();
        var warnings = new List<MigrationWarning>();
        var samples = new List<SampleResult>();
        var maps = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase) {
            ["Movie"] = [], ["Actor"] = [], ["Library"] = [], ["TagStamp"] = []
        };

        await using var source = Open(legacyBusinessDb, readOnly: true);
        await source.OpenAsync();
        await using var target = Open(tempDb, readOnly: false);
        await target.OpenAsync();
        await ExecuteAsync(target, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        string schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "0001_InitialSchema.sql"));
        await using (var schemaTransaction = await target.BeginTransactionAsync()) {
            await ExecuteAsync(target, schema, schemaTransaction);
            string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema))).ToLowerInvariant();
            await ExecuteAsync(target,
                "INSERT INTO SchemaMigrations(Version,Name,AppliedAt,Checksum) VALUES(1,'0001_InitialSchema',$at,$sum)",
                schemaTransaction, ("$at", UtcNow()), ("$sum", checksum));
            await schemaTransaction.CommitAsync();
        }

        await using (var tx = await target.BeginTransactionAsync()) {
            await MigrateLibraries(source, target, tx, maps, counts, warnings);
            await MigrateMoviesAndFiles(source, target, tx, maps, counts, warnings);
            await MigrateActors(source, target, tx, maps, counts, warnings);
            await MigrateMovieActors(source, target, tx, maps, counts, warnings);
            await MigrateTags(source, target, tx, maps, counts, warnings);
            await MigrateGenresStudiosSeries(source, target, tx, maps, counts, warnings);
            await MigratePlayHistory(source, target, tx, maps, counts, warnings);
            await MigrateImages(target, tx, imageRoot, maps, counts, warnings);
            await StoreMetadata(target, tx, sourceBusinessHash, sourceConfigHash, started);
            foreach ((string entityType, Dictionary<long, long> values) in maps)
                foreach ((long legacyId, long newId) in values)
                    await ExecuteAsync(target,
                        "INSERT INTO LegacyIdMappings(EntityType,LegacySource,LegacyId,NewId) VALUES($type,'Jvedio5',$old,$new)",
                        tx, ("$type", entityType), ("$old", legacyId.ToString()), ("$new", newId));
            foreach (MigrationWarning warning in warnings)
                await ExecuteAsync(target,
                    "INSERT INTO MigrationWarnings(EntityType,LegacyId,WarningCode,Message,CreatedAt) VALUES($type,$id,$code,$message,$at)",
                    tx, ("$type", warning.EntityType), ("$id", warning.LegacyId), ("$code", warning.Code),
                    ("$message", warning.Message), ("$at", UtcNow()));
            await tx.CommitAsync();
        }

        IReadOnlyList<int> appliedMigrations = await DatabaseUpgradeRunner.ApplyPendingAsync(
            target, Path.Combine(AppContext.BaseDirectory, "migrations"));

        await ExecuteAsync(target, "PRAGMA wal_checkpoint(TRUNCATE)");
        string integrity = await ScalarText(target, "PRAGMA integrity_check") ?? "unknown";
        long foreignKeyErrors = await CountRows(target, "PRAGMA foreign_key_check");
        await BuildSamples(source, target, maps, samples);
        long legacyMovieCount = await ScalarLong(source, "SELECT COUNT(*) FROM metadata WHERE DataType=0");
        long newMovieCount = await ScalarLong(target, "SELECT COUNT(*) FROM Movies");
        long orphanMovieActors = await ScalarLong(target,
            "SELECT COUNT(*) FROM MovieActors ma LEFT JOIN Movies m ON m.Id=ma.MovieId LEFT JOIN Actors a ON a.Id=ma.ActorId WHERE m.Id IS NULL OR a.Id IS NULL");
        long duplicatePaths = await ScalarLong(target,
            "SELECT COUNT(*) FROM (SELECT NormalizedPath FROM MediaFiles GROUP BY NormalizedPath HAVING COUNT(*)>1)");
        long duplicateCodes = await ScalarLong(target,
            "SELECT COUNT(*) FROM (SELECT upper(trim(Code)) FROM Movies WHERE trim(ifnull(Code,''))<>'' GROUP BY upper(trim(Code)) HAVING COUNT(*)>1)");
        bool allowed = integrity.Equals("ok", StringComparison.OrdinalIgnoreCase) && foreignKeyErrors == 0 &&
            legacyMovieCount == newMovieCount && orphanMovieActors == 0 && samples.All(sample => sample.Passed);

        await target.CloseAsync();
        await source.CloseAsync();
        SqliteConnection.ClearAllPools();
        string targetHash = await HashAsync(tempDb);
        bool switched = false;
        if (allowed && confirmSwitch) {
            Directory.CreateDirectory(dataDir);
            if (File.Exists(officialDb)) File.Copy(officialDb, Path.Combine(databaseBackup, "LocalMediaManager.db"), overwrite: false);
            File.Move(tempDb, officialDb, overwrite: true);
            switched = true;
            targetHash = await HashAsync(officialDb);
        }

        var report = new MigrationReport(
            "0.4.1", started, DateTimeOffset.Now, legacyBusinessDb, legacyConfigDb, sourceBusinessHash,
            sourceConfigHash, switched ? officialDb : tempDb, targetHash, appliedMigrations.DefaultIfEmpty(1).Max(), confirmSwitch, allowed, switched,
            integrity, foreignKeyErrors, legacyMovieCount, newMovieCount, orphanMovieActors, duplicatePaths,
            duplicateCodes, counts, maps.ToDictionary(pair => pair.Key, pair => (long)pair.Value.Count),
            warnings, samples, legacyBackup, reportDir);
        await WriteReport(report);
        return report;
    }

    private static async Task MigrateLibraries(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT DBId,Name,Hide,ScanPath,CreateDate,UpdateDate FROM app_databases ORDER BY DBId";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long legacyId = reader.GetInt64(0);
            long newId = await InsertId(target,
                "INSERT INTO Libraries(Name,Description,IsEnabled,SortOrder,CreatedAt,UpdatedAt) VALUES($name,NULL,$enabled,$sort,$created,$updated); SELECT last_insert_rowid();",
                tx, ("$name", NullIfEmpty(ReadString(reader, 1)) ?? $"媒体库 {legacyId}"), ("$enabled", reader.GetInt64(2) == 0 ? 1 : 0),
                ("$sort", legacyId), ("$created", NormalizeDate(ReadString(reader, 4))), ("$updated", NormalizeDate(ReadString(reader, 5))));
            maps["Library"][legacyId] = newId;
            string scanPath = ReadString(reader, 3);
            if (!string.IsNullOrWhiteSpace(scanPath)) {
                foreach (string folder in SplitPaths(scanPath)) {
                    try {
                        await ExecuteAsync(target,
                            "INSERT OR IGNORE INTO LibraryFolders(LibraryId,FolderPath,NormalizedPath,IncludeSubfolders,IsEnabled,ScanMode,CreatedAt,UpdatedAt) VALUES($library,$path,$normalized,1,1,'normal',$at,$at)",
                            tx, ("$library", newId), ("$path", folder), ("$normalized", NormalizePath(folder)), ("$at", UtcNow()));
                    } catch (Exception ex) { warnings.Add(new("Library", legacyId.ToString(), "INVALID_LIBRARY_PATH", ex.Message)); }
                }
            }
        }
        counts["Libraries"] = maps["Library"].Count;
        counts["LibraryFolders"] = await ScalarLong(target, "SELECT COUNT(*) FROM LibraryFolders", tx);
    }

    private static async Task MigrateMoviesAndFiles(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        await using var command = source.CreateCommand();
        command.CommandText = """
            SELECT m.DataID,m.DBId,m.Title,m.Size,m.Path,m.Hash,m.ReleaseDate,m.ViewCount,m.Rating,
                   m.FavoriteCount,m.Grade,m.ViewDate,m.FirstScanDate,m.LastScanDate,m.CreateDate,m.UpdateDate,m.PathExist,
                   v.VID,v.Series,v.Studio,v.Plot,v.Duration,v.WebType,v.WebUrl,v.ExtraInfo
            FROM metadata m LEFT JOIN metadata_video v ON v.DataID=m.DataID
            WHERE m.DataType=0 ORDER BY m.DataID
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long legacyId = reader.GetInt64(0);
            string path = ReadString(reader, 4);
            string extension = Path.GetExtension(path);
            bool video = VideoExtensions.Contains(extension);
            string code = ReadString(reader, 17);
            long durationSeconds = Math.Max(0, ReadLong(reader, 21) * 60);
            long newId = await InsertId(target, """
                INSERT INTO Movies(Code,Title,OriginalTitle,SortTitle,ReleaseDate,DurationSeconds,Description,ProviderRating,IsScraped,ScrapeStatus,NfoPath,LegacySource,LegacyId,CreatedAt,UpdatedAt,ImportedAt)
                VALUES($code,$title,NULL,$title,$release,$duration,$plot,$rating,$scraped,$status,$nfo,'Jvedio5',$legacy,$created,$updated,$imported);
                SELECT last_insert_rowid();
                """, tx, ("$code", NullIfEmpty(code)), ("$title", NullIfEmpty(ReadString(reader, 2))),
                ("$release", NormalizeNullableDate(ReadString(reader, 6))), ("$duration", durationSeconds),
                ("$plot", NullIfEmpty(ReadString(reader, 20))), ("$rating", ReadDouble(reader, 8)),
                ("$scraped", string.IsNullOrWhiteSpace(code) ? 0 : 1), ("$status", string.IsNullOrWhiteSpace(code) ? "unknown" : "migrated"),
                ("$nfo", extension.Equals(".nfo", StringComparison.OrdinalIgnoreCase) ? path : null),
                ("$legacy", legacyId), ("$created", NormalizeDate(ReadString(reader, 14))),
                ("$updated", NormalizeDate(ReadString(reader, 15))), ("$imported", NormalizeNullableDate(ReadString(reader, 14))));
            maps["Movie"][legacyId] = newId;
            long? libraryId = maps["Library"].GetValueOrDefault(ReadLong(reader, 1));
            if (!string.IsNullOrWhiteSpace(path)) {
                string sourceType = path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) ? "STRM" :
                    Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? "URL" :
                    path.StartsWith("\\\\") ? "NAS" : Path.IsPathRooted(path) ? "Local" : "Unknown";
                string exists = File.Exists(path) ? "Present" : ReadLong(reader, 16) == 1 ? "Unverified" : "Missing";
                await ExecuteAsync(target, """
                    INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,FileHash,LastSeenAt,CreatedAt,UpdatedAt)
                    VALUES($movie,$library,$path,$normalized,$name,$ext,$size,$type,$source,1,$exists,$duration,$hash,$seen,$created,$updated)
                    """, tx, ("$movie", newId), ("$library", libraryId), ("$path", path), ("$normalized", NormalizePath(path)),
                    ("$name", Path.GetFileName(path)), ("$ext", extension.ToLowerInvariant()), ("$size", Math.Max(0, ReadLong(reader, 3))),
                    ("$type", video ? "Video" : extension.Equals(".nfo", StringComparison.OrdinalIgnoreCase) ? "NfoReference" : "LegacyReference"),
                    ("$source", sourceType), ("$exists", exists), ("$duration", durationSeconds),
                    ("$hash", NullIfEmpty(ReadString(reader, 5))), ("$seen", exists == "Present" ? UtcNow() : null),
                    ("$created", NormalizeDate(ReadString(reader, 14))), ("$updated", NormalizeDate(ReadString(reader, 15))));
                if (!video) warnings.Add(new("Movie", legacyId.ToString(), "NON_VIDEO_PATH", $"Legacy path kept as {extension} reference instead of a playable video."));
                if (exists == "Missing") warnings.Add(new("Movie", legacyId.ToString(), "MISSING_PATH", "Legacy media path is missing; record was preserved."));
            } else warnings.Add(new("Movie", legacyId.ToString(), "EMPTY_PATH", "Legacy movie has no path; movie was preserved without a media file."));
            await ExecuteAsync(target,
                "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPlayedAt,LastPositionSeconds,Notes,UpdatedAt) VALUES($movie,$favorite,$rating,$count,$played,0,NULL,$updated)",
                tx, ("$movie", newId), ("$favorite", ReadLong(reader, 9) > 0 ? 1 : 0),
                ("$rating", Math.Clamp(ReadDouble(reader, 10), 0, 5)), ("$count", Math.Max(0, ReadLong(reader, 7))),
                ("$played", NormalizeNullableDate(ReadString(reader, 11))), ("$updated", NormalizeDate(ReadString(reader, 15))));
            string provider = ReadString(reader, 22); string url = ReadString(reader, 23);
            if (!string.IsNullOrWhiteSpace(code)) await InsertExternal(target, tx, "Movie", newId, string.IsNullOrWhiteSpace(provider) ? "LegacyVID" : provider, code);
            if (!string.IsNullOrWhiteSpace(url)) await InsertExternal(target, tx, "Movie", newId, string.IsNullOrWhiteSpace(provider) ? "LegacyWeb" : provider + ":url", url);
        }
        counts["Movies"] = maps["Movie"].Count;
        counts["MediaFiles"] = await ScalarLong(target, "SELECT COUNT(*) FROM MediaFiles", tx);
        counts["UserMovieState"] = await ScalarLong(target, "SELECT COUNT(*) FROM UserMovieState", tx);
    }

    private static async Task MigrateActors(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT ActorID,ActorName,Gender,Birthday,Hobby,WebType,WebUrl,CreateDate,UpdateDate FROM actor_info ORDER BY ActorID";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long legacyId = reader.GetInt64(0); string name = ReadString(reader, 1).Trim();
            if (string.IsNullOrWhiteSpace(name)) { name = $"未命名演员 #{legacyId}"; warnings.Add(new("Actor", legacyId.ToString(), "EMPTY_ACTOR_NAME", "Empty actor name was preserved with a deterministic placeholder.")); }
            long newId = await InsertId(target, """
                INSERT INTO Actors(Name,NormalizedName,SortName,Alias,Gender,BirthDate,Description,ExternalId,LegacySource,LegacyId,CreatedAt,UpdatedAt)
                VALUES($name,$normalized,$name,NULL,$gender,$birth,$description,$external,'Jvedio5',$legacy,$created,$updated); SELECT last_insert_rowid();
                """, tx, ("$name", name), ("$normalized", NormalizeName(name)), ("$gender", ReadLong(reader, 2)),
                ("$birth", NormalizeNullableDate(ReadString(reader, 3))), ("$description", NullIfEmpty(ReadString(reader, 4))),
                ("$external", NullIfEmpty(ReadString(reader, 6))), ("$legacy", legacyId),
                ("$created", NormalizeDate(ReadString(reader, 7))), ("$updated", NormalizeDate(ReadString(reader, 8))));
            maps["Actor"][legacyId] = newId;
        }
        counts["Actors"] = maps["Actor"].Count;
    }

    private static async Task MigrateMovieActors(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        await using var command = source.CreateCommand(); command.CommandText = "SELECT ID,ActorID,DataID FROM metadata_to_actor ORDER BY ID";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long relationId = reader.GetInt64(0); long actor = ReadLong(reader, 1); long movie = ReadLong(reader, 2);
            if (!maps["Movie"].TryGetValue(movie, out long newMovie) || !maps["Actor"].TryGetValue(actor, out long newActor)) {
                warnings.Add(new("MovieActor", relationId.ToString(), "ORPHAN_ACTOR_RELATION", $"Legacy movie {movie} or actor {actor} does not exist.")); continue;
            }
            await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES($movie,$actor,'',0)", tx,
                ("$movie", newMovie), ("$actor", newActor));
        }
        counts["MovieActors"] = await ScalarLong(target, "SELECT COUNT(*) FROM MovieActors", tx);
    }

    private static async Task MigrateTags(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        var tags = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        async Task<long?> EnsureTag(string name, string sourceName, string? color = null) {
            string clean = name.Trim(); if (clean.Length == 0) return null; string normalized = NormalizeName(clean);
            if (tags.TryGetValue(normalized, out long existing)) return existing;
            long id = await InsertId(target,
                "INSERT INTO Tags(Name,NormalizedName,Description,Color,Source,CreatedAt,UpdatedAt) VALUES($name,$normalized,NULL,$color,$source,$at,$at); SELECT last_insert_rowid();",
                tx, ("$name", clean), ("$normalized", normalized), ("$color", color), ("$source", sourceName), ("$at", UtcNow()));
            tags[normalized] = id; return id;
        }
        await using (var command = source.CreateCommand()) {
            command.CommandText = "SELECT DataID,LabelName FROM metadata_to_label ORDER BY id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                long legacyMovie = ReadLong(reader, 0); string name = ReadString(reader, 1);
                if (!maps["Movie"].TryGetValue(legacyMovie, out long movie)) { warnings.Add(new("Tag", legacyMovie.ToString(), "ORPHAN_LABEL_RELATION", name)); continue; }
                long? tag = await EnsureTag(name, "LegacyLabel"); if (tag is null) { warnings.Add(new("Tag", legacyMovie.ToString(), "EMPTY_TAG", "Empty label was not inserted.")); continue; }
                await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) VALUES($movie,$tag,$at)", tx,
                    ("$movie", movie), ("$tag", tag.Value), ("$at", UtcNow()));
            }
        }
        await using (var command = source.CreateCommand()) {
            command.CommandText = "SELECT TagID,TagName,Foreground,Background FROM common_tagstamp ORDER BY TagID";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                long legacy = ReadLong(reader, 0); long? tag = await EnsureTag(ReadString(reader, 1), "LegacyStamp", ReadString(reader, 3));
                if (tag is not null) maps["TagStamp"][legacy] = tag.Value;
            }
        }
        await using (var command = source.CreateCommand()) {
            command.CommandText = "SELECT id,DataID,TagID FROM metadata_to_tagstamp ORDER BY id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                long relation = ReadLong(reader, 0); long legacyMovie = ReadLong(reader, 1); long legacyTag = ReadLong(reader, 2);
                if (!maps["Movie"].TryGetValue(legacyMovie, out long movie) || !maps["TagStamp"].TryGetValue(legacyTag, out long tag)) {
                    warnings.Add(new("MovieTag", relation.ToString(), "ORPHAN_STAMP_RELATION", $"Movie {legacyMovie}, tag {legacyTag}")); continue;
                }
                await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) VALUES($movie,$tag,$at)", tx,
                    ("$movie", movie), ("$tag", tag), ("$at", UtcNow()));
            }
        }
        counts["Tags"] = await ScalarLong(target, "SELECT COUNT(*) FROM Tags", tx);
        counts["MovieTags"] = await ScalarLong(target, "SELECT COUNT(*) FROM MovieTags", tx);
    }

    private static async Task MigrateGenresStudiosSeries(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        var genres = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var studios = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var series = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var command = source.CreateCommand();
        command.CommandText = "SELECT m.DataID,m.Genre,v.Studio,v.Publisher,v.Series FROM metadata m LEFT JOIN metadata_video v ON v.DataID=m.DataID WHERE m.DataType=0";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long legacyMovie = ReadLong(reader, 0); if (!maps["Movie"].TryGetValue(legacyMovie, out long movie)) continue;
            foreach (string name in SplitNames(ReadString(reader, 1))) {
                long id = await EnsureNamed(target, tx, "Genres", genres, name);
                await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieGenres(MovieId,GenreId) VALUES($movie,$id)", tx, ("$movie", movie), ("$id", id));
            }
            foreach ((string name, string relation) in new[] { (ReadString(reader, 2), "Studio"), (ReadString(reader, 3), "Publisher") }) {
                if (string.IsNullOrWhiteSpace(name)) continue; long id = await EnsureNamed(target, tx, "Studios", studios, name);
                await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieStudios(MovieId,StudioId,RelationType) VALUES($movie,$id,$type)", tx,
                    ("$movie", movie), ("$id", id), ("$type", relation));
            }
            foreach (string name in SplitNames(ReadString(reader, 4))) {
                long id = await EnsureNamed(target, tx, "Series", series, name);
                await ExecuteAsync(target, "INSERT OR IGNORE INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES($movie,$id,0)", tx, ("$movie", movie), ("$id", id));
            }
        }
        counts["Genres"] = genres.Count; counts["Studios"] = studios.Count; counts["Series"] = series.Count;
        counts["MovieGenres"] = await ScalarLong(target, "SELECT COUNT(*) FROM MovieGenres", tx);
        counts["MovieStudios"] = await ScalarLong(target, "SELECT COUNT(*) FROM MovieStudios", tx);
        counts["MovieSeries"] = await ScalarLong(target, "SELECT COUNT(*) FROM MovieSeries", tx);
    }

    private static async Task MigratePlayHistory(SqliteConnection source, SqliteConnection target,
        System.Data.Common.DbTransaction tx, Dictionary<string, Dictionary<long, long>> maps,
        Dictionary<string, long> counts, List<MigrationWarning> warnings)
    {
        await using var command = source.CreateCommand(); command.CommandText = "SELECT HistoryID,DataID,PlayDate FROM common_play_history ORDER BY HistoryID";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long legacy = ReadLong(reader, 0); long legacyMovie = ReadLong(reader, 1);
            long? movie = maps["Movie"].GetValueOrDefault(legacyMovie); long? file = null;
            if (movie is not null && movie > 0) file = await ScalarNullableLong(target, "SELECT Id FROM MediaFiles WHERE MovieId=$movie AND IsPrimary=1 LIMIT 1", tx, ("$movie", movie));
            if (movie is null or 0) warnings.Add(new("PlayHistory", legacy.ToString(), "ORPHAN_PLAY_HISTORY", $"Legacy movie {legacyMovie} is missing; history preserved without movie."));
            await ExecuteAsync(target,
                "INSERT INTO PlayHistory(MovieId,MediaFileId,StartedAt,EndedAt,PositionSeconds,DurationSeconds,Completed,PlayerName,LegacyId) VALUES($movie,$file,$started,NULL,0,0,0,NULL,$legacy)",
                tx, ("$movie", movie is 0 ? null : movie), ("$file", file), ("$started", NormalizeDate(ReadString(reader, 2))), ("$legacy", legacy));
        }
        counts["PlayHistory"] = await ScalarLong(target, "SELECT COUNT(*) FROM PlayHistory", tx);
    }

    private static async Task MigrateImages(SqliteConnection target, System.Data.Common.DbTransaction tx,
        string imageRoot, Dictionary<string, Dictionary<long, long>> maps, Dictionary<string, long> counts,
        List<MigrationWarning> warnings)
    {
        if (!Directory.Exists(imageRoot)) { warnings.Add(new("Image", null, "IMAGE_ROOT_MISSING", imageRoot)); counts["Images"] = 0; return; }
        var codeMap = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
        await using (var command = target.CreateCommand()) {
            command.Transaction = (SqliteTransaction)tx; command.CommandText = "SELECT Id,Code FROM Movies WHERE trim(ifnull(Code,''))<>''";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                string code = reader.GetString(1); if (!codeMap.TryGetValue(code, out List<long>? list)) codeMap[code] = list = [];
                list.Add(reader.GetInt64(0));
            }
        }
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["CardCovers"]="GeneratedCard", ["SmallPic"]="Poster", ["BigPic"]="Fanart",
            ["ExtraPic"]="Preview", ["ScreenShot"]="Screenshot"
        };
        foreach ((string folder, string type) in folders) {
            string path = Path.Combine(imageRoot, folder); if (!Directory.Exists(path)) continue;
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)) {
                if (!codeMap.TryGetValue(Path.GetFileNameWithoutExtension(file), out List<long>? movieIds)) continue;
                var info = new FileInfo(file);
                foreach (long movie in movieIds)
                    await ExecuteAsync(target,
                        "INSERT OR IGNORE INTO Images(MovieId,ActorId,ImageType,FilePath,SourceUrl,FileSize,IsPrimary,SourceProvider,CreatedAt,UpdatedAt) VALUES($movie,NULL,$type,$path,NULL,$size,$primary,'LegacyFile',$at,$at)",
                        tx, ("$movie", movie), ("$type", type), ("$path", file), ("$size", info.Length),
                        ("$primary", type is "GeneratedCard" or "Poster" ? 1 : 0), ("$at", UtcNow()));
            }
        }
        counts["Images"] = await ScalarLong(target, "SELECT COUNT(*) FROM Images", tx);
    }

    private static async Task StoreMetadata(SqliteConnection target, System.Data.Common.DbTransaction tx,
        string businessHash, string configHash, DateTimeOffset started)
    {
        foreach ((string key, string value) in new Dictionary<string, string> {
            ["CreatedByVersion"]="0.4.1", ["SchemaVersion"]="1", ["LastMigrationAt"]=UtcNow(),
            ["LegacyBusinessSha256"]=businessHash, ["LegacyConfigSha256"]=configHash,
            ["MigrationStartedAt"]=started.ToString("O"), ["DataSeparationNotice"]="LMM data is independent from the legacy WPF databases."
        }) await ExecuteAsync(target, "INSERT INTO DatabaseMetadata(Key,Value) VALUES($key,$value)", tx, ("$key", key), ("$value", value));
    }

    private static async Task BuildSamples(SqliteConnection source, SqliteConnection target,
        Dictionary<string, Dictionary<long, long>> maps, List<SampleResult> samples)
    {
        samples.Add(await Sample(source, target, "Movies", 50,
            "SELECT DataID FROM metadata WHERE DataType=0 ORDER BY DataID LIMIT 50",
            maps["Movie"], "SELECT COUNT(*) FROM Movies WHERE Id=$id"));
        samples.Add(await Sample(source, target, "Actors", 20,
            "SELECT ActorID FROM actor_info ORDER BY ActorID LIMIT 20", maps["Actor"], "SELECT COUNT(*) FROM Actors WHERE Id=$id"));
        samples.Add(new("Tags", 20, (int)Math.Min(20, await ScalarLong(target, "SELECT COUNT(*) FROM Tags")), await ScalarLong(target, "SELECT COUNT(*) FROM Tags") > 0));
        samples.Add(new("UserMovieState", 20, (int)Math.Min(20, await ScalarLong(target, "SELECT COUNT(*) FROM UserMovieState")), await ScalarLong(target, "SELECT COUNT(*) FROM UserMovieState") >= 20));
        samples.Add(new("PlayHistory", 20, (int)Math.Min(20, await ScalarLong(target, "SELECT COUNT(*) FROM PlayHistory")), await ScalarLong(target, "SELECT COUNT(*) FROM PlayHistory") >= 20));
        long images = await ScalarLong(target, "SELECT COUNT(*) FROM Images");
        samples.Add(new("Images", 20, (int)Math.Min(20, images), images >= 20));
    }

    private static async Task<SampleResult> Sample(SqliteConnection source, SqliteConnection target, string name,
        int requested, string sourceSql, Dictionary<long, long> map, string targetSql)
    {
        var legacyIds = new List<long>(); await using var command = source.CreateCommand(); command.CommandText = sourceSql;
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) legacyIds.Add(reader.GetInt64(0));
        int matched = 0; foreach (long legacy in legacyIds)
            if (map.TryGetValue(legacy, out long id) && await ScalarLong(target, targetSql, null, ("$id", id)) == 1) matched++;
        return new(name, requested, matched, matched == legacyIds.Count && legacyIds.Count == requested);
    }

    private static async Task WriteReport(MigrationReport report)
    {
        Directory.CreateDirectory(report.ReportDirectory);
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(report.ReportDirectory, "migration-report.json"), JsonSerializer.Serialize(report, json));
        var md = new StringBuilder("# Local Media Manager migration report\n\n");
        md.AppendLine($"- Started: {report.StartedAt:O}").AppendLine($"- Completed: {report.CompletedAt:O}")
          .AppendLine($"- Legacy business database: `{report.LegacyBusinessDatabase}`")
          .AppendLine($"- New database: `{report.NewDatabase}`").AppendLine($"- Schema: v{report.SchemaVersion}")
          .AppendLine($"- Integrity check: `{report.IntegrityCheck}`").AppendLine($"- Foreign key errors: {report.ForeignKeyErrors}")
          .AppendLine($"- Legacy movies: {report.LegacyMovieCount}").AppendLine($"- New movies: {report.NewMovieCount}")
          .AppendLine($"- Migration allowed switch: {report.AllowedToSwitch}").AppendLine($"- Switched: {report.Switched}").AppendLine();
        md.AppendLine("## Counts\n"); foreach ((string key,long value) in report.Counts) md.AppendLine($"- {key}: {value}");
        md.AppendLine("\n## Samples\n"); foreach (SampleResult sample in report.Samples) md.AppendLine($"- {sample.Name}: {sample.Matched}/{sample.Requested}, passed: {sample.Passed}");
        md.AppendLine($"\n## Warnings\n\nTotal: {report.Warnings.Count}");
        foreach (var group in report.Warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count())) md.AppendLine($"- {group.Key}: {group.Count()}");
        await File.WriteAllTextAsync(Path.Combine(report.ReportDirectory, "migration-report.md"), md.ToString());
    }

    private static async Task<long> EnsureNamed(SqliteConnection target, System.Data.Common.DbTransaction tx,
        string table, Dictionary<string,long> cache, string name)
    {
        string clean = name.Trim(); string normalized = NormalizeName(clean); if (cache.TryGetValue(normalized, out long id)) return id;
        string extra = table switch { "Studios" => ",Description", "Series" => ",Description,ExternalId", _ => "" };
        string values = table switch { "Studios" => ",NULL", "Series" => ",NULL,NULL", _ => "" };
        id = await InsertId(target, $"INSERT INTO {table}(Name,NormalizedName{extra}) VALUES($name,$normalized{values}); SELECT last_insert_rowid();", tx,
            ("$name", clean), ("$normalized", normalized)); cache[normalized] = id; return id;
    }

    private static async Task InsertExternal(SqliteConnection target, System.Data.Common.DbTransaction tx,
        string entity, long id, string provider, string external) => await ExecuteAsync(target,
        "INSERT OR IGNORE INTO ExternalIds(EntityType,EntityId,Provider,ExternalId) VALUES($entity,$id,$provider,$external)", tx,
        ("$entity", entity), ("$id", id), ("$provider", provider), ("$external", external));

    private static SqliteConnection Open(string path, bool readOnly) => new(new SqliteConnectionStringBuilder {
        DataSource=path, Mode=readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Cache=SqliteCacheMode.Shared
    }.ToString());
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, System.Data.Common.DbTransaction? tx = null,
        params (string Name, object? Value)[] parameters) { await using var c=connection.CreateCommand(); c.Transaction=(SqliteTransaction?)tx; c.CommandText=sql; Add(c,parameters); await c.ExecuteNonQueryAsync(); }
    private static async Task<long> InsertId(SqliteConnection connection, string sql, System.Data.Common.DbTransaction tx,
        params (string Name, object? Value)[] parameters) { await using var c=connection.CreateCommand(); c.Transaction=(SqliteTransaction)tx; c.CommandText=sql; Add(c,parameters); return Convert.ToInt64(await c.ExecuteScalarAsync()); }
    private static void Add(SqliteCommand command, params (string Name, object? Value)[] parameters) { foreach(var p in parameters) command.Parameters.AddWithValue(p.Name,p.Value??DBNull.Value); }
    private static async Task<long> ScalarLong(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx=null,params (string Name,object? Value)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;Add(x,p);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private static async Task<long?> ScalarNullableLong(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx=null,params (string Name,object? Value)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;Add(x,p);object? value=await x.ExecuteScalarAsync();return value is null or DBNull?null:Convert.ToInt64(value);}
    private static async Task<string?> ScalarText(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return(await x.ExecuteScalarAsync())?.ToString();}
    private static async Task<long> CountRows(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;await using var r=await x.ExecuteReaderAsync();long n=0;while(await r.ReadAsync())n++;return n;}
    private static string ReadString(System.Data.Common.DbDataReader reader,int i)=>reader.IsDBNull(i)?"":reader.GetValue(i)?.ToString()??"";
    private static long ReadLong(System.Data.Common.DbDataReader reader,int i)=>reader.IsDBNull(i)?0:Convert.ToInt64(reader.GetValue(i));
    private static double ReadDouble(System.Data.Common.DbDataReader reader,int i)=>reader.IsDBNull(i)?0:Convert.ToDouble(reader.GetValue(i));
    private static string UtcNow()=>DateTimeOffset.UtcNow.ToString("O");
    private static string NormalizeDate(string value)=>NormalizeNullableDate(value)??UtcNow();
    private static string? NormalizeNullableDate(string value)
    {
        if (!DateTime.TryParse(value, out DateTime parsed) || parsed.Year < 1970 || parsed.Year > 9998)
            return null;
        try {
            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Local)).ToUniversalTime().ToString("O");
        } catch (ArgumentOutOfRangeException) {
            return null;
        }
    }
    private static string NormalizePath(string path){if(string.IsNullOrWhiteSpace(path))return "";string value=path.Trim().Replace('/','\\');try{return Path.IsPathRooted(value)?Path.GetFullPath(value):value;}catch{return value;}}
    private static string NormalizeName(string value)=>string.Join(' ',value.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string? NullIfEmpty(string value)=>string.IsNullOrWhiteSpace(value)?null:value;
    private static IEnumerable<string> SplitNames(string value)=>value.Split([',',';','|','、'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<string> SplitPaths(string value)=>value.Split(['|',';','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase);
    private static async Task<string> HashAsync(string path){await using var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();}
}

internal sealed record MigrationWarning(string EntityType,string? LegacyId,string Code,string Message);
internal sealed record SampleResult(string Name,int Requested,int Matched,bool Passed);
internal sealed record MigrationReport(string ToolVersion,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,
    string LegacyBusinessDatabase,string LegacyConfigDatabase,string LegacyBusinessSha256,string LegacyConfigSha256,
    string NewDatabase,string NewDatabaseSha256,int SchemaVersion,bool SwitchConfirmed,bool AllowedToSwitch,bool Switched,
    string IntegrityCheck,long ForeignKeyErrors,long LegacyMovieCount,long NewMovieCount,long OrphanMovieActors,
    long DuplicatePaths,long DuplicateCodes,IReadOnlyDictionary<string,long> Counts,IReadOnlyDictionary<string,long> IdMappings,
    IReadOnlyList<MigrationWarning> Warnings,IReadOnlyList<SampleResult> Samples,string LegacyBackupDirectory,string ReportDirectory);
