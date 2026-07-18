using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class SettingsSaveCoordinatorTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-settings-save-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "settings.db");
    private string LegacyDatabase => Path.Combine(root, "legacy.db");
    private string InstallRoot => Path.Combine(root, "Local Media Manager Next");
    private string DocumentsRoot => Path.Combine(root, "Documents");
    private SettingsSaveCoordinator coordinator = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql", "0012_MediaStorageSettings.sql" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        Directory.CreateDirectory(InstallRoot);
        Directory.CreateDirectory(DocumentsRoot);
        coordinator = new SettingsSaveCoordinator(Database,
            InstallRoot,
            new MetadataProviderSettingsService(Database),
            new NfoService(Database, new MediaStoragePathResolver(Database, InstallRoot)),
            new PlaybackSettingsService(Database, LegacyDatabase),
            new RatingHistoryService(Database));
        Directory.CreateDirectory(SettingsDefaults.MediaStorageForEnvironment(InstallRoot, Database).RootPath);
    }

    [Fact]
    public async Task UnifiedSettingsLoadMatchesDefaultsForFreshDatabase()
    {
        UnifiedSettingsDto defaults = SettingsSaveCoordinator.Defaults(Database, InstallRoot);
        UnifiedSettingsDto loaded = await coordinator.ReadAsync();

        Assert.Equal(defaults.MetaTube, loaded.MetaTube);
        Assert.Equal(defaults.Nfo, loaded.Nfo);
        Assert.Equal(defaults.RatingRetention, loaded.RatingRetention);
        Assert.Equal(defaults.Appearance, loaded.Appearance);
        Assert.Equal(defaults.MediaStorage, loaded.MediaStorage);
    }

    [Fact]
    public async Task MultipleCategoriesSaveInSingleCoordinatorCall()
    {
        string nfoDir = Path.Combine(root, "nfo");
        Directory.CreateDirectory(nfoDir);
        UnifiedSettingsDto current = await coordinator.ReadAsync();
        UnifiedSettingsDto draft = current with
        {
            MetaTube = new(false, "http://localhost:8080", 500, false, true, false, false),
            Nfo = new("SeparateFile", nfoDir, true, false),
            RatingRetention = new(false),
            Appearance = new("light"),
            MediaStorage = current.MediaStorage with { PostersDirectory = "Covers" },
        };

        UnifiedSettingsSaveResult result = await coordinator.SaveAsync(draft);
        UnifiedSettingsDto saved = await coordinator.ReadAsync();

        Assert.Contains("metaTube", result.ChangedFields);
        Assert.Contains("nfo", result.ChangedFields);
        Assert.Contains("ratingRetention", result.ChangedFields);
        Assert.Contains("appearance", result.ChangedFields);
        Assert.Contains("mediaStorage", result.ChangedFields);
        Assert.False(saved.MetaTube.Enabled);
        Assert.Equal(180, saved.MetaTube.TimeoutSeconds);
        Assert.Equal(Path.GetFullPath(nfoDir), saved.Nfo.OutputDirectory);
        Assert.False(saved.Nfo.IncludeImages);
        Assert.False(saved.RatingRetention.Enabled);
        Assert.Equal("light", saved.Appearance.ThemeMode);
        Assert.Equal("Covers", saved.MediaStorage.PostersDirectory);
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
    public async Task MediaStorageCanPersistWithUnifiedSettings()
    {
        string mediaRoot = Path.Combine(root, "custom-media");
        Directory.CreateDirectory(mediaRoot);
        UnifiedSettingsDto draft = (await coordinator.ReadAsync()) with
        {
            MediaStorage = new(mediaRoot, "Posters2", "Thumbs2", "Fanart2", "Previews2", "Shots2", "WallCrops2", "Gifs2", "Nfos2", "{MovieCode}", "{MovieCode}-{MovieTitle}"),
        };

        UnifiedSettingsSaveResult result = await coordinator.SaveAsync(draft);
        UnifiedSettingsDto saved = await coordinator.ReadAsync();

        Assert.Contains("mediaStorage", result.ChangedFields);
        Assert.Equal(Path.GetFullPath(mediaRoot), saved.MediaStorage.RootPath);
        Assert.Equal("Thumbs2", saved.MediaStorage.ThumbnailsDirectory);
        Assert.Equal("{MovieCode}-{MovieTitle}", saved.MediaStorage.FileNameTemplate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MediaStorageRejectsEmptyRoot(string rootPath)
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { RootPath = rootPath } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        UnifiedSettingsDto after = await coordinator.ReadAsync();
        Assert.Equal(before.MediaStorage, after.MediaStorage);
    }

    [Fact]
    public async Task MediaStorageRejectsRootThatPointsToFile()
    {
        string file = Path.Combine(root, "not-a-directory.txt");
        await File.WriteAllTextAsync(file, "x");
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { RootPath = file } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);
    }

    [Fact]
    public async Task MediaStorageRejectsIllegalWindowsRootPath()
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { RootPath = Path.Combine(root, "bad<name") } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);
    }

    [Theory]
    [InlineData("C:\\Absolute")]
    [InlineData("..")]
    [InlineData("Posters\\Nested")]
    [InlineData("CON")]
    public async Task MediaStorageRejectsInvalidRelativeDirectories(string directory)
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { PostersDirectory = directory } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);
    }

    [Fact]
    public async Task MediaStorageRejectsDuplicateResourceDirectories()
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { PostersDirectory = "Same", ThumbnailsDirectory = "same" } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-name")]
    [InlineData("{Unknown}")]
    [InlineData("CON")]
    public async Task MediaStorageRejectsInvalidTemplates(string template)
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with { MediaStorage = before.MediaStorage with { MovieFolderTemplate = template } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);
    }

    [Fact]
    public async Task MediaStorageMissingRootRequiresExplicitCreate()
    {
        string missingRoot = Path.Combine(root, "new-media-root");
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto draft = before with { MediaStorage = before.MediaStorage with { RootPath = missingRoot } };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(draft));
        Assert.False(Directory.Exists(missingRoot));
        Assert.Equal(before.MediaStorage, (await coordinator.ReadAsync()).MediaStorage);

        UnifiedSettingsSaveResult result = await coordinator.SaveAsync(draft, createMissingMediaStorageRoot: true);

        Assert.True(Directory.Exists(missingRoot));
        Assert.Contains("mediaStorage", result.ChangedFields);
        Assert.Equal(Path.GetFullPath(missingRoot), (await coordinator.ReadAsync()).MediaStorage.RootPath);
    }

    [Fact]
    public async Task MediaStorageInvalidSaveDoesNotPartiallyPersistOtherCategories()
    {
        UnifiedSettingsDto before = await coordinator.ReadAsync();
        UnifiedSettingsDto invalid = before with
        {
            Appearance = new("light"),
            MediaStorage = before.MediaStorage with { PostersDirectory = ".." },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.SaveAsync(invalid));

        UnifiedSettingsDto after = await coordinator.ReadAsync();
        Assert.Equal(before.Appearance, after.Appearance);
        Assert.Equal(before.MediaStorage, after.MediaStorage);
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
        UnifiedSettingsDto defaults = SettingsSaveCoordinator.Defaults(Database, InstallRoot);

        UnifiedSettingsDto beforeSave = await coordinator.ReadAsync();
        Assert.NotEqual(defaults.RatingRetention, beforeSave.RatingRetention);

        await coordinator.SaveAsync(defaults);
        UnifiedSettingsDto afterSave = await coordinator.ReadAsync();
        Assert.Equal(defaults.RatingRetention, afterSave.RatingRetention);
        Assert.Equal(defaults.Appearance, afterSave.Appearance);
        Assert.Equal(defaults.MediaStorage, afterSave.MediaStorage);
    }

    [Fact]
    public void MediaStorageDefaultFollowsInstallRoot()
    {
        string installA = Path.Combine(root, "AppA", "Local Media Manager");
        string installB = Path.Combine(root, "AppB", "Local Media Manager");
        Directory.CreateDirectory(installA);
        Directory.CreateDirectory(installB);

        MediaStorageSettingsDto first = SettingsDefaults.MediaStorageForEnvironment(installA, Database, DocumentsRoot, _ => true);
        MediaStorageSettingsDto second = SettingsDefaults.MediaStorageForEnvironment(installB, Database, DocumentsRoot, _ => true);

        Assert.Equal(Path.Combine(root, "AppA", "Local Media Manager Data", "MediaStorage"), first.RootPath);
        Assert.Equal(Path.Combine(root, "AppB", "Local Media Manager Data", "MediaStorage"), second.RootPath);
        Assert.False(first.UsingFallbackDefault);
        Assert.False(second.UsingFallbackDefault);
    }

    [Fact]
    public void MediaStorageDefaultCanResolveDifferentDriveInstallRoot()
    {
        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(@"E:\Apps\Local Media Manager", Database, DocumentsRoot, _ => true);

        Assert.Equal(@"E:\Apps\Local Media Manager Data\MediaStorage", defaults.RootPath);
        Assert.False(defaults.UsingFallbackDefault);
    }

    [Fact]
    public void MediaStorageDefaultFallsBackFromProgramFiles()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) return;
        string install = Path.Combine(programFiles, "Local Media Manager");

        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(install, Database, DocumentsRoot, _ => true);

        Assert.Equal(Path.Combine(DocumentsRoot, "Local Media Manager", "MediaStorage"), defaults.RootPath);
        Assert.True(defaults.UsingFallbackDefault);
    }

    [Fact]
    public void MediaStorageDefaultFallsBackWhenBesideDataRootIsNotWritable()
    {
        string install = Path.Combine(root, "Apps", "Local Media Manager");

        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(install, Database, DocumentsRoot, _ => false);

        Assert.Equal(Path.Combine(DocumentsRoot, "Local Media Manager", "MediaStorage"), defaults.RootPath);
        Assert.True(defaults.UsingFallbackDefault);
    }

    [Fact]
    public async Task MediaStorageSavedUserRootHasHighestPriority()
    {
        string customRoot = Path.Combine(root, "custom-root");
        Directory.CreateDirectory(customRoot);
        UnifiedSettingsDto current = await coordinator.ReadAsync();
        await coordinator.SaveAsync(current with { MediaStorage = current.MediaStorage with { RootPath = customRoot } });
        string movedInstallRoot = Path.Combine(root, "Moved", "Local Media Manager");
        Directory.CreateDirectory(movedInstallRoot);
        var movedCoordinator = new SettingsSaveCoordinator(Database,
            movedInstallRoot,
            new MetadataProviderSettingsService(Database),
            new NfoService(Database, new MediaStoragePathResolver(Database, movedInstallRoot)),
            new PlaybackSettingsService(Database, LegacyDatabase),
            new RatingHistoryService(Database));

        UnifiedSettingsDto loaded = await movedCoordinator.ReadAsync();

        Assert.Equal(Path.GetFullPath(customRoot), loaded.MediaStorage.RootPath);
        Assert.False(loaded.MediaStorage.UsingFallbackDefault);
    }

    [Fact]
    public async Task EmptyMediaStorageRootReturnsRuntimeDefault()
    {
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSettings SET ValueJson='\"\"' WHERE Key='mediaStorage.rootPath'";
        await command.ExecuteNonQueryAsync();

        UnifiedSettingsDto loaded = await coordinator.ReadAsync();

        Assert.Equal(Path.Combine(root, "Local Media Manager Next Data", "MediaStorage"), loaded.MediaStorage.RootPath);
        Assert.False(loaded.MediaStorage.UsingFallbackDefault);
    }

    [Fact]
    public async Task MediaStorageMigrationDoesNotSeedFixedAbsoluteRoot()
    {
        string migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "0012_MediaStorageSettings.sql"));

        Assert.Contains("('mediaStorage.rootPath','\"\"'", migration);
        Assert.DoesNotContain("D:\\", migration);
        Assert.DoesNotContain("C:\\", migration);
        Assert.DoesNotContain("Local Media Manager Next Data", migration);
    }

    [Fact]
    public void MediaStorageDefaultsEndpointValueCanBeUsedAsDraftDefault()
    {
        string install = Path.Combine(root, "E-Apps", "Local Media Manager");
        Directory.CreateDirectory(install);
        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(install, Database, DocumentsRoot, _ => true);
        UnifiedSettingsDto endpointDefaults = SettingsSaveCoordinator.Defaults(Database, install);

        Assert.Equal(defaults.RootPath, endpointDefaults.MediaStorage.RootPath);
        Assert.Equal(defaults.PostersDirectory, endpointDefaults.MediaStorage.PostersDirectory);
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
