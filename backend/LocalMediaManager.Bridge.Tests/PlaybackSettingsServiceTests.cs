using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class PlaybackSettingsServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-playback-settings-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "product.db");
    private string LegacyDatabase => Path.Combine(root, "legacy.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0009_PlaybackSettings.sql" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task EmptyProductSettingFallsBackToLegacyPlayerPath()
    {
        string player = Path.Combine(root, "legacy-player.exe");
        await File.WriteAllBytesAsync(player, [1]);
        await using (var connection = new SqliteConnection($"Data Source={LegacyDatabase}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE app_configs(ConfigName TEXT, ConfigValue TEXT); INSERT INTO app_configs VALUES('WindowConfig.Settings',$json)";
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(new { VideoPlayerPath = player }));
            await command.ExecuteNonQueryAsync();
        }

        PlaybackSettingsDto result = await new PlaybackSettingsService(Database, LegacyDatabase).ReadAsync();

        Assert.False(result.UseSystemDefault);
        Assert.Equal(player, result.PlayerPath);
    }

    [Fact]
    public async Task SavedPlayerOverridesLegacyAndPersists()
    {
        string player = Path.Combine(root, "selected-player.exe");
        await File.WriteAllBytesAsync(player, [1]);
        var service = new PlaybackSettingsService(Database, LegacyDatabase);

        await service.SaveAsync(new(player, false));
        PlaybackSettingsDto result = await service.ReadAsync();

        Assert.False(result.UseSystemDefault);
        Assert.Equal(Path.GetFullPath(player), result.PlayerPath);
    }

    [Fact]
    public async Task InvalidExecutablePathIsRejectedWithoutPersisting()
    {
        var service = new PlaybackSettingsService(Database, LegacyDatabase);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new(Path.Combine(root, "missing.exe"), false)));

        Assert.True((await service.ReadAsync()).UseSystemDefault);
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
        return Task.CompletedTask;
    }
}
