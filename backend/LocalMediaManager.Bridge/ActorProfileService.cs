using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record ActorProfileFieldSource(string Source, string UpdatedAt, bool UserEdited);
public sealed record ActorProfileData(string? BirthDate = null, int? HeightCm = null, string? Cup = null,
    string? BirthPlace = null, string? ActivityPeriod = null, string? Description = null,
    IReadOnlyList<string>? Aliases = null, string? AvatarUrl = null);
public sealed record ActorProfileCandidate(string Source, string MatchedName, string SourceUrl, double Confidence, ActorProfileData Profile);
public sealed record ActorProfileMergeResult(long ActorId, IReadOnlyList<string> UpdatedFields, IReadOnlyList<string> Conflicts, string SourcesJson);

public sealed class ActorProfileService(string databasePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActorProfileMergeResult> MergeAsync(long actorId, ActorProfileCandidate candidate, CancellationToken cancellationToken)
    {
        if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId), "ActorId must be greater than zero.");
        if (candidate.Confidence < 0.85) throw new ArgumentException("Low-confidence actor profiles cannot be merged automatically.");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        ActorRow actor = await ReadAsync(connection, (SqliteTransaction)transaction, actorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Actor {actorId} was not found.");
        Dictionary<string, ActorProfileFieldSource> sources = ParseSources(actor.SourcesJson);
        var values = new Dictionary<string, object?>();
        var updated = new List<string>();
        var conflicts = new List<string>();
        string now = DateTimeOffset.UtcNow.ToString("O");

        Add("BirthDate", actor.BirthDate, NormalizeDate(candidate.Profile.BirthDate));
        Add("HeightCm", actor.HeightCm, candidate.Profile.HeightCm is >= 100 and <= 250 ? candidate.Profile.HeightCm : null);
        Add("Cup", actor.Cup, NormalizeCup(candidate.Profile.Cup));
        Add("BirthPlace", actor.BirthPlace, Clean(candidate.Profile.BirthPlace, 160));
        Add("ActivityPeriod", actor.ActivityPeriod, Clean(candidate.Profile.ActivityPeriod, 160));
        Add("Description", actor.Description, Clean(candidate.Profile.Description, 1200));

        string? aliases = MergeAliases(actor.Alias, candidate.Profile.Aliases);
        if (!string.Equals(actor.Alias, aliases, StringComparison.Ordinal)) Set("Alias", aliases);
        if (values.Count > 0) {
            values["ProfileFieldSourcesJson"] = JsonSerializer.Serialize(sources, JsonOptions);
            values["UpdatedAt"] = now;
            await UpdateAsync(connection, (SqliteTransaction)transaction, actorId, values, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(actorId, updated, conflicts, JsonSerializer.Serialize(sources, JsonOptions));

        void Add(string field, object? current, object? incoming)
        {
            if (incoming is null || incoming is string text && string.IsNullOrWhiteSpace(text)) return;
            if (sources.TryGetValue(field, out ActorProfileFieldSource? tracked) && tracked.UserEdited) {
                if (!Same(current, incoming)) conflicts.Add($"{field}:userEdited");
                return;
            }
            if (current is not null && (current is not string currentText || !string.IsNullOrWhiteSpace(currentText))) {
                if (!Same(current, incoming)) conflicts.Add($"{field}:{candidate.Source}");
                return;
            }
            Set(field, incoming);
        }
        void Set(string field, object? value)
        {
            values[field] = value;
            updated.Add(field);
            sources[field] = new(candidate.Source, now, false);
        }
    }

    public static string MarkUserEdited(string? json, IEnumerable<string> fields, string now)
    {
        Dictionary<string, ActorProfileFieldSource> sources = ParseSources(json);
        foreach (string field in fields) sources[field] = new("User", now, true);
        return JsonSerializer.Serialize(sources, JsonOptions);
    }

    private static async Task UpdateAsync(SqliteConnection connection, SqliteTransaction transaction, long actorId,
        IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        string[] allowed = ["BirthDate", "HeightCm", "Cup", "BirthPlace", "ActivityPeriod", "Description", "Alias", "ProfileFieldSourcesJson", "UpdatedAt"];
        if (values.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new InvalidOperationException("Unsupported actor profile field.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE Actors SET {string.Join(",", values.Keys.Select(key => $"{key}=${key}"))} WHERE Id=$id AND Id>0";
        foreach ((string key, object? value) in values) command.Parameters.AddWithValue($"${key}", value ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", actorId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Actor profile update did not affect exactly one actor.");
    }

    private static async Task<ActorRow?> ReadAsync(SqliteConnection connection, SqliteTransaction transaction, long actorId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Alias,BirthDate,Description,HeightCm,Cup,BirthPlace,ActivityPeriod,ProfileFieldSourcesJson FROM Actors WHERE Id=$id AND Id>0";
        command.Parameters.AddWithValue("$id", actorId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(Text(reader, 0), Text(reader, 1), Text(reader, 2), Int(reader, 3), Text(reader, 4), Text(reader, 5), Text(reader, 6), reader.GetString(7))
            : null;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static Dictionary<string, ActorProfileFieldSource> ParseSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try { return JsonSerializer.Deserialize<Dictionary<string, ActorProfileFieldSource>>(json, JsonOptions) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static string? MergeAliases(string? existing, IReadOnlyList<string>? incoming)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing)) values.AddRange(existing.Split([',', '，', '/', '／'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (incoming is not null) values.AddRange(incoming);
        string[] merged = values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return merged.Length == 0 ? existing : string.Join(", ", merged);
    }

    private static string? NormalizeDate(string? value) => DateOnly.TryParse(value, out DateOnly date) ? date.ToString("yyyy-MM-dd") : null;
    private static string? NormalizeCup(string? value)
    {
        string? clean = Clean(value, 8)?.ToUpperInvariant().Replace("カップ", "", StringComparison.Ordinal);
        return clean is not null && Regex.IsMatch(clean, "^[A-N]$") ? clean : null;
    }
    private static string? Clean(string? value, int max) { string? text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, max)]; }
    private static bool Same(object? left, object? right) => string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.OrdinalIgnoreCase);
    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static int? Int(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private sealed record ActorRow(string? Alias, string? BirthDate, string? Description, int? HeightCm, string? Cup, string? BirthPlace, string? ActivityPeriod, string SourcesJson);
}
