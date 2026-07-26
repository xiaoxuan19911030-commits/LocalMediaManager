using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetadataSyncLaunchResult(long TaskId, string Status, string Message);
public sealed record BatchTaskMutationResult(int Count, string Message);
public sealed record SyncLibraryCommand(long? LibraryId = null);
public sealed record FilteredMovieSyncCommand(
    string Query = "", long? ActorId = null, long? TagId = null, long? DirectorId = null, long? MovieTagId = null,
    long? CustomTagId = null, long? GenreId = null, long? SeriesId = null, long? StudioId = null, bool? Favorite = null,
    bool? Watched = null, double? RatingMin = null, string? RatingFilter = null, string? Metadata = null,
    string? FileStatus = null, string? MetadataStatus = null, long? LibraryId = null, string? Sort = null,
    IReadOnlyList<string>? TargetFields = null);
public sealed record FilteredMovieSyncPreview(long Count);
public sealed record FilteredMovieSyncResult(long Count, string Message);
public sealed record SyncMovie(long Id, string Code, string? Title, string? Description, string? ReleaseDate,
    int DurationSeconds, string? PrimaryFile, string? NfoPath);
public sealed record SavedImage(string Type, string Path, string SourceUrl, long Size, bool Created,
    int Width = 0, int Height = 0, string? ContentType = null, string? FileHash = null,
    string Ownership = "Provider", bool IsDerived = false, string? SourceProvider = null);
public sealed record ImageDownloadFailure(
    string Type,
    string Provider,
    string Url,
    bool RefererApplied,
    Exception Error);
public sealed record ImageDownloadBatchResult(IReadOnlyList<SavedImage> Images, IReadOnlyList<ImageDownloadFailure> Failures);
public sealed record PreparedFiles(IReadOnlyList<SavedImage> Images, string? NfoPath, IReadOnlyList<string> CreatedPaths);
public sealed record ImageDownloadOptions(string? Cookie = null, string? Referer = null, string? RestrictedHost = null);

public sealed class TaskLogService(string databasePath)
{
    public async Task WriteAsync(long taskId, string level, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO TaskLogs(TaskId,Level,Message,CreatedAt) VALUES($task,$level,$message,$at)";
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private async Task<SqliteConnection> OpenAsync() {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        await connection.OpenAsync(); return connection;
    }
}

public sealed class ImageDownloadService(IHttpClientFactory clients)
{
    private const long MaximumDownloadBytes = 64L * 1024 * 1024;

    public async Task<IReadOnlyList<SavedImage>> DownloadAsync(MediaStoragePathResolver pathResolver, MediaStorageMovie movie,
        IReadOnlyList<MetadataImage> images, int timeoutSeconds, bool overwriteExisting, CancellationToken cancellationToken,
        ImageDownloadOptions? options = null)
    {
        ImageDownloadBatchResult result = await DownloadCoreAsync(pathResolver, movie, images, timeoutSeconds,
            overwriteExisting, cancellationToken, options, continueOnError: false);
        return result.Images;
    }

    public Task<ImageDownloadBatchResult> DownloadBatchAsync(MediaStoragePathResolver pathResolver, MediaStorageMovie movie,
        IReadOnlyList<MetadataImage> images, int timeoutSeconds, bool overwriteExisting, CancellationToken cancellationToken,
        ImageDownloadOptions? options = null) =>
        DownloadCoreAsync(pathResolver, movie, images, timeoutSeconds, overwriteExisting, cancellationToken, options,
            continueOnError: true);

    private async Task<ImageDownloadBatchResult> DownloadCoreAsync(MediaStoragePathResolver pathResolver, MediaStorageMovie movie,
        IReadOnlyList<MetadataImage> images, int timeoutSeconds, bool overwriteExisting, CancellationToken cancellationToken,
        ImageDownloadOptions? options, bool continueOnError)
    {
        var saved = new List<SavedImage>();
        var failures = new List<ImageDownloadFailure>();
        if (images.Count == 0) return new(saved, failures);
        using HttpClient client = clients.CreateClient("MetadataImages");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.7.6"));
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        int previewIndex = 0;
        string temporaryRoot = await pathResolver.TemporaryRootAsync(cancellationToken);
        try {
            foreach (MetadataImage image in images) {
                cancellationToken.ThrowIfCancellationRequested();
                string normalizedType = MediaStoragePathResolver.NormalizeResourceType(image.Type);
                try {
                    if (normalizedType == "Preview") previewIndex++;
                    int? index = normalizedType == "Preview" ? previewIndex : null;
                    MediaStorageResourcePath targetPath = await pathResolver.ResolveForMovieAsync(movie, normalizedType, ".jpg", index, null, cancellationToken);
                    string targetDirectory = Path.GetDirectoryName(targetPath.FullPath)!;
                    string baseName = Path.GetFileNameWithoutExtension(targetPath.FullPath);
                    string? existing = FindExisting(targetDirectory, baseName);
                    if (existing is not null && !overwriteExisting) {
                        ImageValidationResult current = await ImageFileValidator.ValidateAsync(existing, null, cancellationToken);
                        if (current.Valid) {
                            saved.Add(new(normalizedType, existing, image.Url, current.FileSize, false, current.Width,
                                current.Height, current.ContentType, current.Sha256, "Legacy", false, image.Provider));
                            continue;
                        }
                        throw new InvalidDataException($"已有图片损坏但受到保护，未覆盖：{existing}");
                    }

                    string temporary = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N") + ".part");
                    using var request = new HttpRequestMessage(HttpMethod.Get, image.Url);
                    ApplyOptions(request, options);
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    string? declaredType = response.Content.Headers.ContentType?.MediaType;
                    if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                        throw new InvalidDataException("远程图片超过 64 MB 安全限制。");
                    await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (FileStream destination = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await CopyWithLimitAsync(source, destination, cancellationToken);
                    ImageValidationResult validation = await ImageFileValidator.ValidateAsync(temporary, declaredType, cancellationToken);
                    if (!validation.Valid) throw new InvalidDataException(validation.Error ?? "图片校验失败。");
                    string target = (await pathResolver.ResolveForMovieAsync(movie, normalizedType, Extension(validation.ContentType), index, null, cancellationToken)).FullPath;
                    pathResolver.EnsureDirectoryForWrite(target);
                    if (existing is not null && !string.Equals(existing, target, StringComparison.OrdinalIgnoreCase) && File.Exists(existing))
                        File.Delete(existing);
                    if (File.Exists(target)) {
                        if (overwriteExisting) {
                            File.Move(temporary, target, true);
                            saved.Add(new(normalizedType, target, image.Url, validation.FileSize, true, validation.Width,
                                validation.Height, validation.ContentType, validation.Sha256, "Provider", false, image.Provider));
                            continue;
                        }
                        File.Delete(temporary);
                        ImageValidationResult concurrent = await ImageFileValidator.ValidateAsync(target, null, cancellationToken);
                        if (!concurrent.Valid) throw new InvalidDataException($"目标图片冲突且无效，未覆盖：{target}");
                        saved.Add(new(normalizedType, target, image.Url, concurrent.FileSize, false, concurrent.Width,
                            concurrent.Height, concurrent.ContentType, concurrent.Sha256, "Legacy", false, image.Provider));
                        continue;
                    }
                    File.Move(temporary, target, false);
                    saved.Add(new(normalizedType, target, image.Url, validation.FileSize, true, validation.Width,
                        validation.Height, validation.ContentType, validation.Sha256, "Provider", false, image.Provider));
                } catch (Exception error) when (continueOnError && error is not OperationCanceledException) {
                    failures.Add(new(normalizedType, image.Provider ?? "Unknown", image.Url,
                        options is not null && AppliesTo(Uri.TryCreate(image.Url, UriKind.Absolute, out Uri? failedUri) ? failedUri : null, options.RestrictedHost)
                            && !string.IsNullOrWhiteSpace(options.Referer), error));
                }
            }
        } finally {
            try { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); } catch { }
        }
        return new(saved, failures);
    }

    private static void ApplyOptions(HttpRequestMessage request, ImageDownloadOptions? options) {
        if (options is null || !AppliesTo(request.RequestUri, options.RestrictedHost)) return;
        if (!string.IsNullOrWhiteSpace(options.Cookie)) request.Headers.TryAddWithoutValidation("Cookie", options.Cookie);
        if (!string.IsNullOrWhiteSpace(options.Referer) && Uri.TryCreate(options.Referer, UriKind.Absolute, out Uri? referer)) request.Headers.Referrer = referer;
    }
    private static bool AppliesTo(Uri? uri, string? restrictedHost) {
        if (string.IsNullOrWhiteSpace(restrictedHost)) return true;
        if (uri is null) return false;
        string host = restrictedHost.Trim().TrimEnd('.');
        string baseHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals(baseHost, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith('.' + baseHost, StringComparison.OrdinalIgnoreCase);
    }
    private static string? FindExisting(string directory, string baseName) {
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }) {
            string candidate = Path.Combine(directory, baseName + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    private static string Extension(string? contentType) => contentType switch {
        "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".jpg"
    };
    private static async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken token) {
        byte[] buffer = new byte[81920]; long total = 0;
        while (true) {
            int read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            total += read;
            if (total > MaximumDownloadBytes) throw new InvalidDataException("远程图片超过 64 MB 安全限制。");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    internal static string FormatFailureEvidence(ImageDownloadFailure failure)
    {
        string host = Uri.TryCreate(failure.Url, UriKind.Absolute, out Uri? uri) ? uri.Host : "invalid-url";
        string status = failure.Error is HttpRequestException http && http.StatusCode is not null
            ? ((int)http.StatusCode.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "Unavailable";
        return $"[Image Resource] Type={failure.Type}; Provider={failure.Provider}; Host={host}; HTTP={status}; "
            + $"ContentType=Unavailable; ContentLength=Unavailable; Referer={(failure.RefererApplied ? "Applied" : "NotApplied")}; "
            + "UserAgent=LocalMediaManager/0.7.6; Retry=0; Result=Failed; "
            + $"Error={failure.Error.GetType().Name}: {failure.Error.Message}";
    }
}

public sealed class MetadataWriteService(string databasePath)
{
    public async Task<string> ApplyAsync(long taskId, SyncMovie movie, ProviderMetadata metadata,
        PreparedFiles files, bool overwrite, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        bool normalizeEquivalentCode = !string.IsNullOrWhiteSpace(metadata.Code)
            && JavBusCode.Normalize(movie.Code).Equals(JavBusCode.Normalize(metadata.Code), StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<string> addedFields = await DetermineAddedFieldsAsync(connection, transaction, movie, metadata, files, overwrite);
        string before = JsonSerializer.Serialize(new { movie.Title, movie.Description, movie.ReleaseDate, movie.DurationSeconds, movie.NfoPath });
        long snapshotId = await InsertIdAsync(connection, transaction, """
            INSERT INTO MetadataSyncSnapshots(TaskId,MovieId,Provider,BeforeJson,CreatedAt)
            VALUES($task,$movie,$provider,$before,$at); SELECT last_insert_rowid();
            """, ("$task", taskId), ("$movie", movie.Id), ("$provider", metadata.Provider), ("$before", before), ("$at", Now()));

        await ExecuteAsync(connection, transaction, """
            UPDATE Movies SET
              Code=CASE WHEN $overwrite=1 THEN COALESCE($code,Code) WHEN trim(ifnull(Code,''))='' OR $normalizeCode=1 THEN $code ELSE Code END,
              Title=CASE WHEN $overwrite=1 THEN COALESCE($title,Title) WHEN trim(ifnull(Title,''))='' OR Title=Code THEN COALESCE($title,Title) ELSE Title END,
              OriginalTitle=CASE WHEN $overwrite=1 THEN COALESCE($original,OriginalTitle) WHEN trim(ifnull(OriginalTitle,''))='' THEN $original ELSE OriginalTitle END,
              SortTitle=CASE WHEN $overwrite=1 THEN COALESCE($title,SortTitle) WHEN trim(ifnull(SortTitle,''))='' OR SortTitle=Code THEN COALESCE($title,SortTitle) ELSE SortTitle END,
              Description=CASE WHEN $overwrite=1 THEN COALESCE($description,Description) WHEN trim(ifnull(Description,''))='' THEN $description ELSE Description END,
              ReleaseDate=CASE WHEN $overwrite=1 THEN COALESCE($release,ReleaseDate) WHEN trim(ifnull(ReleaseDate,''))='' THEN $release ELSE ReleaseDate END,
              DurationSeconds=CASE WHEN $overwrite=1 THEN COALESCE($duration,DurationSeconds) WHEN DurationSeconds=0 THEN COALESCE($duration,0) ELSE DurationSeconds END,
              ProviderRating=CASE WHEN $overwrite=1 THEN COALESCE($rating,ProviderRating) WHEN ProviderRating IS NULL OR ProviderRating=0 THEN $rating ELSE ProviderRating END,
              NfoPath=CASE WHEN $overwrite=1 THEN COALESCE($nfo,NfoPath) WHEN trim(ifnull(NfoPath,''))='' THEN $nfo ELSE NfoPath END,
              IsScraped=1,ScrapeStatus='complete',UpdatedAt=$at
            WHERE Id=$movie
            """, ("$code", metadata.Code), ("$title", metadata.Title), ("$description", metadata.Description),
            ("$original", metadata.OriginalTitle), ("$release", metadata.ReleaseDate), ("$duration", metadata.DurationSeconds),
            ("$rating", metadata.Rating), ("$nfo", files.NfoPath), ("$overwrite", overwrite ? 1 : 0),
            ("$normalizeCode", normalizeEquivalentCode ? 1 : 0),
            ("$at", Now()), ("$movie", movie.Id));
        await ExecuteAsync(connection, transaction,
            "INSERT OR IGNORE INTO ExternalIds(EntityType,EntityId,Provider,ExternalId) VALUES('Movie',$movie,$provider,$external)",
            ("$movie", movie.Id), ("$provider", metadata.Provider), ("$external", metadata.ExternalId));

        if (overwrite) {
            await ExecuteAsync(connection, transaction, "DELETE FROM MovieGenres WHERE MovieId=$movie", ("$movie", movie.Id));
            await ExecuteAsync(connection, transaction, "DELETE FROM MovieSeries WHERE MovieId=$movie", ("$movie", movie.Id));
            await ExecuteAsync(connection, transaction, "DELETE FROM MovieStudios WHERE MovieId=$movie", ("$movie", movie.Id));
            await ExecuteAsync(connection, transaction, "DELETE FROM MovieActors WHERE MovieId=$movie", ("$movie", movie.Id));
            if (await TableExistsAsync(connection, transaction, "MovieDirectors"))
                await ExecuteAsync(connection, transaction, "DELETE FROM MovieDirectors WHERE MovieId=$movie", ("$movie", movie.Id));
        }
        foreach (string genre in metadata.Genres) {
            long id = await EnsureNamedAsync(connection, transaction, "Genres", genre);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieGenres(MovieId,GenreId) VALUES($movie,$id)", ("$movie", movie.Id), ("$id", id));
        }
        foreach (string actor in metadata.Actors) {
            long id = await EnsureActorAsync(connection, transaction, actor);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES($movie,$id,'',999)", ("$movie", movie.Id), ("$id", id));
        }
        if (!string.IsNullOrWhiteSpace(metadata.Director)
            && await TableExistsAsync(connection, transaction, "Directors")
            && await TableExistsAsync(connection, transaction, "MovieDirectors")) {
            long id = await EnsureNamedAsync(connection, transaction, "Directors", metadata.Director);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieDirectors(MovieId,DirectorId) VALUES($movie,$id)", ("$movie", movie.Id), ("$id", id));
        }
        foreach ((string? name, string relation) in new[] { (metadata.Studio, "Studio"), (metadata.Publisher, "Publisher") }) {
            if (string.IsNullOrWhiteSpace(name)) continue;
            long id = await EnsureNamedAsync(connection, transaction, "Studios", name);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieStudios(MovieId,StudioId,RelationType) VALUES($movie,$id,$type)", ("$movie", movie.Id), ("$id", id), ("$type", relation));
        }
        if (!string.IsNullOrWhiteSpace(metadata.Series)) {
            long id = await EnsureNamedAsync(connection, transaction, "Series", metadata.Series);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES($movie,$id,0)", ("$movie", movie.Id), ("$id", id));
        }
        foreach (SavedImage image in files.Images)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO Images(MovieId,ActorId,ImageType,FilePath,SourceUrl,Width,Height,FileSize,FileHash,
                    IsPrimary,SourceProvider,DownloadedAt,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ContentType,ValidationStatus,ValidatedAt)
                SELECT $movie,NULL,$type,$path,$url,$width,$height,$size,$hash,$primary,$provider,$at,$at,$at,
                    $ownership,0,$derived,$content,'Valid',$at
                WHERE NOT EXISTS (
                    SELECT 1 FROM Images
                     WHERE MovieId=$movie AND ActorId IS NULL AND ImageType=$type
                       AND lower(FilePath)=lower($path))
                """, ("$movie", movie.Id), ("$type", image.Type), ("$path", image.Path), ("$url", image.SourceUrl),
                ("$width", image.Width), ("$height", image.Height), ("$size", image.Size), ("$hash", image.FileHash),
                ("$primary", image.Type == "Poster" ? 1 : 0), ("$provider", image.SourceProvider ?? metadata.Provider),
                ("$ownership", image.Ownership), ("$derived", image.IsDerived ? 1 : 0),
                ("$content", image.ContentType), ("$at", Now()));
        string applied = JsonSerializer.Serialize(new { metadata.Provider, metadata.ExternalId, metadata.Code, metadata.Title, metadata.OriginalTitle, metadata.Rating,
            metadata.Confidence, metadata.FieldSources, metadata.RawResponseHash,
            ImagesDownloaded = files.Images.Count(value => value.Created), ImagesPreserved = files.Images.Count(value => !value.Created),
            Genres = metadata.Genres.Count, Actors = metadata.Actors.Count, Director = string.IsNullOrWhiteSpace(metadata.Director) ? 0 : 1,
            AddedFields = addedFields, NonDestructive = !overwrite });
        await ExecuteAsync(connection, transaction, "UPDATE MetadataSyncSnapshots SET AppliedJson=$applied,AppliedAt=$at WHERE Id=$id",
            ("$applied", applied), ("$at", Now()), ("$id", snapshotId));
        await transaction.CommitAsync(cancellationToken);
        return applied;
    }

    private static async Task<IReadOnlyList<string>> DetermineAddedFieldsAsync(SqliteConnection connection,
        System.Data.Common.DbTransaction transaction, SyncMovie movie, ProviderMetadata metadata, PreparedFiles files, bool overwrite)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (overwrite || string.IsNullOrWhiteSpace(movie.Title) || movie.Title == movie.Code) AddIf(fields, "标题", metadata.Title);
        if (overwrite || string.IsNullOrWhiteSpace(movie.Description)) AddIf(fields, "简介", metadata.Description);
        if (overwrite || string.IsNullOrWhiteSpace(movie.ReleaseDate)) AddIf(fields, "日期", metadata.ReleaseDate);
        if ((overwrite || movie.DurationSeconds <= 0) && metadata.DurationSeconds is > 0) fields.Add("时长");
        string? original = await ScalarTextAsync(connection, transaction, "SELECT OriginalTitle FROM Movies WHERE Id=$id", ("$id", movie.Id));
        if (overwrite || string.IsNullOrWhiteSpace(original)) AddIf(fields, "原始标题", metadata.OriginalTitle);
        long hasRating = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM Movies WHERE Id=$id AND ProviderRating IS NOT NULL AND ProviderRating<>0", ("$id", movie.Id));
        if ((overwrite || hasRating == 0) && metadata.Rating is > 0) fields.Add("评分");
        if (await HasNewRelationAsync(connection, transaction, movie.Id, "Directors", "MovieDirectors", "DirectorId", Values(metadata.Director))) fields.Add("导演");
        if (await HasNewRelationAsync(connection, transaction, movie.Id, "Studios", "MovieStudios", "StudioId", Values(metadata.Studio, metadata.Publisher))) fields.Add("厂商");
        if (await HasNewRelationAsync(connection, transaction, movie.Id, "Series", "MovieSeries", "SeriesId", Values(metadata.Series))) fields.Add("系列");
        if (await HasNewRelationAsync(connection, transaction, movie.Id, "Genres", "MovieGenres", "GenreId", metadata.Genres)) fields.Add("标签");
        if (await HasNewRelationAsync(connection, transaction, movie.Id, "Actors", "MovieActors", "ActorId", metadata.Actors)) fields.Add("演员");
        foreach (SavedImage image in files.Images.Where(value => value.Created)) fields.Add(image.Type);
        if (!string.IsNullOrWhiteSpace(files.NfoPath) && files.CreatedPaths.Contains(files.NfoPath, StringComparer.OrdinalIgnoreCase)) fields.Add("NFO");
        return fields.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<bool> HasNewRelationAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        long movieId, string entityTable, string relationTable, string relationKey, IReadOnlyList<string> values)
    {
        if (!await TableExistsAsync(connection, transaction, entityTable) || !await TableExistsAsync(connection, transaction, relationTable)) return false;
        foreach (string value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            long exists = await ScalarLongAsync(connection, transaction,
                $"SELECT COUNT(*) FROM {relationTable} r JOIN {entityTable} e ON e.Id=r.{relationKey} WHERE r.MovieId=$movie AND e.NormalizedName=$name",
                ("$movie", movieId), ("$name", Normalize(value)));
            if (exists == 0) return true;
        }
        return false;
    }

    private static IReadOnlyList<string> Values(params string?[] values) => values.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
    private static void AddIf(HashSet<string> fields, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) fields.Add(name); }

    private static async Task<long> EnsureNamedAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string table, string value) {
        string name = value.Trim(); string normalized = Normalize(name);
        string idColumn = table == "Genres" ? "GenreId" : table == "Studios" ? "StudioId" : "SeriesId";
        _ = idColumn;
        long existing = await ScalarLongAsync(c, tx, $"SELECT COALESCE(MAX(Id),0) FROM {table} WHERE NormalizedName=$name", ("$name", normalized));
        if (existing > 0) return existing;
        string columns = table is "Genres" or "Directors" ? "Name,NormalizedName" : table == "Studios" ? "Name,NormalizedName,Description" : "Name,NormalizedName,Description,ExternalId";
        string values = table is "Genres" or "Directors" ? "$name,$normalized" : table == "Studios" ? "$name,$normalized,NULL" : "$name,$normalized,NULL,NULL";
        return await InsertIdAsync(c, tx, $"INSERT INTO {table}({columns}) VALUES({values}); SELECT last_insert_rowid();", ("$name", name), ("$normalized", normalized));
    }
    private static async Task<long> EnsureActorAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string value) {
        string name = value.Trim(); string normalized = Normalize(name);
        long existing = await ScalarLongAsync(c, tx, "SELECT COALESCE(MAX(Id),0) FROM Actors WHERE NormalizedName=$name", ("$name", normalized));
        if (existing > 0) return existing;
        return await InsertIdAsync(c, tx, """
            INSERT INTO Actors(Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt)
            VALUES($name,$normalized,'MetaTube',$at,$at); SELECT last_insert_rowid();
            """, ("$name", name), ("$normalized", normalized), ("$at", Now()));
    }
    private async Task<SqliteConnection> OpenAsync() { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=databasePath, Mode=SqliteOpenMode.ReadWrite }.ToString()); await c.OpenAsync(); return c; }
    private static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private static async Task ExecuteAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); await x.ExecuteNonQueryAsync(); }
    private static async Task<long> InsertIdAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); return Convert.ToInt64(await x.ExecuteScalarAsync()); }
    private static async Task<long> ScalarLongAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); return Convert.ToInt64(await x.ExecuteScalarAsync()??0L); }
    private static async Task<string?> ScalarTextAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); return (await x.ExecuteScalarAsync())?.ToString(); }
    private static async Task<bool> TableExistsAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string table) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table"; x.Parameters.AddWithValue("$table", table); return Convert.ToInt64(await x.ExecuteScalarAsync()??0L)>0; }
}

public sealed class MetadataSyncExecutor(
    string databasePath,
    MediaStoragePathResolver pathResolver,
    MetadataProviderSettingsService settingsService,
    ProviderDiagnosticsService diagnostics,
    IMetadataProvider provider,
    MetadataWriteService writer,
    ImageDownloadService images,
    NfoService nfo,
    TaskLogService logs,
    MovieImageImporter? actorImageImporter = null,
    MetadataHealthAnalysisService? health = null,
    IMovieNumberExtractor? movieNumberExtractor = null) : BackgroundService
{
    private const int MaxConcurrentSyncTasks = 6;
    private static readonly string[] ActiveStates = ["Preparing", "FetchingMetadata", "DownloadingImages", "WritingMetadata", "WritingNfo", "Running"];
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RecoverInterruptedAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception error) { Console.Error.WriteLine($"Metadata sync recovery skipped: {error}"); }
        var running = new List<Task>();
        while (!stoppingToken.IsCancellationRequested) {
            try {
                MetadataProviderContext settings = await ReadProviderContextAsync(stoppingToken);
                foreach (Task completed in running.Where(task => task.IsCompleted).ToArray()) {
                    running.Remove(completed);
                    if (completed.IsFaulted) await completed;
                }
                while (running.Count < MaxConcurrentSyncTasks) {
                    long? taskId = await ClaimAsync(stoppingToken);
                    if (taskId is null) break;
                    running.Add(RunClaimedAsync(taskId.Value, settings, stoppingToken));
                }
                if (running.Count == 0) { await Task.Delay(750, stoppingToken); continue; }
                Task finished = await Task.WhenAny(running.Append(Task.Delay(750, stoppingToken)));
                if (finished.IsFaulted) await finished;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Console.Error.WriteLine($"Metadata sync runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
        if (running.Count > 0) {
            try { await Task.WhenAll(running); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
    private async Task RunClaimedAsync(long taskId, MetadataProviderContext settings, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cancellations[taskId] = linked;
        try { await ExecuteOneAsync(taskId, settings, linked.Token); }
        finally { cancellations.TryRemove(taskId, out _); }
    }

    public async Task<TaskMutationResult> PauseAsync(long taskId) {
        await UpdateTaskAsync(taskId, "Paused", "Paused", null, null, false);
        await logs.WriteAsync(taskId, "Info", "同步任务已暂停；当前网络步骤完成后停止推进。");
        return new(taskId, "Paused", "同步任务已暂停。");
    }
    public async Task<TaskMutationResult> ResumeAsync(long taskId) {
        await UpdateTaskAsync(taskId, "Pending", "Pending", null, null, false);
        await logs.WriteAsync(taskId, "Info", "同步任务已继续并重新进入队列。");
        return new(taskId, "Pending", "同步任务已继续。");
    }
    public async Task<TaskMutationResult> CancelAsync(long taskId) {
        if (cancellations.TryGetValue(taskId, out CancellationTokenSource? source)) source.Cancel();
        await UpdateTaskAsync(taskId, "Cancelled", "Cancelled", null, "用户取消", true);
        await logs.WriteAsync(taskId, "Warning", "同步任务已由用户取消。");
        return new(taskId, "Cancelled", "同步任务已取消；已有有效数据未被覆盖。");
    }
    public async Task<MetadataSyncLaunchResult> RetryAsync(long taskId) {
        await using var connection = await OpenAsync();
        string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='Sync'", ("$id", taskId));
        if (status is null) throw new KeyNotFoundException("同步任务不存在。");
        if (status is not ("Failed" or "Cancelled" or "NoResult" or "Blocked")) throw new InvalidOperationException("只有失败、无结果、被阻断或已取消的同步任务可以重试。");
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Retrying',Stage='Retrying',RetryCount=RetryCount+1,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,UpdatedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Info", "同步任务已进入重试队列。");
        return new(taskId, "Retrying", "同步任务已进入重试队列。");
    }
    public async Task<MetadataSyncLaunchResult> EnqueueAsync(long movieId, string trigger, bool overwrite = false, string? source = null, IReadOnlyList<string>? targetFields = null) {
        await using var connection = await OpenAsync();
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Movies WHERE Id=$id", ("$id", movieId)) == 0) throw new KeyNotFoundException("影片不存在。");
        if (await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM MediaFiles f JOIN Libraries l ON l.Id=f.LibraryId
             WHERE f.MovieId=$id AND l.LibraryType='Local'
            """, ("$id", movieId)) > 0)
            throw new InvalidOperationException("普通媒体库影片不执行番号刮削或 Provider 元数据同步。");
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 AND MediaType='Video'", ("$id", movieId)) == 0)
            throw new InvalidOperationException("当前影片没有主媒体关联，不能创建同步任务。");
        long existing = await ScalarLongAsync(connection, "SELECT COALESCE(MAX(Id),0) FROM Tasks WHERE TaskType='Sync' AND CurrentMovieId=$movie AND Status NOT IN ('Completed','CompletedWithErrors','CompletedWithWarnings','NoResult','Blocked','Failed','Cancelled')", ("$movie", movieId));
        if (existing > 0) return new(existing, "Pending", "该影片已有同步任务。");
        long id = await InsertIdAsync(connection, """
            INSERT INTO Tasks(TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId)
            VALUES('Sync','Pending','Pending',$provider,0,1,0,$payload,$at,$at,$movie); SELECT last_insert_rowid();
            """, ("$provider", NormalizeSource(source) ?? "Auto"), ("$payload", JsonSerializer.Serialize(new { MovieId=movieId, Trigger=trigger, Overwrite=overwrite, Source=NormalizeSource(source), TargetFields=targetFields })), ("$at", Now()), ("$movie", movieId));
        await logs.WriteAsync(id, "Info", $"同步任务已创建（{trigger}）。");
        return new(id, "Pending", "同步任务已创建。");
    }

    public async Task<BatchTaskMutationResult> EnqueueBatchAsync(IReadOnlyList<long> movieIds) {
        long[] ids = movieIds.Distinct().Where(id => id > 0).ToArray();
        if (ids.Length is 0 or > 500) throw new ArgumentException("Select 1 to 500 movies.");
        foreach (long id in ids) await EnqueueAsync(id, "Batch");
        return new(ids.Length, $"Created {ids.Length} sync tasks.");
    }

    public async Task<BatchTaskMutationResult> EnqueueLibraryAsync(long? libraryId = null) {
        await using var connection = await OpenAsync();
        if (libraryId.HasValue) {
            string? type = await ScalarTextAsync(connection, "SELECT LibraryType FROM Libraries WHERE Id=$id", ("$id", libraryId.Value));
            if (type is null) throw new KeyNotFoundException("Library does not exist.");
            if (MediaLibraryType.Parse(type) == LibraryType.Local)
                return new(0, "普通媒体库不创建元数据同步任务。");
        }
        await using var command = connection.CreateCommand();
        command.CommandText = libraryId.HasValue
            ? """
              SELECT DISTINCT m.Id
              FROM Movies m
              JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
              WHERE f.LibraryId=$library AND COALESCE(f.ExistsState,'')<>'Missing'
              ORDER BY m.Id
              """
            : """
              SELECT DISTINCT m.Id
              FROM Movies m
              JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
              JOIN Libraries l ON l.Id=f.LibraryId AND l.LibraryType='Standard'
              WHERE COALESCE(f.ExistsState,'')<>'Missing'
              ORDER BY m.Id
              """;
        if (libraryId.HasValue) command.Parameters.AddWithValue("$library", libraryId.Value);
        var ids = new List<long>();
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
        }
        foreach (long id in ids) await EnqueueAsync(id, "Library");
        return new(ids.Count, libraryId.HasValue ? $"Created sync tasks for {ids.Count} movies in library." : $"Created sync tasks for {ids.Count} movies.");
    }

    public async Task<FilteredMovieSyncPreview> PreviewFilteredAsync(FilteredMovieSyncCommand command, CancellationToken cancellationToken) {
        IReadOnlyList<long> ids = await ReadFilteredMovieIdsAsync(command);
        return new(ids.Count);
    }

    public async Task<FilteredMovieSyncResult> EnqueueFilteredAsync(FilteredMovieSyncCommand command, CancellationToken cancellationToken) {
        IReadOnlyList<long> ids = await ReadFilteredMovieIdsAsync(command);
        if (ids.Count == 0) return new(0, "当前筛选结果没有匹配影片，未创建同步任务。");
        foreach (long id in ids) {
            cancellationToken.ThrowIfCancellationRequested();
            await EnqueueAsync(id, "FilteredRepair", false, null, command.TargetFields);
        }
        return new(ids.Count, $"已为当前筛选结果的 {ids.Count} 部影片创建同步任务。");
    }

    private Task<IReadOnlyList<long>> ReadFilteredMovieIdsAsync(FilteredMovieSyncCommand command) =>
        ProductReader.ReadAdvancedSearchMovieIdsAsync(databasePath, command.Query ?? "", command.ActorId, command.TagId,
            command.DirectorId, command.MovieTagId, command.CustomTagId, command.SeriesId, command.Favorite, command.Watched,
            Math.Clamp(command.RatingMin ?? 0, 0, 5), command.RatingFilter ?? "all", command.Metadata ?? "all",
            command.FileStatus ?? "all", command.MetadataStatus ?? "all", command.LibraryId, command.Sort ?? "newest",
            command.GenreId, command.StudioId);

    private async Task ExecuteOneAsync(long taskId, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        var createdPaths = new List<string>();
        var partialFailures = new List<string>();
        try {
            SyncMovie movie = await ReadMovieAsync(taskId, cancellationToken);
            bool overwrite = await ReadOverwriteAsync(taskId, cancellationToken);
            HashSet<string>? targets = await ReadTargetFieldsAsync(taskId, cancellationToken);
            MovieNumberExtractionResult? extraction = movieNumberExtractor?.Extract(movie.Code);
            string? normalizedCode = extraction?.NormalizedNumber ?? NormalizeSyncCode(movie.Code);
            if (!string.IsNullOrWhiteSpace(extraction?.NormalizedNumber)
                && extraction.Confidence < movieNumberExtractor!.MinimumAutoSyncConfidence)
                throw new MetadataProviderBlockedException($"番号识别置信度 {extraction.Confidence:0.00} 低于自动同步阈值 {movieNumberExtractor.MinimumAutoSyncConfidence:0.00}。");
            if (string.IsNullOrWhiteSpace(normalizedCode))
                throw new MetadataProviderBlockedException("影片没有可用于同步的番号。");
            movie = movie with { Code = normalizedCode };
            if (extraction is not null)
                await logs.WriteAsync(taskId, "Info", $"[Movie Number] Original={extraction.OriginalFileName}; Matched={extraction.MatchedRule}; Detected={extraction.DetectedNumber}; Normalized={extraction.NormalizedNumber}; Confidence={extraction.Confidence:0.00}; PartIndex={extraction.PartIndex?.ToString() ?? "None"}", cancellationToken);
            await StageAsync(taskId, "Preparing", 8, $"准备影片 {movie.Code}", cancellationToken);
            await EnsureRunnableAsync(taskId, cancellationToken);

            settings = settings with {
                PreferredSource = await ReadSourceAsync(taskId, cancellationToken),
                CurrentMoviePath = movie.PrimaryFile,
                RequestedFields = targets,
                ProviderLog = (source, message, token) => logs.WriteAsync(taskId, "Info", $"[{source}] {message}", token),
                ProviderDebugLog = (source, message, token) => logs.WriteAsync(taskId, "Debug", $"[{source}] {message}", token),
                ProviderFailure = diagnostics.ReportRuntimeFailureAsync,
                ProviderSuccess = diagnostics.ReportRuntimeSuccessAsync,
            };
            settings = await diagnostics.FilterMovieProvidersAsync(settings, cancellationToken);
            await StageAsync(taskId, "FetchingMetadata", 22, $"{settings.PreferredSource ?? "自动数据源"} 搜索：{movie.Code}", cancellationToken);
            IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync(movie.Code, settings, cancellationToken);
            await logs.WriteAsync(taskId, "Info", $"Provider Search Results: {results.Count} candidate(s).", cancellationToken);
            if (results.Count == 0) throw new MetadataNoResultException($"{settings.PreferredSource ?? "元数据源"} 未找到 {movie.Code} 的结果。");
            ProviderMetadata? metadata = null;
            var attemptErrors = new List<string>();
            foreach (MetadataSearchResult selected in results) {
                await SetProviderAsync(taskId, selected.Provider, cancellationToken);
                await logs.WriteAsync(taskId, "Info", $"Provider Detail Request: {selected.Provider} externalId={selected.ExternalId}", cancellationToken);
                try {
                    ProviderMetadata? candidate = await provider.GetMetadataAsync(selected, settings, cancellationToken);
                    if (candidate is null) {
                        attemptErrors.Add($"{selected.Provider}: empty metadata");
                        await logs.WriteAsync(taskId, "Warning", $"{selected.Provider} 返回结果缺少可用元数据，继续尝试下一个候选。", cancellationToken);
                        continue;
                    }
                    if (!(movieNumberExtractor?.AreEquivalent(movie.Code, candidate.Code, candidate.ExternalId)
                        ?? Comparable(candidate.Code).Equals(Comparable(movie.Code), StringComparison.OrdinalIgnoreCase))) {
                        attemptErrors.Add($"{selected.Provider}: expected {movie.Code}, got {candidate.Code}");
                        await logs.WriteAsync(taskId, "Warning", $"{selected.Provider} 结果番号不匹配：期望 {movie.Code}，实际 {candidate.Code}，继续尝试下一个候选。", cancellationToken);
                        continue;
                    }
                    metadata = candidate;
                    break;
                } catch (Exception error) when (error is not OperationCanceledException) {
                    attemptErrors.Add($"{selected.Provider}: {error.Message}");
                    await logs.WriteAsync(taskId, "Warning", $"{selected.Provider} 详情获取失败，继续尝试下一个候选：{error.Message}", cancellationToken);
                }
            }
            if (metadata is null)
                throw new MetadataNoResultException($"{settings.PreferredSource ?? "元数据源"} 未找到 {movie.Code} 的可用详情。Attempts: {string.Join(" | ", attemptErrors)}");
            if (targets is not null) {
                metadata = RestrictToTargets(metadata, movie, targets);
                await logs.WriteAsync(taskId, "Info", $"[Repair Target] {string.Join(",", targets.Order())}", cancellationToken);
            }
            await logs.WriteAsync(taskId, "Info",
                $"Parse Success: title={(string.IsNullOrWhiteSpace(metadata.Title) ? 0 : 1)}, actors={metadata.Actors.Count}, director={(string.IsNullOrWhiteSpace(metadata.Director) ? 0 : 1)}, series={(string.IsNullOrWhiteSpace(metadata.Series) ? 0 : 1)}, tags={metadata.Genres.Count}, images={metadata.Images.Count}",
                cancellationToken);

            IReadOnlyList<SavedImage> savedImages = [];
            if (settings.DownloadImages(metadata.Provider) && (targets is null || targets.Overlaps(["Poster", "Fanart", "Preview"]))) {
                await StageAsync(taskId, "DownloadingImages", 48, $"下载图片（{metadata.Images.Count} 项）", cancellationToken);
                ImageDownloadOptions? imageOptions = JavBusImageOptions(settings.JavBus);
                try {
                    ImageDownloadBatchResult download = await images.DownloadBatchAsync(pathResolver, new(movie.Id, movie.Code, movie.Title),
                        await provider.GetImagesAsync(metadata, cancellationToken), settings.TimeoutSeconds(metadata.Provider), overwrite, cancellationToken, imageOptions);
                    savedImages = download.Images;
                    createdPaths.AddRange(savedImages.Where(value => value.Created).Select(value => value.Path));
                    if (download.Failures.Count > 0) {
                        foreach (ImageDownloadFailure failure in download.Failures)
                            await logs.WriteAsync(taskId, "Warning", ImageDownloadService.FormatFailureEvidence(failure), cancellationToken);
                        string imageFailureSummary = string.Join(", ", download.Failures.GroupBy(value => value.Type, StringComparer.OrdinalIgnoreCase)
                            .Select(group => $"{group.Key} {group.Count()} 项"));
                        partialFailures.Add($"部分图片写入失败: {imageFailureSummary}");
                        await logs.WriteAsync(taskId, "Warning", $"[Image Write] Partial failure; continuing with valid images: {imageFailureSummary}", cancellationToken);
                    }
                } catch (Exception error) when (error is not OperationCanceledException) {
                    partialFailures.Add($"图片写入失败: {error.Message}");
                    await logs.WriteAsync(taskId, "Warning", $"[Image Write] Failed; metadata merge will continue: {error.Message}", cancellationToken);
                }
            }
            string? nfoPath = null;
            if (settings.MetaTube.WriteNfo && (targets is null || targets.Contains("NFO"))) {
                await StageAsync(taskId, "WritingNfo", 68, "生成 NFO（已有文件不会覆盖）", cancellationToken);
                try {
                    (nfoPath, bool created) = targets is not null && targets.SetEquals(["NFO"])
                        ? await nfo.WriteFromDatabaseAsync(movie.Id, cancellationToken)
                        : await nfo.WriteAsync(movie, metadata, cancellationToken);
                    if (created && nfoPath is not null) createdPaths.Add(nfoPath);
                    await logs.WriteAsync(taskId, "Info", $"[NFO Write] Success: {nfoPath ?? "skipped"}", cancellationToken);
                } catch (Exception error) when (error is not OperationCanceledException) {
                    partialFailures.Add($"NFO 写入失败: {error.Message}");
                    await logs.WriteAsync(taskId, "Warning", $"[NFO Write] Failed; metadata merge will continue: {error.Message}", cancellationToken);
                }
            }
            await StageAsync(taskId, "WritingMetadata", 82, overwrite ? "覆盖同步刮削元数据" : "非破坏合并元数据", cancellationToken);
            string summary = await writer.ApplyAsync(taskId, movie, metadata, new(savedImages, nfoPath, createdPaths), overwrite, cancellationToken);
            await logs.WriteAsync(taskId, "Info", $"[Database Merge] {summary}", cancellationToken);
            await logs.WriteAsync(taskId, "Info", "[Database Merge] Success", cancellationToken);
            health?.Invalidate();
            if (actorImageImporter is not null && settings.DownloadImages(metadata.Provider) && metadata.ActorImages?.Count > 0 && (targets is null || targets.Contains("Actors"))) {
                await StageAsync(taskId, "DownloadingImages", 92, $"下载演员头像（{metadata.ActorImages.Count} 项）", cancellationToken);
                MovieImageImportResult actorImages = await actorImageImporter.ImportActorImagesAsync(
                    metadata.ActorImages, metadata.Provider, settings.TimeoutSeconds(metadata.Provider), overwrite, cancellationToken);
                foreach (string warning in actorImages.Warnings)
                    await logs.WriteAsync(taskId, "Warning", warning, cancellationToken);
                if (actorImages.ActorImagesDownloaded > 0)
                    await logs.WriteAsync(taskId, "Info", $"演员头像已下载：{actorImages.ActorImagesDownloaded} 张。", cancellationToken);
            }
            await CompleteAsync(taskId, summary, partialFailures, cancellationToken);
        } catch (OperationCanceledException) {
            await SafeDeleteAsync(createdPaths);
            await MarkInterruptedAsync(taskId);
        } catch (MetadataProviderBlockedException error) {
            await SafeDeleteAsync(createdPaths);
            await FinishWithoutWriteAsync(taskId, "Blocked", "同步被 Provider 可用性检查阻断", error.Message);
        } catch (MetadataNoResultException error) {
            await SafeDeleteAsync(createdPaths);
            await FinishWithoutWriteAsync(taskId, "NoResult", "Provider 没有返回匹配结果", error.Message);
        } catch (Exception error) {
            await SafeDeleteAsync(createdPaths);
            await FailAsync(taskId, error);
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken token) {
        await using var connection = await OpenAsync();
        var interrupted = new List<long>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = $"SELECT Id FROM Tasks WHERE TaskType='Sync' AND Status IN ({string.Join(',', ActiveStates.Select((_,i)=>"$s"+i))})";
            for(int i=0;i<ActiveStates.Length;i++) command.Parameters.AddWithValue("$s"+i,ActiveStates[i]);
            await using var reader = await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)) interrupted.Add(reader.GetInt64(0));
        }
        foreach(long id in interrupted) {
            await ExecuteAsync(connection, "UPDATE Tasks SET Status='Retrying',Stage='Retrying',RetryCount=RetryCount+1,ErrorMessage='上次异常中断',UpdatedAt=$at WHERE Id=$id", ("$at",Now()),("$id",id));
            await logs.WriteAsync(id,"Warning","上次异常中断，任务已恢复到可重试队列。",token);
        }
    }
    private async Task<long?> ClaimAsync(CancellationToken token) {
        await using var connection = await OpenAsync(); await using var tx=await connection.BeginTransactionAsync(token);
        long id=await ScalarLongAsync(connection,"SELECT COALESCE((SELECT Id FROM Tasks WHERE TaskType='Sync' AND Status IN ('Pending','Retrying') ORDER BY CASE Status WHEN 'Retrying' THEN 0 ELSE 1 END, Id LIMIT 1),0)",tx);
        if(id==0){await tx.RollbackAsync(token);return null;}
        await ExecuteAsync(connection,"UPDATE Tasks SET Status='Preparing',Stage='Preparing',Provider='MetaTube',Progress=3,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at,CancellationRequested=0 WHERE Id=$id",tx,("$at",Now()),("$id",id));
        await tx.CommitAsync(token); return id;
    }
    private async Task<SyncMovie> ReadMovieAsync(long taskId,CancellationToken token) {
        await using var c=await OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="""
            SELECT m.Id,COALESCE(m.Code,''),m.Title,m.Description,m.ReleaseDate,m.DurationSeconds,m.NfoPath,
                   (SELECT FilePath FROM MediaFiles WHERE MovieId=m.Id AND IsPrimary=1 AND MediaType='Video' ORDER BY Id LIMIT 1)
            FROM Tasks t JOIN Movies m ON m.Id=COALESCE(t.CurrentMovieId,json_extract(t.PayloadJson,'$.MovieId')) WHERE t.Id=$task
            """; x.Parameters.AddWithValue("$task",taskId); await using var r=await x.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token))throw new KeyNotFoundException("同步任务关联的影片不存在。");
        return new(r.GetInt64(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt32(5),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(6)?null:r.GetString(6));
    }
    private async Task<bool> ReadOverwriteAsync(long taskId,CancellationToken token) {
        await using var c=await OpenAsync(); string? payload=await ScalarTextAsync(c,"SELECT PayloadJson FROM Tasks WHERE Id=$id",("$id",taskId));
        if(string.IsNullOrWhiteSpace(payload)) return false;
        try { using JsonDocument json=JsonDocument.Parse(payload); return json.RootElement.TryGetProperty("Overwrite", out JsonElement value) && value.ValueKind is JsonValueKind.True; }
        catch(JsonException) { return false; }
    }
    private async Task<HashSet<string>?> ReadTargetFieldsAsync(long taskId, CancellationToken token) {
        await using var c=await OpenAsync(); string? payload=await ScalarTextAsync(c,"SELECT PayloadJson FROM Tasks WHERE Id=$id",("$id",taskId));
        if(string.IsNullOrWhiteSpace(payload)) return null;
        try { using JsonDocument json=JsonDocument.Parse(payload); if(!json.RootElement.TryGetProperty("TargetFields",out JsonElement value)||value.ValueKind!=JsonValueKind.Array)return null; var result=value.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase); return result.Count==0?null:result; }
        catch(JsonException) { return null; }
    }
    private static ProviderMetadata RestrictToTargets(ProviderMetadata value, SyncMovie movie, HashSet<string> targets) => value with {
        Code = movie.Code,
        Title = targets.Contains("Title") ? value.Title : null,
        Description = targets.Contains("Description") ? value.Description : null,
        Director = targets.Contains("Director") ? value.Director : null,
        Studio = targets.Contains("Studio") ? value.Studio : null,
        Publisher = targets.Contains("Studio") ? value.Publisher : null,
        Series = targets.Contains("Series") ? value.Series : null,
        DurationSeconds = targets.Contains("Duration") ? value.DurationSeconds : null,
        ReleaseDate = targets.Contains("ReleaseDate") ? value.ReleaseDate : null,
        Genres = targets.Contains("Tags") ? value.Genres : [],
        Actors = targets.Contains("Actors") ? value.Actors : [],
        Images = value.Images.Where(image => targets.Contains(MediaStoragePathResolver.NormalizeResourceType(image.Type))).ToArray(),
        Rating = targets.Contains("Rating") ? value.Rating : null,
        OriginalTitle = targets.Contains("Title") ? value.OriginalTitle : null,
        ActorImages = targets.Contains("Actors") ? value.ActorImages : [],
    };
    private async Task<string?> ReadSourceAsync(long taskId,CancellationToken token) {
        await using var c=await OpenAsync(); string? payload=await ScalarTextAsync(c,"SELECT PayloadJson FROM Tasks WHERE Id=$id",("$id",taskId));
        if(string.IsNullOrWhiteSpace(payload)) return null;
        try { using JsonDocument json=JsonDocument.Parse(payload); return json.RootElement.TryGetProperty("Source", out JsonElement value) && value.ValueKind == JsonValueKind.String ? NormalizeSource(value.GetString()) : null; }
        catch(JsonException) { return null; }
    }
    private async Task<MetadataProviderContext> ReadProviderContextAsync(CancellationToken token) =>
        new(await settingsService.ReadMetaTubeAsync(), await settingsService.ReadJavBusAsync(), null,
            await settingsService.ReadDmmAsync(), await settingsService.ReadJavDbAsync(), await settingsService.ReadNetworkAsync()) {
            MdcNg = await settingsService.ReadMdcNgAsync(),
        };
    private static ImageDownloadOptions? JavBusImageOptions(JavBusSettingsDto settings) {
        if (string.IsNullOrWhiteSpace(settings.Cookie)
            || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out Uri? uri)) return null;
        return new(settings.Cookie, uri.GetLeftPart(UriPartial.Authority) + "/", uri.Host);
    }
    private static string? NormalizeSyncCode(string value) {
        string? standard = MovieCodeNormalizer.Normalize(value);
        if (!string.IsNullOrWhiteSpace(standard)) return standard;
        string fc2 = JavBusCode.Normalize(value);
        return fc2.StartsWith("FC2-PPV-", StringComparison.OrdinalIgnoreCase) ? fc2 : null;
    }
    private async Task SetProviderAsync(long id,string providerName,CancellationToken token){await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Provider=$provider,UpdatedAt=$at WHERE Id=$id",("$provider",providerName),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Info",$"使用数据源：{providerName}",token);}
    private static string? NormalizeSource(string? value) => value?.Trim().ToLowerInvariant() switch { "javbus" => "JavBus", "metatube" => "MetaTube", "mdc-ng" or "mdcng" => "MDC-NG", _ => null };
    private async Task StageAsync(long id,string stage,double progress,string message,CancellationToken token){await EnsureRunnableAsync(id,token);await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status=$stage,Stage=$stage,Progress=$progress,UpdatedAt=$at WHERE Id=$id",("$stage",stage),("$progress",progress),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Info",message,token);}
    private async Task EnsureRunnableAsync(long id,CancellationToken token){while(true){token.ThrowIfCancellationRequested();await using var c=await OpenAsync();string? s=await ScalarTextAsync(c,"SELECT Status FROM Tasks WHERE Id=$id",("$id",id));if(s=="Cancelled")throw new OperationCanceledException(token);if(s!="Paused")return;await Task.Delay(250,token);}}
    private async Task CompleteAsync(long id,string summary,IReadOnlyList<string> partialFailures,CancellationToken token){string status=partialFailures.Count==0?"Completed":"CompletedWithWarnings";string resultSummary=partialFailures.Count==0?"元数据同步完成":"元数据已获取并写入数据库，但部分文件步骤已跳过或失败";string? error=partialFailures.Count==0?null:string.Join(" | ",partialFailures);await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status=$status,Stage=$status,Progress=100,CompletedItems=1,ResultJson=$result,ResultSummary=$summary,ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$status",status),("$result",summary),("$summary",resultSummary),("$error",error),("$at",Now()),("$id",id));await logs.WriteAsync(id,partialFailures.Count==0?"Info":"Warning",partialFailures.Count==0?"元数据、图片与 NFO 工作流已完成。":$"[Final] CompletedWithWarnings: Database Merge succeeded; {error}",token);}
    private async Task FinishWithoutWriteAsync(long id,string status,string summary,string reason){await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status=$status,Stage=$status,Progress=100,CompletedItems=0,ResultSummary=$summary,ErrorMessage=$reason,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$status",status),("$summary",summary),("$reason",reason),("$at",Now()),("$id",id));await logs.WriteAsync(id,status=="Blocked"?"Warning":"Info",$"[Final] {status}: {reason}");}
    private async Task FailAsync(long id,Exception error){try{await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,ResultSummary='同步失败，已有有效数据未被覆盖',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$error",error.Message),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Error",error.Message);}catch(Exception e){Console.Error.WriteLine($"Could not persist sync failure {id}: {e}");}}
    private async Task MarkInterruptedAsync(long id){try{await using var c=await OpenAsync();string active=string.Join(',',ActiveStates.Select((_,index)=>"$state"+index));await using var x=c.CreateCommand();x.CommandText=$"""
        UPDATE Tasks
           SET Status=CASE WHEN Status='Cancelled' AND CancellationRequested=1 THEN 'Cancelled' ELSE 'Retrying' END,
               Stage=CASE WHEN Status='Cancelled' AND CancellationRequested=1 THEN 'Cancelled' ELSE 'Retrying' END,
               ResultSummary=CASE WHEN Status='Cancelled' AND CancellationRequested=1 THEN '用户取消' ELSE '同步中断，等待继续' END,
               CompletedAt=CASE WHEN Status='Cancelled' AND CancellationRequested=1 THEN $at ELSE NULL END,
               UpdatedAt=$at
         WHERE Id=$id AND (Status='Cancelled' OR Status IN ({active}))
        """;x.Parameters.AddWithValue("$at",Now());x.Parameters.AddWithValue("$id",id);for(int index=0;index<ActiveStates.Length;index++)x.Parameters.AddWithValue("$state"+index,ActiveStates[index]);await x.ExecuteNonQueryAsync();}catch(Exception e){Console.Error.WriteLine(e);}}
    private async Task UpdateTaskAsync(long id,string status,string stage,double? progress,string? error,bool cancel){await using var c=await OpenAsync();if(await ScalarLongAsync(c,"SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='Sync'",("$id",id))==0)throw new KeyNotFoundException("同步任务不存在。");await ExecuteAsync(c,"UPDATE Tasks SET Status=$status,Stage=$stage,Progress=COALESCE($progress,Progress),ErrorMessage=$error,CancellationRequested=$cancel,UpdatedAt=$at WHERE Id=$id",("$status",status),("$stage",stage),("$progress",progress),("$error",error),("$cancel",cancel?1:0),("$at",Now()),("$id",id));}
    private async Task<SqliteConnection> OpenAsync(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadWrite}.ToString());await c.OpenAsync();await ExecuteAsync(c,"PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");return c;}
    private static async Task SafeDeleteAsync(IEnumerable<string> paths){foreach(string p in paths.Reverse())try{if(File.Exists(p))File.Delete(p);}catch{await Task.Yield();}}
    private static string Comparable(string value)=>JavBusCode.Normalize(value);
    private static string Now()=>DateTimeOffset.UtcNow.ToString("O");
    private static Task ExecuteAsync(SqliteConnection c,string sql,params (string,object?)[] p)=>ExecuteAsync(c,sql,null,p);
    private static async Task ExecuteAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> InsertIdAsync(SqliteConnection c,string sql,params (string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync());}
    private static Task<long> ScalarLongAsync(SqliteConnection c,string sql,params (string,object?)[] p)=>ScalarLongAsync(c,sql,null,p);
    private static async Task<long> ScalarLongAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private static async Task<string?> ScalarTextAsync(SqliteConnection c,string sql,params (string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return(await x.ExecuteScalarAsync())?.ToString();}
}

public sealed class TaskCommandService(string databasePath, LibraryWorkflowService libraries,
    MetadataSyncExecutor sync, ImageCacheTaskService imageCache, FileOrganizerService organizer,
    ImageGenerationTaskService imageGeneration, SafeDeleteWorkflowService safeDelete,
    ActorProfileCompleteTaskService actorProfileComplete)
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase) { "Completed", "CompletedWithErrors", "CompletedWithWarnings", "NoResult", "Blocked", "Failed", "Cancelled" };
    private static bool IsImageGeneration(string type) => type is "Poster" or "Preview" or "Screenshot" or "GIF";
    private static bool IsDelete(string type) => type is "DeleteMetadata" or "DeleteMedia";
    public async Task<TaskMutationResult> PauseAsync(long id)=>(await TypeAsync(id)) switch { "Sync"=>await sync.PauseAsync(id), "ImageCacheRebuild"=>await imageCache.PauseAsync(id), "Organizer"=>await organizer.PauseAsync(id), "ActorProfileComplete"=>await actorProfileComplete.PauseAsync(id), var type when IsImageGeneration(type)=>await imageGeneration.PauseAsync(id), var type when IsDelete(type)=>await safeDelete.PauseAsync(id), _=>await libraries.PauseTaskAsync(id) };
    public async Task<TaskMutationResult> ResumeAsync(long id)=>(await TypeAsync(id)) switch { "Sync"=>await sync.ResumeAsync(id), "ImageCacheRebuild"=>await imageCache.ResumeAsync(id), "Organizer"=>await organizer.ResumeAsync(id), "ActorProfileComplete"=>await actorProfileComplete.ResumeAsync(id), var type when IsImageGeneration(type)=>await imageGeneration.ResumeAsync(id), var type when IsDelete(type)=>await safeDelete.ResumeAsync(id), _=>await libraries.ResumeTaskAsync(id) };
    public async Task<TaskMutationResult> CancelAsync(long id)=>(await TypeAsync(id)) switch { "Sync"=>await sync.CancelAsync(id), "ImageCacheRebuild"=>await imageCache.CancelAsync(id), "Organizer"=>await organizer.CancelAsync(id), "ActorProfileComplete"=>await actorProfileComplete.CancelAsync(id), var type when IsImageGeneration(type)=>await imageGeneration.CancelAsync(id), var type when IsDelete(type)=>await safeDelete.CancelAsync(id), _=>await libraries.CancelTaskAsync(id) };
    public async Task<object> RetryAsync(long id)=>(await TypeAsync(id)) switch { "Sync"=>await sync.RetryAsync(id), "ImageCacheRebuild"=>await imageCache.RetryAsync(id), "Organizer"=>await organizer.RetryAsync(id), "ActorProfileComplete"=>await actorProfileComplete.RetryAsync(id), var type when IsImageGeneration(type)=>await imageGeneration.RetryAsync(id), var type when IsDelete(type)=>await safeDelete.RetryAsync(id), _=>await libraries.RetryTaskAsync(id) };
    public async Task<TaskCleanupResult> DeleteAsync(long id)
    {
        TaskSnapshot task = await SnapshotAsync(id);
        if (!TerminalStatuses.Contains(task.Status))
            throw new InvalidOperationException("只能删除已完成、失败或已取消的任务。");
        await using var connection = await OpenWriteAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "DELETE FROM TaskLogs WHERE TaskId=$id", transaction, ("$id", id));
        await ExecuteAsync(connection, "DELETE FROM Tasks WHERE Id=$id", transaction, ("$id", id));
        await transaction.CommitAsync();
        return new(1, $"已删除任务：{task.Type} #{id}");
    }
    public async Task<TaskCleanupResult> CleanupAsync(string? status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? "terminal" : status.Trim();
        string[] statuses = normalized.ToLowerInvariant() switch {
            "terminal" => ["Completed", "CompletedWithErrors", "CompletedWithWarnings", "NoResult", "Blocked", "Failed", "Cancelled"],
            "all-tasks" => ["Completed", "CompletedWithErrors", "CompletedWithWarnings", "NoResult", "Blocked", "Failed", "Cancelled"],
            "completed" => ["Completed"],
            "failed" => ["Failed"],
            "cancelled" or "canceled" => ["Cancelled"],
            _ => throw new ArgumentException("仅支持清理已完成、失败、已取消、全部终态或全部任务。"),
        };
        await using var connection = await OpenWriteAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string placeholders = string.Join(",", statuses.Select((_, index) => $"$s{index}"));
        string condition = $"Status IN ({placeholders})";
        (string, object?)[] parameters = statuses.Select((value, index) => ($"$s{index}", (object?)value)).ToArray();
        long count = await ScalarLongAsync(connection, $"SELECT COUNT(*) FROM Tasks WHERE {condition}", transaction, parameters);
        if (count > 0) {
            await ExecuteAsync(connection, $"DELETE FROM TaskLogs WHERE TaskId IN (SELECT Id FROM Tasks WHERE {condition})", transaction, parameters);
            await ExecuteAsync(connection, $"DELETE FROM Tasks WHERE {condition}", transaction, parameters);
        }
        await transaction.CommitAsync();
        return new(count, count == 0 ? "没有可清理的任务。" : $"已清理 {count} 个任务。");
    }
    public async Task<BatchTaskMutationResult> CancelBatchAsync(IReadOnlyList<long> ids) {
        long[] values=ids.Distinct().Where(id=>id>0).ToArray();
        if(values.Length is 0 or >500) throw new ArgumentException("请选择 1 到 500 个任务。");
        int cancelled=0;
        foreach(long id in values){
            TaskSnapshot snapshot=await SnapshotAsync(id);
            if(TerminalStatuses.Contains(snapshot.Status)) continue;
            await CancelAsync(id);
            cancelled++;
        }
        return new(cancelled,cancelled==0?"没有可取消的执行中任务。":$"已取消 {cancelled} 个任务。");
    }
    public async Task<BatchTaskMutationResult> CancelSyncBatchAsync(IReadOnlyList<long> ids) { long[] values=ids.Distinct().Where(id=>id>0).ToArray(); if(values.Length is 0 or >500) throw new ArgumentException("Select 1 to 500 sync tasks."); foreach(long id in values){if(await TypeAsync(id)!="Sync")throw new ArgumentException("Only sync tasks can be cancelled in batch."); await sync.CancelAsync(id);} return new(values.Length,$"Cancelled {values.Length} sync tasks."); }
    private async Task<string> TypeAsync(long id){await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync();await using var x=c.CreateCommand();x.CommandText="SELECT TaskType FROM Tasks WHERE Id=$id";x.Parameters.AddWithValue("$id",id);return(await x.ExecuteScalarAsync())?.ToString()??throw new KeyNotFoundException("任务不存在。");}
    private async Task<TaskSnapshot> SnapshotAsync(long id){await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync();await using var x=c.CreateCommand();x.CommandText="SELECT TaskType,Status FROM Tasks WHERE Id=$id";x.Parameters.AddWithValue("$id",id);await using var r=await x.ExecuteReaderAsync();if(!await r.ReadAsync())throw new KeyNotFoundException("任务不存在。");return new(r.GetString(0),r.GetString(1));}
    private async Task<SqliteConnection> OpenWriteAsync(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadWrite}.ToString());await c.OpenAsync();return c;}
    private static async Task ExecuteAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> ScalarLongAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private sealed record TaskSnapshot(string Type, string Status);
}

public sealed record TaskCleanupCommand(string? Status);
public sealed record TaskCleanupResult(long Count, string Message);
