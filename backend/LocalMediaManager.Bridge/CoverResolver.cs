using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record ResolvedCover(string Path, string? ContentType, string Source, long? ImageId);

// Keeps the display-only fallback separate from every formal poster workflow.
public sealed class CoverResolver(string databasePath)
{
    public async Task<ResolvedCover?> ResolveAsync(long movieId, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(token);
        await using (SqliteCommand manualCrop = connection.CreateCommand()) {
            manualCrop.CommandText = "SELECT Id,FilePath,ContentType FROM Images WHERE MovieId=$movie AND ImageType='GeneratedCard' AND SourceProvider='ManualCrop' AND FilePath IS NOT NULL ORDER BY IsPrimary DESC,Id DESC";
            manualCrop.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await manualCrop.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) {
                ImageValidationResult validation = await ImageFileValidator.ValidateAsync(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), token);
                if (validation.Valid) return new(reader.GetString(1), validation.ContentType, "Manual", reader.GetInt64(0));
            }
        }
        await using (SqliteCommand posters = connection.CreateCommand()) {
            posters.CommandText = """
                SELECT Id,FilePath,ContentType,Ownership,SourceProvider
                  FROM Images
                 WHERE MovieId=$movie AND ImageType='Poster' AND FilePath IS NOT NULL
                   AND COALESCE(SourceProvider,'')<>'LegacyFile'
                 ORDER BY CASE WHEN IsLocked=1 OR Ownership='User' THEN 0
                               WHEN upper(COALESCE(SourceProvider,''))='NFO' THEN 2 ELSE 1 END,
                          IsLocked DESC,IsPrimary DESC,Id DESC
                """;
            posters.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await posters.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) {
                string path = reader.GetString(1);
                ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, reader.IsDBNull(2) ? null : reader.GetString(2), token);
                if (!validation.Valid) continue;
                string sourceLabel = reader.GetInt64(3) == 1 || string.Equals(reader.GetString(3), "User", StringComparison.OrdinalIgnoreCase)
                    ? "Manual" : string.Equals(reader.IsDBNull(4) ? "" : reader.GetString(4), "NFO", StringComparison.OrdinalIgnoreCase) ? "NFO" : "Provider";
                return new(path, validation.ContentType, sourceLabel, reader.GetInt64(0));
            }
        }

        await using SqliteCommand generated = connection.CreateCommand();
        generated.CommandText = "SELECT SourceVideoPath,SourceVideoSize,SourceVideoModifiedTime,GeneratedCoverPath FROM GeneratedCoverInfo WHERE MovieId=$movie";
        generated.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader generatedReader = await generated.ExecuteReaderAsync(token);
        if (!await generatedReader.ReadAsync(token)) return null;
        string video = generatedReader.GetString(0), cover = generatedReader.GetString(3);
        FileInfo videoInfo = new(video);
        if (!videoInfo.Exists || videoInfo.Length != generatedReader.GetInt64(1)
            || videoInfo.LastWriteTimeUtc.Ticks.ToString() != generatedReader.GetString(2)) return null;
        ImageValidationResult generatedValidation = await ImageFileValidator.ValidateAsync(cover, null, token);
        return generatedValidation.Valid ? new(cover, generatedValidation.ContentType, "Generated", null) : null;
    }

    public async Task<bool> HasFormalPosterAsync(long movieId, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT FilePath FROM Images WHERE MovieId=$movie AND ImageType='Poster' AND FilePath IS NOT NULL AND COALESCE(SourceProvider,'')<>'LegacyFile'";
        command.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) if (File.Exists(reader.GetString(0))) return true;
        return false;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
}
