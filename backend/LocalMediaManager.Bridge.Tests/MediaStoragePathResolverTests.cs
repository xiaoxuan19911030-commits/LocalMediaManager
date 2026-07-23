using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MediaStoragePathResolverTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-media-storage-path-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "media.db");
    private string InstallRoot => Path.Combine(root, "Local Media Manager Next");
    private string MediaRoot => Path.Combine(root, "MediaStorage");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(InstallRoot);
        await using SqliteConnection connection = await OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql", "0012_MediaStorageSettings.sql", "0015_LibraryTypesAndLocalMedia.sql" })
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(MediaRoot)));
        await InsertMovieAsync(connection, 1, "ABC-123", "Example Movie");
    }

    [Theory]
    [InlineData("Poster", "Posters", "ABC-123.jpg", false)]
    [InlineData("Thumbnail", "Thumbnails", "ABC-123.jpg", false)]
    [InlineData("Fanart", "Fanart", "ABC-123.jpg", false)]
    [InlineData("Preview", "Previews", "01.jpg", true)]
    [InlineData("Screenshot", "Screenshots", "01.jpg", true)]
    [InlineData("GIF", "GIF", "01.gif", true)]
    [InlineData("NFO", "NFO", "ABC-123.nfo", true)]
    [InlineData("GeneratedCard", "WallCrops", "ABC-123.jpg", false)]
    [InlineData("CardCover", "WallCrops", "ABC-123.jpg", false)]
    public async Task ResolvesMediaStoragePathForResourceType(string type, string directory, string fileName, bool grouped)
    {
        string extension = type.Equals("GIF", StringComparison.OrdinalIgnoreCase) ? ".gif"
            : type.Equals("NFO", StringComparison.OrdinalIgnoreCase) ? ".nfo"
            : ".jpg";
        int? index = type is "Preview" or "Screenshot" or "GIF" ? 1 : null;

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, type, extension, index);

        string expected = grouped
            ? Path.Combine(MediaRoot, directory, "ABC-123", fileName)
            : Path.Combine(MediaRoot, directory, fileName);
        Assert.Equal(expected, result.FullPath);
        Assert.Equal(directory, result.Directory);
        Assert.Equal(grouped ? "ABC-123" : "", result.MovieFolder);
    }

    [Fact]
    public async Task ResolveDoesNotCreateDirectoriesUntilWriteIsRequested()
    {
        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.False(Directory.Exists(Path.GetDirectoryName(result.FullPath)!));
        Resolver().EnsureDirectoryForWrite(result.FullPath);
        Assert.True(Directory.Exists(Path.GetDirectoryName(result.FullPath)!));
    }

    [Theory]
    [InlineData("Poster", 1, "Posters", "42.jpg")]
    [InlineData("Screenshot", 1, "Screenshots", "42", "01.jpg")]
    [InlineData("Screenshot", 2, "Screenshots", "42", "02.jpg")]
    public async Task LocalResourcesUseStableMovieId(string type, int index, string directory, params string[] segments)
    {
        MediaStorageResourcePath result = await Resolver().ResolveForLocalMovieAsync(42, type, ".jpg", index);
        Assert.Equal(Path.Combine(new[] { MediaRoot, directory }.Concat(segments).ToArray()), result.FullPath);
    }

    [Fact]
    public async Task ResolveIgnoresExistingLowerCaseDirectoryWhenWriting()
    {
        string actualResourceDirectory = Path.Combine(MediaRoot, "posters");
        string actualMovieDirectory = Path.Combine(actualResourceDirectory, "abc-123");
        Directory.CreateDirectory(actualMovieDirectory);

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(MediaRoot, "Posters", "ABC-123.jpg"), result.FullPath);
        Assert.Equal("", result.MovieFolder);
    }

    [Fact]
    public async Task ResolveIgnoresExistingLowerCaseFileWhenWriting()
    {
        string actualMovieDirectory = Path.Combine(MediaRoot, "posters", "abc-123");
        Directory.CreateDirectory(actualMovieDirectory);
        string existingPath = Path.Combine(actualMovieDirectory, "abc-123.jpg");
        await File.WriteAllTextAsync(existingPath, "existing");

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(MediaRoot, "Posters", "ABC-123.jpg"), result.FullPath);
        Assert.Equal("ABC-123.jpg", result.FileName);
    }

    [Theory]
    [InlineData("sone-454", "SONE-454")]
    [InlineData("SONE-454", "SONE-454")]
    [InlineData("SonE-454", "SONE-454")]
    [InlineData("abw-001", "ABW-001")]
    [InlineData("ipx-123", "IPX-123")]
    public void NormalizeMovieNumberUsesFixedUpperCasePolicy(string input, string expected)
    {
        Assert.Equal(MetadataNamingPolicy.UpperCaseMovieNumber, MetadataNamingPolicy.Current);
        Assert.Equal(expected, MetadataNamingPolicy.NormalizeMovieNumber(input, 1));
    }

    [Theory]
    [InlineData("Poster", ".jpg", null, "Posters", "SONE-454.jpg")]
    [InlineData("Fanart", ".jpg", null, "Fanart", "SONE-454.jpg")]
    [InlineData("GeneratedCard", ".jpg", null, "WallCrops", "SONE-454.jpg")]
    [InlineData("NFO", ".nfo", null, "NFO", "SONE-454", "SONE-454.nfo")]
    [InlineData("Preview", ".jpg", 1, "Previews", "SONE-454", "01.jpg")]
    [InlineData("Screenshot", ".jpg", 2, "Screenshots", "SONE-454", "02.jpg")]
    [InlineData("GIF", ".gif", 3, "GIF", "SONE-454", "03.gif")]
    public async Task AllResourcePathsUseNormalizedMovieNumber(
        string type,
        string extension,
        int? index,
        string directory,
        params string[] relativeSegments)
    {
        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(
            new MediaStorageMovie(3287, "  sone-454  ", "Example"), type, extension, index);

        Assert.Equal(Path.Combine(new[] { MediaRoot, directory }.Concat(relativeSegments).ToArray()), result.FullPath);
    }

    [Fact]
    public async Task UserConfiguredRootHasHighestPriority()
    {
        string custom = Path.Combine(root, "CustomMedia");
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(custom)));

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(custom, "Posters", "ABC-123.jpg"), result.FullPath);
    }

    [Fact]
    public async Task EmptyRootUsesRuntimeDefault()
    {
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize("")));

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(root, "Local Media Manager Next Data", "MediaStorage", "Posters", "ABC-123.jpg"), result.FullPath);
    }

    [Fact]
    public async Task EnsureDirectoryForWriteReportsInvalidRoot()
    {
        string blockedRoot = Path.Combine(root, "blocked-file");
        await File.WriteAllTextAsync(blockedRoot, "not a directory");
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(blockedRoot)));
        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.ThrowsAny<IOException>(() => Resolver().EnsureDirectoryForWrite(result.FullPath));
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }

    private MediaStoragePathResolver Resolver() => new(Database, InstallRoot);

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        return connection;
    }

    private static Task InsertMovieAsync(SqliteConnection connection, long id, string code, string title)
    {
        string at = DateTimeOffset.UtcNow.ToString("O");
        return ExecuteAsync(connection,
            "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES($id,$code,$title,0,0,'pending','Test',$at,$at)",
            ("$id", id), ("$code", code), ("$title", title), ("$at", at));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
