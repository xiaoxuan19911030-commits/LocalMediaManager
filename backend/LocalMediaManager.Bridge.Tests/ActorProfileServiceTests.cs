using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ActorProfileServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-actor-profile-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0014_ActorProfileFields.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,Alias,BirthDate,Description,LegacySource,CreatedAt,UpdatedAt) VALUES(7,'Test Actor','TEST ACTOR','Existing','1990-01-01',NULL,'Test',$at,$at)", ("$at", DateTimeOffset.UtcNow.ToString("O")));
    }

    [Fact]
    public async Task MigrationAddsNullableFieldsWithoutRebuildingOrChangingActorIds()
    {
        await using var connection = await Open();
        Assert.Equal(7, await Scalar(connection, "SELECT Id FROM Actors"));
        Assert.Equal(5, await Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('Actors') WHERE name IN ('HeightCm','Cup','BirthPlace','ActivityPeriod','ProfileFieldSourcesJson')"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM Actors WHERE Id=0"));
        Assert.Equal("{}", await Text(connection, "SELECT ProfileFieldSourcesJson FROM Actors WHERE Id=7"));
    }

    [Fact]
    public async Task MergeFillsEmptyFieldsPreservesExistingAndTracksConflicts()
    {
        var candidate = new ActorProfileCandidate("Minnano", "Test Actor", "https://example.test/actor", 0.98,
            new("1991-02-03", 165, "C", null, null, null, ["Existing", "別名"]));
        ActorProfileMergeResult result = await new ActorProfileService(Database).MergeAsync(7, candidate, CancellationToken.None);
        await using var connection = await Open();
        Assert.Equal("1990-01-01", await Text(connection, "SELECT BirthDate FROM Actors WHERE Id=7"));
        Assert.Equal(165, await Scalar(connection, "SELECT HeightCm FROM Actors WHERE Id=7"));
        Assert.Equal("C", await Text(connection, "SELECT Cup FROM Actors WHERE Id=7"));
        Assert.Contains("BirthDate:Minna" + "no", result.Conflicts);
        Assert.Equal("Existing, 別名", await Text(connection, "SELECT Alias FROM Actors WHERE Id=7"));
        using JsonDocument sources = JsonDocument.Parse(result.SourcesJson);
        Assert.Equal("Minnano", sources.RootElement.GetProperty("HeightCm").GetProperty("source").GetString());
    }

    [Fact]
    public async Task UserEditedFieldAndActorZeroAreProtected()
    {
        await using (var connection = await Open()) {
            string sources = ActorProfileService.MarkUserEdited("{}", ["HeightCm"], DateTimeOffset.UtcNow.ToString("O"));
            await Execute(connection, "UPDATE Actors SET HeightCm=170,ProfileFieldSourcesJson=$sources WHERE Id=7", ("$sources", sources));
        }
        ActorProfileMergeResult result = await new ActorProfileService(Database).MergeAsync(7,
            new("Minnano", "Test Actor", "https://example.test", 0.99, new(HeightCm: 160)), CancellationToken.None);
        Assert.Contains("HeightCm:userEdited", result.Conflicts);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ActorProfileService(Database).MergeAsync(0,
            new("Minnano", "Test Actor", "https://example.test", 0.99, new(HeightCm: 160)), CancellationToken.None));
    }

    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); return Task.CompletedTask; }
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static async Task Execute(SqliteConnection connection, string sql, params (string, object)[] args) { await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var (name,value) in args)command.Parameters.AddWithValue(name,value);await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    private static async Task<string> Text(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToString(await command.ExecuteScalarAsync())!; }
}
