using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record CoverCropDto(string Mode, double FocusX, double FocusY, bool Manual);
public sealed record CoverCropUpdateCommand(string Mode);

public sealed class CoverCropService(string databasePath, IFaceDetectionService faces)
{
    public async Task<CoverCropDto> ResolveAsync(long movieId, CancellationToken token = default)
    {
        await using SqliteConnection connection = Open();
        (string mode, double? focusX, double? focusY, string? version) = await ReadAsync(connection, movieId, token);
        string effectiveMode = NormalizeMode(mode, await GlobalModeAsync(connection, token));
        if (effectiveMode is "Left" or "Center" or "Right") return Manual(effectiveMode);
        string? path = await CoverPathAsync(connection, movieId, token);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new("AutoFace", .5, .5, false);
        FileInfo file = new(path);
        string nextVersion = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}:{AiModelRegistry.YuNetFaceDetector.Version}";
        if (version == nextVersion && focusX is >= 0 and <= 1) return new("AutoFace", focusX.Value, focusY ?? .5, false);
        try {
            FaceRectangle? face = (await faces.DetectAsync(path, token)).LargestFace;
            double x = face is null ? .5 : Math.Clamp(face.CenterX, .2, .8);
            await StoreAsync(connection, movieId, "AutoFace", x, .5, nextVersion, token);
            return new("AutoFace", x, .5, false);
        } catch {
            return new("AutoFace", .5, .5, false);
        }
    }

    public async Task<CoverCropDto> SetAsync(long movieId, CoverCropUpdateCommand command, CancellationToken token = default)
    {
        string mode = NormalizeMode(command.Mode, "AutoFace");
        await using SqliteConnection connection = Open();
        if (await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies WHERE Id=$id", token, ("$id", movieId)) == 0) throw new KeyNotFoundException("影片不存在。");
        if (mode == "AutoFace") {
            await ExecuteAsync(connection, "DELETE FROM MovieCoverCropSettings WHERE MovieId=$id", token, ("$id", movieId));
            return await ResolveAsync(movieId, token);
        }
        CoverCropDto value = Manual(mode);
        await StoreAsync(connection, movieId, mode, value.FocusX, value.FocusY, null, token);
        return value;
    }

    private static CoverCropDto Manual(string mode) => new(mode, mode == "Left" ? 0 : mode == "Right" ? 1 : .5, .5, true);
    private static string NormalizeMode(string? value, string fallback) => value is "AutoFace" or "Left" or "Center" or "Right" ? value : fallback;
    private SqliteConnection Open() { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString()); c.Open(); return c; }
    private static async Task<(string Mode, double? X, double? Y, string? Version)> ReadAsync(SqliteConnection c, long id, CancellationToken token)
    {
        await using SqliteCommand command = c.CreateCommand(); command.CommandText = "SELECT CoverCropMode,CoverFocusX,CoverFocusY,CoverVersion FROM MovieCoverCropSettings WHERE MovieId=$id"; command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : ("AutoFace", null, null, null);
    }
    private static async Task<string> GlobalModeAsync(SqliteConnection c, CancellationToken token)
    {
        await using SqliteCommand command = c.CreateCommand(); command.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.coverCropMode'";
        object? raw = await command.ExecuteScalarAsync(token);
        try { return NormalizeMode(raw is null ? null : JsonSerializer.Deserialize<string>(Convert.ToString(raw)!), "AutoFace"); } catch { return "AutoFace"; }
    }
    private static async Task<string?> CoverPathAsync(SqliteConnection c, long id, CancellationToken token)
    {
        await using SqliteCommand command = c.CreateCommand(); command.CommandText = "SELECT FilePath FROM Images WHERE MovieId=$id AND FilePath IS NOT NULL AND IsDerived=0 ORDER BY IsPrimary DESC, CASE ImageType WHEN 'Poster' THEN 0 WHEN 'Thumbnail' THEN 1 WHEN 'Fanart' THEN 2 ELSE 9 END, Id LIMIT 1"; command.Parameters.AddWithValue("$id", id);
        return Convert.ToString(await command.ExecuteScalarAsync(token));
    }
    private static async Task StoreAsync(SqliteConnection c, long id, string mode, double? x, double? y, string? version, CancellationToken token) => await ExecuteAsync(c, "INSERT INTO MovieCoverCropSettings(MovieId,CoverCropMode,CoverFocusX,CoverFocusY,CoverVersion,UpdatedAt) VALUES($id,$mode,$x,$y,$version,$at) ON CONFLICT(MovieId) DO UPDATE SET CoverCropMode=excluded.CoverCropMode,CoverFocusX=excluded.CoverFocusX,CoverFocusY=excluded.CoverFocusY,CoverVersion=excluded.CoverVersion,UpdatedAt=excluded.UpdatedAt", token, ("$id", id), ("$mode", mode), ("$x", (object?)x ?? DBNull.Value), ("$y", (object?)y ?? DBNull.Value), ("$version", (object?)version ?? DBNull.Value), ("$at", DateTimeOffset.UtcNow.ToString("O")));
    private static async Task<long> ScalarAsync(SqliteConnection c, string sql, CancellationToken token, params (string, object)[] values) { await using SqliteCommand command = c.CreateCommand(); command.CommandText = sql; foreach ((string key, object value) in values) command.Parameters.AddWithValue(key, value); return Convert.ToInt64(await command.ExecuteScalarAsync(token)); }
    private static async Task ExecuteAsync(SqliteConnection c, string sql, CancellationToken token, params (string, object)[] values) { await using SqliteCommand command = c.CreateCommand(); command.CommandText = sql; foreach ((string key, object value) in values) command.Parameters.AddWithValue(key, value); await command.ExecuteNonQueryAsync(token); }
}
