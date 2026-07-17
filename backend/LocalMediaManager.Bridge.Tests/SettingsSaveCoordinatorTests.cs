using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class SettingsSaveCoordinatorTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-settings-save-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "settings.db");
    private string LegacyDatabase => Path.Combine(root, "legacy.db");
    private SettingsSaveCoordinator coordinator = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        coordinator = new SettingsSaveCoordinator(Database,
            new MetadataProviderSettingsService(Database),
            new NfoService(Database),
            new PlaybackSettingsService(Database, LegacyDatabase),
            new RatingHistoryService(Database));
    }

    [Fact]
    public async Task UnifiedSettingsLoadMatchesDefaultsForFreshDatabase()
    {
        UnifiedSettingsDto defaults = SettingsSaveCoordinator.Defaults();
        UnifiedSettingsDto loaded = await coordinator.ReadAsync();

        Assert.Equal(defaults.MetaTube, loaded.MetaTube);
        Assert.Equal(defaults.Nfo, loaded.Nfo);
        Assert.Equal(defaults.RatingRetention, loaded.RatingRetention);
        Assert.Equal(defaults.Appearance, loaded.Appearance);
    }

    [Fact]
    public async Task MultipleCategoriesSaveInSingleCoordinatorCall()
    {
        string nfoDir = Path.Combine(root, "nfo");
        Directory.CreateDirectory(nfoDir);
        UnifiedSettingsDto draft = (await coordinator.ReadAsync()) with
        {
            MetaTube = new(false, "http://localhost:8080", 500, false, true, false, false),
            Nfo = new("SeparateFile", nfoDir, true, false),
            RatingRetention = new(false),
            Appearance = new("light"),
        };

        UnifiedSettingsSaveResult result = await coordinator.SaveAsync(draft);
        UnifiedSettingsDto saved = await coordinator.ReadAsync();

        Assert.Contains("metaTube", result.ChangedFields);
        Assert.Contains("nfo", result.ChangedFields);
        Assert.Contains("ratingRetention", result.ChangedFields);
        Assert.Contains("appearance", result.ChangedFields);
        Assert.False(saved.MetaTube.Enabled);
        Assert.Equal(180, saved.MetaTube.TimeoutSeconds);
        Assert.Equal(Path.GetFullPath(nfoDir), saved.Nfo.OutputDirectory);
        Assert.False(saved.Nfo.IncludeImages);
        Assert.False(saved.RatingRetention.Enabled);
        Assert.Equal("light", saved.Appearance.ThemeMode);
    }

    [Fact]
    public async Task FailedSaveDoesNotPartiallyPersistEarlierFields()
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with
        {
            MetaTube = before.MetaTube with { Enabled = false },
            Playback = new(Path.Combine(root, "missing-player.exe"), false),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        UnifiedSettingsDto after = await coordinator.ReadAsync();
        Assert.Equal(before.MetaTube, after.MetaTube);
        Assert.Equal(before.Playback, after.Playback);
    }

    [Fact]
    public async Task DefaultsAreDraftUntilExplicitSave()
    {
        UnifiedSettingsDto modified = (await coordinator.ReadAsync()) with
        {
            RatingRetention = new(false),
            Appearance = new("light"),
        };
        await coordinator.SaveAsync(modified);
        UnifiedSettingsDto defaults = SettingsSaveCoordinator.Defaults();

        UnifiedSettingsDto beforeSave = await coordinator.ReadAsync();
        Assert.NotEqual(defaults.RatingRetention, beforeSave.RatingRetention);

        await coordinator.SaveAsync(defaults);
        UnifiedSettingsDto afterSave = await coordinator.ReadAsync();
        Assert.Equal(defaults.RatingRetention, afterSave.RatingRetention);
        Assert.Equal(defaults.Appearance, afterSave.Appearance);
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
