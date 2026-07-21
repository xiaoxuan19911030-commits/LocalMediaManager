using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MovieMetadataImportResult(long TaskId, string AppliedJson);

public sealed class MovieMetadataImporter(string databasePath, MetadataWriteService writer)
{
    public async Task<SyncMovie> ReadMovieAsync(long movieId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.Id,COALESCE(m.Code,''),m.Title,m.Description,m.ReleaseDate,m.DurationSeconds,m.NfoPath,
                   (SELECT FilePath FROM MediaFiles WHERE MovieId=m.Id AND IsPrimary=1 AND MediaType='Video' ORDER BY Id LIMIT 1)
            FROM Movies m WHERE m.Id=$movie
            """;
        command.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("影片不存在。");
        return new(reader.GetInt64(0), reader.GetString(1), Text(reader, 2), Text(reader, 3), Text(reader, 4),
            reader.GetInt32(5), Text(reader, 7), Text(reader, 6));
    }

    public async Task<MovieMetadataImportResult> ImportAsync(long movieId, MovieMetadata metadata, bool overwrite,
        CancellationToken cancellationToken)
    {
        SyncMovie movie = await ReadMovieAsync(movieId, cancellationToken);
        long taskId = await CreateTaskAsync(movieId, metadata, overwrite, cancellationToken);
        try {
            ProviderMetadata providerMetadata = ToProviderMetadata(metadata);
            string applied = await writer.ApplyAsync(taskId, movie, providerMetadata, new([], null, []), overwrite, cancellationToken);
            await CompleteTaskAsync(taskId, applied, cancellationToken);
            return new(taskId, applied);
        } catch (Exception error) when (error is not OperationCanceledException) {
            await FailTaskAsync(taskId, error.Message, cancellationToken);
            throw;
        }
    }

    private static ProviderMetadata ToProviderMetadata(MovieMetadata metadata)
    {
        var images = new List<MetadataImage>();
        AddImage(images, "Poster", metadata.Poster);
        AddImage(images, "Thumb", metadata.Thumb);
        AddImage(images, "Fanart", metadata.Fanart);
        foreach (string image in metadata.ExtraFanart) AddImage(images, "Preview", image);
        AddImage(images, "Trailer", metadata.Trailer);
        return new(metadata.Provider, metadata.ExternalId ?? metadata.Code, metadata.Code, metadata.Title,
            metadata.Description, metadata.Director, metadata.Studio, null, metadata.Series,
            metadata.DurationSeconds, metadata.ReleaseDate, null, metadata.Tags, metadata.Actors,
            images, metadata.Rating, metadata.OriginalTitle, metadata.Country);
    }

    private async Task<long> CreateTaskAsync(long movieId, MovieMetadata metadata, bool overwrite,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks(TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId)
            VALUES('Sync','WritingMetadata','WritingMetadata',$provider,82,1,0,$payload,$at,$at,$movie);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$provider", metadata.Provider);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(new { MovieId = movieId, Trigger = "MetadataSyncService", Overwrite = overwrite, Source = metadata.Provider }));
        command.Parameters.AddWithValue("$at", Now());
        command.Parameters.AddWithValue("$movie", movieId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CompleteTaskAsync(long taskId, string applied, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await ExecuteAsync(connection, """
            UPDATE Tasks SET Status='Completed',Stage='Completed',Progress=100,CompletedItems=1,
              ResultJson=$result,ResultSummary='元数据同步完成',ErrorMessage=NULL,CompletedAt=$at,UpdatedAt=$at
            WHERE Id=$id
            """, cancellationToken, ("$result", applied), ("$at", Now()), ("$id", taskId));
    }

    private async Task FailTaskAsync(long taskId, string message, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await ExecuteAsync(connection, """
            UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,
              ResultSummary='元数据同步失败',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id
            """, cancellationToken, ("$error", message), ("$at", Now()), ("$id", taskId));
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode }.ToString());
        await connection.OpenAsync(cancellationToken);
        if (mode == SqliteOpenMode.ReadWrite)
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", cancellationToken);
        return connection;
    }

    private static void AddImage(List<MetadataImage> images, string type, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url)) images.Add(new(type, url.Trim()));
    }

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken,
        params (string, object?)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
