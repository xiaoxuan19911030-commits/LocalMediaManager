using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace LocalMediaManager.Bridge;

public sealed record ImageValidationResult(bool Valid, string Status, string? ContentType, int Width, int Height,
    long FileSize, string? Sha256, string? Error);

public sealed record ImageAssetDto(long Id, string Type, string? Url, string Ownership, bool Locked, bool Derived,
    bool Primary, string ValidationStatus, int Width, int Height, long FileSize, string? Provider, string? DownloadedAt, string? Directory);

public sealed record ImageAssetContent(string Path, string ContentType);
public sealed record ImageAssetStatusDto(long Id, string Type, string Status, string CacheStatus, string? Url, string? Message);
public sealed record ImageCenterStatusDto(long MovieId, long TotalImages, long NormalImages, long MissingImages,
    long InvalidCacheEntries, long FailedImages, IReadOnlyList<ImageAssetStatusDto> Assets);
public sealed record ImageLockCommand(bool Locked);
public sealed record ImageMutationResult(bool Changed, string Message);
public sealed record ImageCachePreview(long Entries, long ExistingEntries, long MissingEntries, long Bytes,
    string ConfirmationToken, IReadOnlyList<string> Warnings);
public sealed record ImageCacheCleanupCommand(string ConfirmationToken);
public sealed record ImageCacheCleanupResult(long DeletedEntries, long DeletedBytes, long FailedEntries, string Message);
public sealed record ImageCacheRebuildLaunchResult(long TaskId, string Status, long TotalItems, string Message);

public static class ImageFileValidator
{
    private const long MaximumBytes = 64L * 1024 * 1024;
    private static readonly HashSet<string> AllowedDeclaredTypes = new(StringComparer.OrdinalIgnoreCase) {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    public static async Task<ImageValidationResult> ValidateAsync(string path, string? declaredContentType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new(false, "Missing", null, 0, 0, 0, null, "图片文件不存在。");

        var file = new FileInfo(path);
        if (file.Length < 32)
            return new(false, "Corrupt", null, 0, 0, file.Length, null, "图片文件过小。");
        if (file.Length > MaximumBytes)
            return new(false, "Unsupported", null, 0, 0, file.Length, null, "图片超过 64 MB 安全限制。");

        string? normalizedDeclared = declaredContentType?.Split(';', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(normalizedDeclared) && !AllowedDeclaredTypes.Contains(normalizedDeclared))
            return new(false, "Unsupported", normalizedDeclared, 0, 0, file.Length, null,
                $"服务器返回的 MIME 不是图片：{normalizedDeclared}");

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SKCodec? codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            return new(false, "Corrupt", normalizedDeclared, 0, 0, file.Length, null,
                "文件头无法识别为有效图片，可能是 HTML 错误页或损坏文件。");

        string? detected = codec.EncodedFormat switch {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            SKEncodedImageFormat.Gif => "image/gif",
            _ => null,
        };
        if (detected is null)
            return new(false, "Unsupported", normalizedDeclared, codec.Info.Width, codec.Info.Height, file.Length, null,
                $"不支持的图片格式：{codec.EncodedFormat}");
        if (!string.IsNullOrWhiteSpace(normalizedDeclared) && !string.Equals(normalizedDeclared, detected, StringComparison.OrdinalIgnoreCase))
            return new(false, "Corrupt", normalizedDeclared, codec.Info.Width, codec.Info.Height, file.Length, null,
                $"MIME 与文件头不一致：{normalizedDeclared} / {detected}");

        stream.Position = 0;
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return new(true, "Valid", detected, codec.Info.Width, codec.Info.Height, file.Length, hash, null);
    }
}

public sealed class ImageAssetService(string databasePath, string imageRoot)
{
    private string CacheRoot => Path.GetFullPath(Path.Combine(imageRoot, ".lmm-cache", "thumbnails"));

    public async Task<IReadOnlyList<ImageAssetDto>> ReadMovieAssetsAsync(long movieId, string bridgeUrl,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,ImageType,Ownership,IsLocked,IsDerived,IsPrimary,ValidationStatus,
                   COALESCE(Width,0),COALESCE(Height,0),COALESCE(FileSize,0),SourceProvider,DownloadedAt
                   ,FilePath
              FROM Images WHERE MovieId=$movie ORDER BY IsLocked DESC,IsPrimary DESC,Id
            """;
        command.Parameters.AddWithValue("$movie", movieId);
        var items = new List<ImageAssetDto>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            long id = reader.GetInt64(0);
            items.Add(new(id, reader.GetString(1), $"{bridgeUrl}/api/image-assets/{id}/content",
                reader.GetString(2), reader.GetInt64(3) == 1, reader.GetInt64(4) == 1, reader.GetInt64(5) == 1,
                reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : Path.GetDirectoryName(reader.GetString(12))));
        }
        return items;
    }

    public async Task<ImageCenterStatusDto> ReadMovieStatusAsync(long movieId, string bridgeUrl,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        var assets = new List<ImageAssetStatusDto>();
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT Id,ImageType,FilePath,ValidationStatus
                  FROM Images WHERE MovieId=$movie ORDER BY IsLocked DESC,IsPrimary DESC,Id
                """;
            command.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) {
                long id = reader.GetInt64(0);
                string type = reader.GetString(1);
                string? path = reader.IsDBNull(2) ? null : reader.GetString(2);
                string validation = reader.GetString(3);
                bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                string status = !exists ? "Missing" : validation is "Corrupt" or "Unsupported" ? "Failed" : "Normal";
                assets.Add(new(id, type, status, "Unknown", $"{bridgeUrl}/api/image-assets/{id}/content",
                    status == "Normal" ? null : exists ? validation : "图片文件缺失"));
            }
        }
        long invalidCache = 0;
        await using (SqliteCommand cache = connection.CreateCommand()) {
            cache.CommandText = "SELECT CachePath FROM ImageCacheEntries WHERE MovieId=$movie";
            cache.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await cache.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) {
                string path = reader.GetString(0);
                if (!IsInsideCache(path) || !File.Exists(path)) invalidCache++;
            }
        }
        string cacheStatus = invalidCache > 0 ? "Invalid" : "Valid";
        assets = assets.Select(item => item with { CacheStatus = item.Type is "Poster" or "Thumbnail" or "GeneratedCard" ? cacheStatus : "NotCached" }).ToList();
        return new(movieId, assets.Count, assets.LongCount(item => item.Status == "Normal"),
            assets.LongCount(item => item.Status == "Missing"), invalidCache,
            assets.LongCount(item => item.Status == "Failed"), assets);
    }

    public async Task<ImageAssetContent?> ResolveMovieAsync(long movieId, string variant,
        CancellationToken cancellationToken = default)
    {
        string normalized = variant.Equals("thumbnail", StringComparison.OrdinalIgnoreCase) ? "thumbnail" : "original";
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        if (normalized == "thumbnail") {
            ImageAssetContent? cached = await ReadCachedAsync(connection, movieId, cancellationToken);
            if (cached is not null) return cached;
        }

        (long Id, string Path, string? ContentType)? source = await ReadBestSourceAsync(connection, movieId, normalized, cancellationToken);
        if (source is null) return null;
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(source.Value.Path, null, cancellationToken);
        await UpdateValidationAsync(connection, source.Value.Id, validation, cancellationToken);
        if (!validation.Valid) return null;
        if (normalized == "original") return new(source.Value.Path, validation.ContentType!);
        return await CreateThumbnailAsync(connection, movieId, source.Value.Id, source.Value.Path, validation, cancellationToken);
    }

    public async Task<ImageAssetContent?> ResolveAssetAsync(long imageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT FilePath FROM Images WHERE Id=$id";
        command.Parameters.AddWithValue("$id", imageId);
        string? path = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        if (string.IsNullOrWhiteSpace(path)) return null;
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, null, cancellationToken);
        await UpdateValidationAsync(connection, imageId, validation, cancellationToken);
        return validation.Valid ? new(path, validation.ContentType!) : null;
    }

    public async Task<ImageAssetContent?> ResolveActorAsync(long actorId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,FilePath FROM Images WHERE ActorId=$id AND FilePath IS NOT NULL ORDER BY IsLocked DESC,IsPrimary DESC,Id LIMIT 1";
        command.Parameters.AddWithValue("$id", actorId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        long imageId = reader.GetInt64(0); string path = reader.GetString(1);
        await reader.DisposeAsync();
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, null, cancellationToken);
        await UpdateValidationAsync(connection, imageId, validation, cancellationToken);
        return validation.Valid ? new(path, validation.ContentType!) : null;
    }

    public async Task<ImageMutationResult> SetLockAsync(long imageId, bool locked, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Images SET IsLocked=$locked,Ownership=CASE WHEN $locked=1 THEN 'User' ELSE Ownership END,UpdatedAt=$at WHERE Id=$id";
        command.Parameters.AddWithValue("$locked", locked ? 1 : 0);
        command.Parameters.AddWithValue("$at", Now());
        command.Parameters.AddWithValue("$id", imageId);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0) throw new KeyNotFoundException("图片不存在。");
        return new(true, locked ? "已锁定用户图片，自动同步和缓存重建不会覆盖。" : "已解除图片锁定。");
    }

    public async Task<ImageCachePreview> PreviewCacheCleanupAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        var rows = new List<(long Id, string Path, long Size)>();
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "SELECT Id,CachePath,FileSize FROM ImageCacheEntries ORDER BY Id";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
        }
        long existing = rows.LongCount(row => IsInsideCache(row.Path) && File.Exists(row.Path));
        long bytes = rows.Where(row => IsInsideCache(row.Path) && File.Exists(row.Path)).Sum(row => new FileInfo(row.Path).Length);
        string token = CacheToken(rows);
        return new(rows.Count, existing, rows.Count - existing, bytes, token,
            ["仅删除 .lmm-cache 中可重建的缩略缓存，不删除源图、用户图片或智能卡图。"]);
    }

    public async Task<ImageCacheCleanupResult> CleanupCacheAsync(string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        ImageCachePreview preview = await PreviewCacheCleanupAsync(cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(preview.ConfirmationToken), Encoding.UTF8.GetBytes(confirmationToken ?? "")))
            throw new UnauthorizedAccessException("缓存内容已变化，请重新预览后确认。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        var rows = new List<(long Id, string Path)>();
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "SELECT Id,CachePath FROM ImageCacheEntries ORDER BY Id";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }
        long deleted = 0, bytes = 0, failed = 0;
        foreach ((long id, string path) in rows) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                if (!IsInsideCache(path)) throw new InvalidOperationException("缓存记录超出受控目录。");
                if (File.Exists(path)) { bytes += new FileInfo(path).Length; File.Delete(path); }
                await DeleteCacheRowAsync(connection, id, cancellationToken); deleted++;
            } catch { failed++; }
        }
        return new(deleted, bytes, failed, failed == 0 ? "派生图片缓存已清理。" : "部分缓存未能清理，源图片未受影响。");
    }

    public async Task<IReadOnlyList<long>> ReadThumbnailCandidatesAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT MovieId FROM Images WHERE MovieId IS NOT NULL AND IsDerived=0 AND FilePath IS NOT NULL ORDER BY MovieId";
        var result = new List<long>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task<bool> RebuildThumbnailAsync(long movieId, CancellationToken cancellationToken = default)
    {
        await using (SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken)) {
            var rows = new List<(long Id, string Path)>();
            await using (SqliteCommand command = connection.CreateCommand()) {
                command.CommandText = "SELECT Id,CachePath FROM ImageCacheEntries WHERE MovieId=$movie AND CacheKind='CardThumbnail'";
                command.Parameters.AddWithValue("$movie", movieId);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            foreach ((long id, string path) in rows) {
                if (IsInsideCache(path) && File.Exists(path)) File.Delete(path);
                await DeleteCacheRowAsync(connection, id, cancellationToken);
            }
        }
        return await ResolveMovieAsync(movieId, "thumbnail", cancellationToken) is not null;
    }

    public async Task<long> ImportLegacyActorAssetsAsync(CancellationToken cancellationToken = default)
    {
        string folder = Path.Combine(imageRoot, "Actresses");
        if (!Directory.Exists(folder)) return 0;
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), path => path, StringComparer.OrdinalIgnoreCase);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand query = connection.CreateCommand();
        query.CommandText = "SELECT Id,Name,LegacyId FROM Actors ORDER BY Id";
        var actors = new List<(long Id, string Name, long? LegacyId)>();
        await using (SqliteDataReader reader = await query.ExecuteReaderAsync(cancellationToken)) {
            while (await reader.ReadAsync(cancellationToken))
                actors.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }
        long imported = 0;
        foreach ((long actorId, string name, long? legacyId) in actors) {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = null;
            if (legacyId.HasValue) files.TryGetValue($"{legacyId.Value}_{name}", out path);
            if (path is null) files.TryGetValue(name, out path);
            if (path is null) continue;
            ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, null, cancellationToken);
            if (!validation.Valid) continue;
            await using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT OR IGNORE INTO Images(MovieId,ActorId,ImageType,FilePath,Width,Height,FileSize,FileHash,IsPrimary,SourceProvider,Ownership,IsLocked,IsDerived,ContentType,ValidationStatus,ValidatedAt,CreatedAt,UpdatedAt)
                VALUES(NULL,$actor,'ActorAvatar',$path,$width,$height,$size,$hash,1,'LegacyFile','Legacy',0,0,$content,'Valid',$at,$at,$at)
                """;
            insert.Parameters.AddWithValue("$actor", actorId); insert.Parameters.AddWithValue("$path", path);
            insert.Parameters.AddWithValue("$width", validation.Width); insert.Parameters.AddWithValue("$height", validation.Height);
            insert.Parameters.AddWithValue("$size", validation.FileSize); insert.Parameters.AddWithValue("$hash", validation.Sha256 ?? "");
            insert.Parameters.AddWithValue("$content", validation.ContentType ?? "application/octet-stream"); insert.Parameters.AddWithValue("$at", Now());
            imported += await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        return imported;
    }

    private async Task<ImageAssetContent?> ReadCachedAsync(SqliteConnection connection, long movieId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,CachePath FROM ImageCacheEntries WHERE MovieId=$movie AND CacheKind='CardThumbnail' ORDER BY Id DESC LIMIT 1";
        command.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        long id = reader.GetInt64(0); string path = reader.GetString(1);
        await reader.DisposeAsync();
        if (!IsInsideCache(path) || !File.Exists(path)) { await DeleteCacheRowAsync(connection, id, token); return null; }
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, null, token);
        if (!validation.Valid) { try { File.Delete(path); } catch { } await DeleteCacheRowAsync(connection, id, token); return null; }
        await using SqliteCommand touch = connection.CreateCommand();
        touch.CommandText = "UPDATE ImageCacheEntries SET LastAccessedAt=$at WHERE Id=$id";
        touch.Parameters.AddWithValue("$at", Now()); touch.Parameters.AddWithValue("$id", id);
        await touch.ExecuteNonQueryAsync(token);
        return new(path, validation.ContentType!);
    }

    private static async Task<(long Id, string Path, string? ContentType)?> ReadBestSourceAsync(SqliteConnection connection,
        long movieId, string variant, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,FilePath,ContentType FROM Images
             WHERE MovieId=$movie AND FilePath IS NOT NULL AND IsDerived=0
             ORDER BY IsLocked DESC,IsPrimary DESC,
               CASE WHEN $variant='thumbnail' THEN
                 CASE ImageType WHEN 'Thumbnail' THEN 0 WHEN 'GeneratedCard' THEN 1 WHEN 'Poster' THEN 2 WHEN 'Fanart' THEN 3 ELSE 9 END
               ELSE
                 CASE ImageType WHEN 'BigPic' THEN 0 WHEN 'Fanart' THEN 1 WHEN 'Poster' THEN 2 WHEN 'GeneratedCard' THEN 3 ELSE 9 END
               END, Id LIMIT 1
            """;
        command.Parameters.AddWithValue("$movie", movieId); command.Parameters.AddWithValue("$variant", variant);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return (reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<ImageAssetContent> CreateThumbnailAsync(SqliteConnection connection, long movieId, long sourceId,
        string sourcePath, ImageValidationResult source, CancellationToken token)
    {
        Directory.CreateDirectory(CacheRoot);
        string hash = source.Sha256 ?? sourceId.ToString();
        string target = Path.Combine(CacheRoot, $"{movieId}-{hash[..Math.Min(hash.Length, 16)]}-w360.jpg");
        if (!File.Exists(target)) {
            string temporary = target + $".{Guid.NewGuid():N}.part";
            try {
                using SKBitmap? bitmap = SKBitmap.Decode(sourcePath);
                if (bitmap is null) throw new InvalidDataException("源图无法解码。");
                int width = Math.Min(360, bitmap.Width);
                int height = Math.Max(1, (int)Math.Round(bitmap.Height * (width / (double)bitmap.Width)));
                using var resized = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(resized)) {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
                }
                using SKImage image = SKImage.FromBitmap(resized);
                using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    data.SaveTo(output);
                    await output.FlushAsync(token);
                }
                ImageValidationResult validation = await ImageFileValidator.ValidateAsync(temporary, "image/jpeg", token);
                if (!validation.Valid) throw new InvalidDataException(validation.Error);
                File.Move(temporary, target, false);
            } catch {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                throw;
            }
        }
        ImageValidationResult result = await ImageFileValidator.ValidateAsync(target, "image/jpeg", token);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO ImageCacheEntries(MovieId,ActorId,SourceImagePath,CachePath,CacheKind,Width,Height,FileSize,FileHash,CreatedAt,LastAccessedAt)
            VALUES($movie,NULL,$source,$path,'CardThumbnail',$width,$height,$size,$hash,$at,$at)
            ON CONFLICT(CachePath) DO UPDATE SET LastAccessedAt=excluded.LastAccessedAt
            """;
        insert.Parameters.AddWithValue("$movie", movieId); insert.Parameters.AddWithValue("$source", sourcePath);
        insert.Parameters.AddWithValue("$path", target); insert.Parameters.AddWithValue("$width", result.Width);
        insert.Parameters.AddWithValue("$height", result.Height); insert.Parameters.AddWithValue("$size", result.FileSize);
        insert.Parameters.AddWithValue("$hash", result.Sha256 ?? ""); insert.Parameters.AddWithValue("$at", Now());
        await insert.ExecuteNonQueryAsync(token);
        return new(target, "image/jpeg");
    }

    private static async Task UpdateValidationAsync(SqliteConnection connection, long id, ImageValidationResult result, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Images SET Width=$width,Height=$height,FileSize=$size,FileHash=$hash,ContentType=$content,
                   ValidationStatus=$status,ValidatedAt=$at,LastAccessedAt=CASE WHEN $valid=1 THEN $at ELSE LastAccessedAt END,UpdatedAt=$at
             WHERE Id=$id
            """;
        command.Parameters.AddWithValue("$width", result.Width); command.Parameters.AddWithValue("$height", result.Height);
        command.Parameters.AddWithValue("$size", result.FileSize); command.Parameters.AddWithValue("$hash", (object?)result.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$content", (object?)result.ContentType ?? DBNull.Value); command.Parameters.AddWithValue("$status", result.Status);
        command.Parameters.AddWithValue("$valid", result.Valid ? 1 : 0); command.Parameters.AddWithValue("$at", Now()); command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(token);
    }

    private bool IsInsideCache(string path) {
        string full = Path.GetFullPath(path);
        return full.StartsWith(CacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private static string CacheToken(IEnumerable<(long Id, string Path, long Size)> rows)
    {
        string state = string.Join('|', rows.Select(row => {
            var file = new FileInfo(row.Path);
            return $"{row.Id}:{Path.GetFullPath(row.Path)}:{row.Size}:{file.Exists}:{(file.Exists ? file.Length : 0)}:{(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)}";
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lmm-image-cache|" + state))).ToLowerInvariant();
    }
    private static async Task DeleteCacheRowAsync(SqliteConnection connection, long id, CancellationToken token) { await using SqliteCommand command=connection.CreateCommand();command.CommandText="DELETE FROM ImageCacheEntries WHERE Id=$id";command.Parameters.AddWithValue("$id",id);await command.ExecuteNonQueryAsync(token); }
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token) { var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=mode,Cache=SqliteCacheMode.Private}.ToString());await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";await command.ExecuteNonQueryAsync(token);return connection; }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
}
