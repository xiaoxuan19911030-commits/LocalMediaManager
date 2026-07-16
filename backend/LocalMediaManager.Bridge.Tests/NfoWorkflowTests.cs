using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class NfoWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-nfo-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string Video => Path.Combine(root, "media", "SPECIAL-001.mp4");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Video)!);
        await File.WriteAllBytesAsync(Video, [0, 1, 2, 3]);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,OriginalTitle,Description,ReleaseDate,DurationSeconds,ProviderRating,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'SPECIAL-001','标题 & <测试>','Original \"Title\"','Unicode 简介：日本語', '2026-07-16',5400,4.25,1,'complete','Test',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt) VALUES(1,$path,$path,'SPECIAL-001.mp4','.mp4',4,'Video','Local',1,'Present',5400,$at,$at)", ("$path", Video), ("$at", at));
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'演员 A','演员 A','Test',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,1,'',0)");
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'自定义标签','自定义标签','User',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'剧情','剧情'); INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");
        await Execute(connection, "INSERT INTO Series(Id,Name,NormalizedName) VALUES(1,'系列 Ω','系列 Ω'); INSERT INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES(1,1,0)");
        await Execute(connection, "INSERT INTO Studios(Id,Name,NormalizedName) VALUES(1,'厂商 & Co','厂商 & CO'); INSERT INTO MovieStudios(MovieId,StudioId,RelationType) VALUES(1,1,'Studio')");
    }

    [Fact]
    public async Task ExportAndParseRoundTripPreservesUnicodeRelationshipsAndEscaping()
    {
        var service = new NfoService(Database);
        NfoPreview preview = await service.PreviewExportAsync(1);
        NfoMutationResult result = await service.ExportAsync(1, preview.ConfirmationToken);
        NfoData parsed = await NfoService.ParseAsync(result.Path);

        Assert.True(result.Changed); Assert.Equal("LMM", result.Ownership); Assert.False(result.Locked);
        Assert.Equal("标题 & <测试>", parsed.Title); Assert.Equal("Original \"Title\"", parsed.OriginalTitle);
        Assert.Equal("Unicode 简介：日本語", parsed.Plot); Assert.Equal(4.25, parsed.Rating);
        Assert.Contains("演员 A", parsed.Actors); Assert.Contains("自定义标签", parsed.Tags);
        Assert.Contains("剧情", parsed.Genres); Assert.Contains("系列 Ω", parsed.Series);
        Assert.Equal("厂商 & Co", parsed.Studio);
        await using SqliteConnection verify = await Open();
        Assert.Equal("LMM", await Text(verify, "SELECT Ownership FROM NfoDocuments WHERE MovieId=1"));
        Assert.Equal(0, await Scalar(verify, "SELECT IsLocked FROM NfoDocuments WHERE MovieId=1"));
    }

    [Fact]
    public async Task ExistingUserNfoIsLockedAndCanOnlyExportToSeparateFile()
    {
        string path = Path.ChangeExtension(Video, ".nfo");
        const string original = "<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><title>User title</title><id>SPECIAL-001</id></movie>";
        await File.WriteAllTextAsync(path, original);
        var service = new NfoService(Database);

        NfoPreview preview = await service.PreviewExportAsync(1);
        NfoMutationResult skipped = await service.ExportAsync(1, preview.ConfirmationToken);
        NfoPreview secondPreview = await service.PreviewExportAsync(1);
        NfoMutationResult separate = await service.ExportAsync(1, secondPreview.ConfirmationToken, true);

        Assert.False(skipped.Changed); Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.EndsWith(".lmm.nfo", separate.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(separate.Path)); Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ImportFillsEmptyFieldsButReportsAndPreservesConflicts()
    {
        string path = Path.ChangeExtension(Video, ".nfo");
        await File.WriteAllTextAsync(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <movie><title>Conflicting title</title><id>OTHER-999</id><plot>Imported plot</plot>
            <actor><name>演员 B</name></actor><tag>导入标签</tag><genre>喜剧</genre></movie>
            """);
        await using (SqliteConnection connection = await Open())
            await Execute(connection, "UPDATE Movies SET Description=NULL,NfoPath=$path WHERE Id=1", ("$path", path));
        var service = new NfoService(Database);

        NfoPreview preview = await service.PreviewImportAsync(1);
        NfoMutationResult result = await service.ImportAsync(1, preview.ConfirmationToken);

        Assert.Contains("标题", preview.Conflicts); Assert.Contains("番号", preview.Conflicts);
        Assert.True(result.Changed); Assert.Equal("User", result.Ownership); Assert.True(result.Locked);
        await using SqliteConnection verify = await Open();
        Assert.Equal("SPECIAL-001", await Text(verify, "SELECT Code FROM Movies WHERE Id=1"));
        Assert.Equal("标题 & <测试>", await Text(verify, "SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal("Imported plot", await Text(verify, "SELECT Description FROM Movies WHERE Id=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Actors a JOIN MovieActors ma ON ma.ActorId=a.Id WHERE ma.MovieId=1 AND a.Name='演员 B'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Tags t JOIN MovieTags mt ON mt.TagId=t.Id WHERE mt.MovieId=1 AND t.Name='导入标签'"));
    }

    [Fact]
    public async Task MalformedNfoDoesNotModifyDatabase()
    {
        string path = Path.ChangeExtension(Video, ".nfo");
        await File.WriteAllTextAsync(path, "<movie><title>broken");
        await using (SqliteConnection connection = await Open())
            await Execute(connection, "UPDATE Movies SET NfoPath=$path WHERE Id=1", ("$path", path));
        var service = new NfoService(Database);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewImportAsync(1));

        await using SqliteConnection verify = await Open();
        Assert.Equal("标题 & <测试>", await Text(verify, "SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM NfoDocuments"));
    }

    public Task DisposeAsync() { try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] values) { await using var command=connection.CreateCommand();command.CommandText=sql;foreach((string name,object? value) in values)command.Parameters.AddWithValue(name,value??DBNull.Value);await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync()??0L); }
    private static async Task<string?> Text(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return(await command.ExecuteScalarAsync())?.ToString(); }
}
