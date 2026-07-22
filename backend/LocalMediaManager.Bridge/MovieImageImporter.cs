using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MovieImageImportResult(PreparedFiles PreparedFiles, int MovieImagesDownloaded,
    int ActorImagesDownloaded, IReadOnlyList<string> Warnings);

public sealed class MovieImageImporter(
    string databasePath,
    string imageRoot,
    MediaStoragePathResolver pathResolver,
    ImageDownloadService downloader,
    IHttpClientFactory clients)
{
    public async Task<MovieImageImportResult> PrepareAsync(SyncMovie movie, MovieMetadata metadata,
        int timeoutSeconds, bool overwrite, CancellationToken cancellationToken)
    {
        var savedImages = new List<SavedImage>();
        var createdPaths = new List<string>();
        var warnings = new List<string>();
        IReadOnlyList<MetadataImage> images = MovieImages(metadata);
        if (images.Count > 0) {
            try {
                IReadOnlyList<SavedImage> saved = await downloader.DownloadAsync(pathResolver,
                    new(movie.Id, movie.Code, movie.Title), images, timeoutSeconds, overwrite, cancellationToken);
                savedImages.AddRange(saved);
                createdPaths.AddRange(saved.Where(value => value.Created).Select(value => value.Path));
            } catch (Exception error) when (error is not OperationCanceledException) {
                warnings.Add($"MovieImages: {error.Message}");
            }
        }

        return new(new(savedImages, null, createdPaths), savedImages.Count(value => value.Created), 0, warnings);
    }

    public async Task<MovieImageImportResult> ImportActorImagesAsync(MovieMetadata metadata,
        int timeoutSeconds, bool overwrite, CancellationToken cancellationToken)
    {
        return await ImportActorImagesAsync(metadata.ActorImages, metadata.Provider, timeoutSeconds, overwrite, cancellationToken);
    }

    public async Task<MovieImageImportResult> ImportActorImagesAsync(IReadOnlyList<ActorImageMetadata>? actors, string provider,
        int timeoutSeconds, bool overwrite, CancellationToken cancellationToken)
    {
        int actorImages = 0;
        var warnings = new List<string>();
        foreach (ActorImageMetadata actor in actors ?? []) {
            try {
                if (await ImportActorImageAsync(actor, provider, timeoutSeconds, overwrite, cancellationToken))
                    actorImages++;
            } catch (Exception error) when (error is not OperationCanceledException) {
                warnings.Add($"ActorAvatar {actor.Name}: {error.Message}");
            }
        }
        return new(new([], null, []), 0, actorImages, warnings);
    }

    private static IReadOnlyList<MetadataImage> MovieImages(MovieMetadata metadata)
    {
        var result = new List<MetadataImage>();
        string? poster = First(metadata.Poster, metadata.Thumb);
        if (!string.IsNullOrWhiteSpace(poster)) result.Add(new("Poster", poster));
        if (!string.IsNullOrWhiteSpace(metadata.Fanart)) result.Add(new("Fanart", metadata.Fanart));
        foreach (string image in metadata.ExtraFanart)
            if (!string.IsNullOrWhiteSpace(image))
                result.Add(new("Preview", image));
        return result;
    }

    private async Task<bool> ImportActorImageAsync(ActorImageMetadata actor, string provider, int timeoutSeconds,
        bool overwrite, CancellationToken cancellationToken)
    {
        long actorId = await FindActorIdAsync(actor.Name, cancellationToken);
        if (actorId <= 0) return false;
        if (!overwrite && await HasActiveActorImageAsync(actorId, cancellationToken)) return false;

        using HttpClient client = clients.CreateClient("MetadataImages");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 180));
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.7.1"));
        if (Uri.TryCreate(actor.ImageUrl, UriKind.Absolute, out Uri? actorUri))
            client.DefaultRequestHeaders.Referrer = new Uri(actorUri.GetLeftPart(UriPartial.Authority) + "/");
        using HttpResponseMessage response = await client.GetAsync(actor.ImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        string? declaredType = response.Content.Headers.ContentType?.MediaType;

        string directory = Path.Combine(imageRoot, "Actresses");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.part");
        try {
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream target = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(target, cancellationToken);
            ImageValidationResult validation = await ImageFileValidator.ValidateAsync(temporary, declaredType, cancellationToken);
            if (!validation.Valid) throw new InvalidDataException(validation.Error ?? "Actor image validation failed.");
            string path = Path.Combine(directory, $"{actorId}_{SafeFileName(actor.Name)}{Extension(validation.ContentType)}");
            if (File.Exists(path) && overwrite) File.Delete(path);
            if (!File.Exists(path)) File.Move(temporary, path, false);
            else File.Delete(temporary);
            await RegisterActorImageAsync(actorId, path, actor.ImageUrl, provider, validation, cancellationToken);
            return true;
        } catch {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw;
        }
    }

    private async Task<long> FindActorIdAsync(string name, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id),0) FROM Actors WHERE NormalizedName=$name";
        command.Parameters.AddWithValue("$name", Normalize(name));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<bool> HasActiveActorImageAsync(long actorId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Images WHERE ActorId=$actor AND ImageType='ActorAvatar' AND COALESCE(SourceProvider,'')<>'LegacyFile' AND FilePath IS NOT NULL";
        command.Parameters.AddWithValue("$actor", actorId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
    }

    private async Task RegisterActorImageAsync(long actorId, string path, string sourceUrl, string provider,
        ImageValidationResult validation, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Images(MovieId,ActorId,ImageType,FilePath,SourceUrl,Width,Height,FileSize,FileHash,
                IsPrimary,SourceProvider,DownloadedAt,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ContentType,ValidationStatus,ValidatedAt)
            VALUES(NULL,$actor,'ActorAvatar',$path,$url,$width,$height,$size,$hash,1,$provider,$at,$at,$at,
                'Provider',0,0,$content,'Valid',$at)
            """;
        command.Parameters.AddWithValue("$actor", actorId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$url", sourceUrl);
        command.Parameters.AddWithValue("$width", validation.Width);
        command.Parameters.AddWithValue("$height", validation.Height);
        command.Parameters.AddWithValue("$size", validation.FileSize);
        command.Parameters.AddWithValue("$hash", validation.Sha256 ?? "");
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$at", at);
        command.Parameters.AddWithValue("$content", validation.ContentType ?? "application/octet-stream");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode }.ToString());
        await connection.OpenAsync(cancellationToken);
        if (mode == SqliteOpenMode.ReadWrite) {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return connection;
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string SafeFileName(string value)
    {
        string clean = string.Concat((value ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "actor" : clean;
    }
    private static string Extension(string? contentType) => contentType switch {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };
}
