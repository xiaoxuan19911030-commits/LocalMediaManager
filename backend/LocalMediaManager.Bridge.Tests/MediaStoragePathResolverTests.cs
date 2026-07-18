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
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql", "0012_MediaStorageSettings.sql" })
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(MediaRoot)));
        await InsertMovieAsync(connection, 1, "ABC-123", "Example Movie");
    }

    [Theory]
    [InlineData("Poster", "Posters", "ABC-123.jpg")]
    [InlineData("Thumbnail", "Thumbnails", "ABC-123.jpg")]
    [InlineData("Fanart", "Fanart", "ABC-123.jpg")]
    [InlineData("Preview", "Previews", "ABC-123_001.jpg")]
    [InlineData("Screenshot", "Screenshots", "ABC-123_001.jpg")]
    [InlineData("GIF", "GIF", "ABC-123_001.gif")]
    [InlineData("NFO", "NFO", "ABC-123.nfo")]
    [InlineData("GeneratedCard", "WallCrops", "ABC-123.jpg")]
    [InlineData("CardCover", "WallCrops", "ABC-123.jpg")]
    public async Task ResolvesMediaStoragePathForResourceType(string type, string directory, string fileName)
    {
        string extension = type.Equals("GIF", StringComparison.OrdinalIgnoreCase) ? ".gif"
            : type.Equals("NFO", StringComparison.OrdinalIgnoreCase) ? ".nfo"
            : ".jpg";
        int? index = type is "Preview" or "Screenshot" or "GIF" ? 1 : null;

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, type, extension, index);

        Assert.Equal(Path.Combine(MediaRoot, directory, "ABC-123", fileName), result.FullPath);
        Assert.Equal(directory, result.Directory);
    }

    [Fact]
    public async Task ResolveDoesNotCreateDirectoriesUntilWriteIsRequested()
    {
        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.False(Directory.Exists(Path.GetDirectoryName(result.FullPath)!));
        Resolver().EnsureDirectoryForWrite(result.FullPath);
        Assert.True(Directory.Exists(Path.GetDirectoryName(result.FullPath)!));
    }

    [Fact]
    public async Task UserConfiguredRootHasHighestPriority()
    {
        string custom = Path.Combine(root, "CustomMedia");
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(custom)));

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(custom, "Posters", "ABC-123", "ABC-123.jpg"), result.FullPath);
    }

    [Fact]
    public async Task EmptyRootUsesRuntimeDefault()
    {
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize("")));

        MediaStorageResourcePath result = await Resolver().ResolveForMovieAsync(1, "Poster", ".jpg");

        Assert.Equal(Path.Combine(root, "Local Media Manager Next Data", "MediaStorage", "Posters", "ABC-123", "ABC-123.jpg"), result.FullPath);
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
