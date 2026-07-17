using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed class RatingHistoryService(string databasePath)
{
    public async Task<RatingHistorySettingsDto> ReadSettingsAsync(CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        return new(await EnabledAsync(connection, token), await DeleteOnClearAsync(connection, token));
    }

    public async Task<RatingHistorySettingsDto> SaveSettingsAsync(RatingHistorySettingsDto input, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await StoreSettingAsync(connection, "ratingHistory.enabled", input.Enabled, token);
        await StoreSettingAsync(connection, "ratingHistory.deleteOnClear", input.DeleteOnClear, token);
        return input;
    }

    public async Task RememberAsync(long movieId, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        if (!await EnabledAsync(connection, token)) return;
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(m.Code,''), COALESCE(s.UserRating,0), COALESCE(s.HasUserRating,0)
            FROM Movies m JOIN UserMovieState s ON s.MovieId=m.Id
            WHERE m.Id=$movie
            """;
        command.Parameters.AddWithValue("$movie", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return;
        string? normalized = MovieCodeNormalizer.Normalize(reader.GetString(0));
        double rating = reader.GetDouble(1);
        bool hasRating = reader.GetInt64(2) == 1 && rating > 0;
        await reader.DisposeAsync();
        if (!hasRating || normalized is null) return;
        await StoreAsync(connection, normalized, rating, token);
    }

    public async Task RememberExplicitRatingAsync(long movieId, double rating, CancellationToken token = default)
    {
        if (rating <= 0) return;
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        if (!await EnabledAsync(connection, token)) return;
        string? code = await ScalarTextAsync(connection, "SELECT Code FROM Movies WHERE Id=$movie", token, ("$movie", movieId));
        string? normalized = MovieCodeNormalizer.Normalize(code);
        if (normalized is null) return;
        await StoreAsync(connection, normalized, rating, token);
    }

    public async Task MaybeDeleteOnClearAsync(long movieId, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        if (!await DeleteOnClearAsync(connection, token)) return;
        string? normalized = MovieCodeNormalizer.Normalize(await ScalarTextAsync(connection, "SELECT Code FROM Movies WHERE Id=$movie", token, ("$movie", movieId)));
        if (normalized is null) return;
        await ExecuteAsync(connection, "DELETE FROM DeletedMovieRatings WHERE NormalizedMovieCode=$code", token, ("$code", normalized));
    }

    public static async Task<bool> RestoreForImportedMovieAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        long movieId, string code, string at)
    {
        if (!await EnabledAsync(connection, transaction)) return false;
        string? normalized = MovieCodeNormalizer.Normalize(code);
        if (normalized is null) return false;
        double? rating = await ScalarNullableDoubleAsync(connection, transaction,
            "SELECT Rating FROM DeletedMovieRatings WHERE NormalizedMovieCode=$code", ("$code", normalized));
        if (rating is null or <= 0) return false;
        long hasRating = await ScalarLongAsync(connection, transaction,
            "SELECT COALESCE(HasUserRating,0) FROM UserMovieState WHERE MovieId=$movie", ("$movie", movieId));
        if (hasRating == 1) return false;
        await ExecuteAsync(connection, transaction, """
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating)
            VALUES($movie,0,$rating,0,0,$at,1)
            ON CONFLICT(MovieId) DO UPDATE SET
                UserRating=CASE WHEN HasUserRating=0 THEN $rating ELSE UserRating END,
                HasUserRating=CASE WHEN HasUserRating=0 THEN 1 ELSE HasUserRating END,
                UpdatedAt=CASE WHEN HasUserRating=0 THEN $at ELSE UpdatedAt END
            """, ("$movie", movieId), ("$rating", rating.Value), ("$at", at));
        return true;
    }

    private static async Task StoreAsync(SqliteConnection connection, string normalizedCode, double rating, CancellationToken token)
    {
        await ExecuteAsync(connection, """
            INSERT INTO DeletedMovieRatings(NormalizedMovieCode,Rating,UpdatedAt)
            VALUES($code,$rating,$at)
            ON CONFLICT(NormalizedMovieCode) DO UPDATE SET Rating=excluded.Rating,UpdatedAt=excluded.UpdatedAt
            """, token, ("$code", normalizedCode), ("$rating", rating), ("$at", DateTimeOffset.UtcNow.ToString("O")));
    }

    private static Task StoreSettingAsync(SqliteConnection connection, string key, bool value, CancellationToken token) =>
        ExecuteAsync(connection, """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt)
            VALUES($key,$value,'boolean',$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """, token, ("$key", key), ("$value", JsonSerializer.Serialize(value)), ("$at", DateTimeOffset.UtcNow.ToString("O")));

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static async Task<bool> EnabledAsync(SqliteConnection connection, CancellationToken token)
    {
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='ratingHistory.enabled'", token);
        return ParseBool(raw, true);
    }

    private static async Task<bool> EnabledAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
    {
        string? raw = await ScalarTextAsync(connection, transaction, "SELECT ValueJson FROM AppSettings WHERE Key='ratingHistory.enabled'");
        return ParseBool(raw, true);
    }

    private static async Task<bool> DeleteOnClearAsync(SqliteConnection connection, CancellationToken token)
    {
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='ratingHistory.deleteOnClear'", token);
        return ParseBool(raw, false);
    }

    private static bool ParseBool(string? raw, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        try { return JsonSerializer.Deserialize<bool>(raw); }
        catch { return bool.TryParse(raw, out bool value) ? value : fallback; }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<double?> ScalarNullableDoubleAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        object? scalar = await command.ExecuteScalarAsync();
        return scalar is null or DBNull ? null : Convert.ToDouble(scalar);
    }
}

public sealed record RatingHistorySettingsDto(bool Enabled, bool DeleteOnClear);
