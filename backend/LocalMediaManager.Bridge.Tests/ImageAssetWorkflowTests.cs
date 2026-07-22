using System.Net;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ImageAssetWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-image-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string ImageRoot => Path.Combine(root, "images");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0012_MediaStorageSettings.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await Execute(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'", ("$root", System.Text.Json.JsonSerializer.Serialize(Path.Combine(root, "MediaStorage"))));
    }

    [Fact]
    public async Task ValidatorRejectsHtmlEvenWhenServerClaimsJpeg()
    {
        string path = Path.Combine(root, "error.jpg");
        await File.WriteAllTextAsync(path, "<html><body>upstream error</body></html>");

        ImageValidationResult result = await ImageFileValidator.ValidateAsync(path, "image/jpeg");

        Assert.False(result.Valid);
        Assert.Equal("Corrupt", result.Status);
        Assert.Contains("HTML", result.Error);
    }

    [Fact]
    public async Task DownloadValidatesAtomicallyAndPreservesExistingImage()
    {
        byte[] png = CreatePng(48, 72, SKColors.CornflowerBlue);
        var service = new ImageDownloadService(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) {
            Content = new ByteArrayContent(png) { Headers = { ContentType = new("image/png") } }
        }));
        await using (SqliteConnection connection = await Open())
            await InsertMovie(connection, 1, "TEST-001", "Test Title");

        IReadOnlyList<SavedImage> first = await service.DownloadAsync(Resolver(), new(1, "TEST-001", "Test Title"), [new("Poster", "https://img.example/poster")], 10, false, CancellationToken.None);
        IReadOnlyList<SavedImage> second = await service.DownloadAsync(Resolver(), new(1, "TEST-001", "Test Title"), [new("Poster", "https://img.example/poster")], 10, false, CancellationToken.None);

        Assert.Single(first); Assert.True(first[0].Created); Assert.Equal(48, first[0].Width); Assert.Equal(72, first[0].Height);
        Assert.EndsWith(Path.Combine("MediaStorage", "Posters", "TEST-001.png"), first[0].Path, StringComparison.OrdinalIgnoreCase);
        Assert.Single(second); Assert.False(second[0].Created);
        string mediaRoot = Path.Combine(root, "MediaStorage");
        Assert.Empty(Directory.Exists(Path.Combine(mediaRoot, ".lmm-temp"))
            ? Directory.EnumerateFiles(Path.Combine(mediaRoot, ".lmm-temp"), "*", SearchOption.AllDirectories)
            : []);
    }

    [Fact]
    public async Task DownloadCanOverwriteExistingProviderImageForRescrape()
    {
        await using (SqliteConnection connection = await Open())
            await InsertMovie(connection, 9, "TEST-009", "Test Title");
        var firstService = new ImageDownloadService(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) {
            Content = new ByteArrayContent(CreatePng(48, 72, SKColors.CornflowerBlue)) { Headers = { ContentType = new("image/png") } }
        }));
        var secondService = new ImageDownloadService(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) {
            Content = new ByteArrayContent(CreatePng(64, 80, SKColors.IndianRed)) { Headers = { ContentType = new("image/png") } }
        }));

        IReadOnlyList<SavedImage> first = await firstService.DownloadAsync(Resolver(), new(9, "TEST-009", "Test Title"), [new("Poster", "https://img.example/old")], 10, false, CancellationToken.None);
        IReadOnlyList<SavedImage> second = await secondService.DownloadAsync(Resolver(), new(9, "TEST-009", "Test Title"), [new("Poster", "https://img.example/new")], 10, true, CancellationToken.None);

        Assert.True(first[0].Created);
        Assert.True(second[0].Created);
        Assert.Equal(first[0].Path, second[0].Path);
        Assert.Equal(64, second[0].Width);
        Assert.Equal(80, second[0].Height);
    }

    [Fact]
    public async Task FailedDownloadCleansOnlyTaskTemporaryFiles()
    {
        string protectedFile = Path.Combine(ImageRoot, "BigPic", "KEEP.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedFile)!);
        await File.WriteAllBytesAsync(protectedFile, CreatePng(20, 30, SKColors.Green));
        var service = new ImageDownloadService(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) {
            Content = new StringContent("<html>blocked</html>") { Headers = { ContentType = new("image/jpeg") } }
        }));
        await using (SqliteConnection connection = await Open())
            await InsertMovie(connection, 2, "TEST-002", "Test Title");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Resolver(), new(2, "TEST-002", "Test Title"), [new("Poster", "https://img.example/error")], 10, false, CancellationToken.None));

        Assert.True(File.Exists(protectedFile));
        Assert.Empty(Directory.Exists(Path.Combine(ImageRoot, ".lmm-temp"))
            ? Directory.EnumerateFiles(Path.Combine(ImageRoot, ".lmm-temp"), "*", SearchOption.AllDirectories)
            : []);
    }

    [Fact]
    public async Task GeneratedCardReplacementWritesToWallCropsDirectory()
    {
        string source = Path.Combine(root, "manual-crop.png");
        await File.WriteAllBytesAsync(source, CreatePng(64, 96, SKColors.Teal));
        await using (SqliteConnection connection = await Open())
            await InsertMovie(connection, 3, "WALL-001", "Wall Crop");
        var workflow = new ImageWorkflowService(Database, ImageRoot, Resolver());

        ImageMutationResult result = await workflow.ReplaceAsync(3, "GeneratedCard", source);

        Assert.True(result.Changed);
        await using SqliteConnection verify = await Open();
        string? savedPath = await Text(verify, "SELECT FilePath FROM Images WHERE MovieId=3 AND ImageType='GeneratedCard' ORDER BY Id DESC LIMIT 1");
        Assert.NotNull(savedPath);
        Assert.Contains(Path.Combine("MediaStorage", "WallCrops", "WALL-001"), savedPath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(savedPath));
    }

    [Fact]
    public async Task CropCardCreatesGeneratedCardWithoutChangingSource()
    {
        string source = Path.Combine(root, "source-poster.png");
        await File.WriteAllBytesAsync(source, CreatePng(100, 100, SKColors.SteelBlue));
        await using (SqliteConnection connection = await Open()) {
            await InsertMovie(connection, 4, "CROP-001", "Crop");
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Images(Id,MovieId,ImageType,FilePath,IsPrimary,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ValidationStatus) VALUES(44,4,'Poster',$path,1,$at,$at,'Legacy',0,0,'Unknown')", ("$path", source), ("$at", at));
        }
        var workflow = new ImageWorkflowService(Database, ImageRoot, Resolver());

        ImageMutationResult result = await workflow.CropCardAsync(4, new(44, 16.0 / 9.0, "center"));

        Assert.True(result.Changed);
        Assert.True(File.Exists(source));
        await using SqliteConnection verify = await Open();
        string? savedPath = await Text(verify, "SELECT FilePath FROM Images WHERE MovieId=4 AND ImageType='GeneratedCard' ORDER BY Id DESC LIMIT 1");
        Assert.NotNull(savedPath);
        Assert.Contains(Path.Combine("MediaStorage", "WallCrops", "CROP-001"), savedPath!, StringComparison.OrdinalIgnoreCase);
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(savedPath!);
        Assert.True(validation.Valid);
        Assert.Equal(100, validation.Width);
        Assert.Equal(56, validation.Height);

        var assets = new ImageAssetService(Database, ImageRoot);
        Assert.NotNull(await assets.ResolveMovieAsync(4, "thumbnail"));
        Assert.Contains(savedPath!, await Text(verify, "SELECT SourceImagePath FROM ImageCacheEntries WHERE MovieId=4 AND CacheKind='CardThumbnail' ORDER BY Id DESC LIMIT 1"));
    }

    [Fact]
    public async Task LockPersistsAndCacheCleanupNeverDeletesSource()
    {
        string source = Path.Combine(root, "MediaStorage", "Covers", "LOCK-001", "LOCK-001.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, CreatePng(80, 120, SKColors.Orange));
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'LOCK-001','Locked',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Images(Id,MovieId,ImageType,FilePath,IsPrimary,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ValidationStatus) VALUES(1,1,'Poster',$path,1,$at,$at,'Legacy',0,0,'Unknown')", ("$path", source), ("$at", at));
        }
        var service = new ImageAssetService(Database, ImageRoot);

        ImageMutationResult locked = await service.SetLockAsync(1, true);
        ImageAssetContent? thumbnail = await service.ResolveMovieAsync(1, "thumbnail");
        ImageCachePreview preview = await service.PreviewCacheCleanupAsync();
        ImageCacheCleanupResult cleanup = await service.CleanupCacheAsync(preview.ConfirmationToken);

        Assert.True(locked.Changed); Assert.NotNull(thumbnail); Assert.True(preview.Entries >= 1);
        Assert.True(cleanup.DeletedEntries >= 1); Assert.True(File.Exists(source));
        await using SqliteConnection verify = await Open();
        Assert.Equal(1, await Scalar(verify, "SELECT IsLocked FROM Images WHERE Id=1"));
        Assert.Equal("User", await Text(verify, "SELECT Ownership FROM Images WHERE Id=1"));
    }

    [Fact]
    public async Task RebuildTaskImportsLegacyActorPortraitAndCompletesThroughTaskCenter()
    {
        string source = Path.Combine(root, "MediaStorage", "Covers", "TASK-001", "TASK-001.png");
        string portrait = Path.Combine(ImageRoot, "Actresses", "7_Alice.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(portrait)!);
        await File.WriteAllBytesAsync(source, CreatePng(80, 120, SKColors.Purple));
        await File.WriteAllBytesAsync(portrait, CreatePng(60, 60, SKColors.Pink));
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'TASK-001','Task',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,LegacyId,CreatedAt,UpdatedAt) VALUES(1,'Alice','ALICE','Jvedio5',7,$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Images(MovieId,ImageType,FilePath,IsPrimary,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ValidationStatus) VALUES(1,'Poster',$path,1,$at,$at,'Legacy',0,0,'Unknown')", ("$path", source), ("$at", at));
        }
        var assets = new ImageAssetService(Database, ImageRoot);
        var service = new ImageCacheTaskService(Database, assets, new TaskLogService(Database));

        ImageCacheRebuildLaunchResult launch = await service.EnqueueAsync();
        await service.StartAsync(CancellationToken.None);
        string? status = null;
        for (int attempt = 0; attempt < 50 && status != "Completed"; attempt++) {
            await Task.Delay(50);
            await using SqliteConnection check = await Open();
            status = await Text(check, $"SELECT Status FROM Tasks WHERE Id={launch.TaskId}");
        }
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("Completed", status);
        await using SqliteConnection verify = await Open();
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Images WHERE ActorId=1 AND ImageType='ActorAvatar'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM ImageCacheEntries WHERE MovieId=1 AND CacheKind='CardThumbnail'"));
        Assert.True(await Scalar(verify, $"SELECT COUNT(*) FROM TaskLogs WHERE TaskId={launch.TaskId}") >= 2);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(portrait));
    }

    [Fact]
    public async Task LegacyImageRecordsAreIgnoredForRuntimeDisplay()
    {
        string legacy = Path.Combine(ImageRoot, "BigPic", "OLD-001.png");
        string current = Path.Combine(root, "MediaStorage", "Covers", "OLD-001", "OLD-001.png");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        await File.WriteAllBytesAsync(legacy, CreatePng(80, 120, SKColors.Gray));
        await File.WriteAllBytesAsync(current, CreatePng(80, 120, SKColors.Goldenrod));
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(20,'OLD-001','Old',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Images(Id,MovieId,ImageType,FilePath,IsPrimary,SourceProvider,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ValidationStatus) VALUES(201,20,'Poster',$path,1,'LegacyFile',$at,$at,'Legacy',0,0,'Unknown')", ("$path", legacy), ("$at", at));
            await Execute(connection, "INSERT INTO Images(Id,MovieId,ImageType,FilePath,IsPrimary,SourceProvider,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ValidationStatus) VALUES(202,20,'Poster',$path,1,'MetaTube',$at,$at,'Provider',0,0,'Unknown')", ("$path", current), ("$at", at));
        }
        var service = new ImageAssetService(Database, ImageRoot);

        IReadOnlyList<ImageAssetDto> assets = await service.ReadMovieAssetsAsync(20, "http://localhost");
        ImageAssetContent? oldAsset = await service.ResolveAssetAsync(201);
        ImageAssetContent? currentAsset = await service.ResolveMovieAsync(20, "original");

        Assert.Single(assets);
        Assert.Equal(202, assets[0].Id);
        Assert.Null(oldAsset);
        Assert.NotNull(currentAsset);
        Assert.Equal(current, currentAsset!.Path);
    }

    [Fact]
    public async Task LocalReplaceLocksImageAndDeleteUsesPreviewToken()
    {
        string source = Path.Combine(root, "local.png");
        await File.WriteAllBytesAsync(source, CreatePng(64, 96, SKColors.DarkCyan));
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'IMG-001','Image',0,0,'pending','Test',$at,$at)", ("$at", at));
        }
        var service = new ImageWorkflowService(Database, ImageRoot, Resolver());

        ImageMutationResult replaced = await service.ReplaceAsync(1, "Poster", source);
        long imageId;
        string? copied;
        await using (SqliteConnection verifyReplace = await Open())
        {
            imageId = await Scalar(verifyReplace, "SELECT Id FROM Images WHERE MovieId=1 AND ImageType='Poster'");
            copied = await Text(verifyReplace, "SELECT FilePath FROM Images WHERE MovieId=1 AND ImageType='Poster'");
            Assert.True(replaced.Changed);
            Assert.Equal(1, await Scalar(verifyReplace, "SELECT IsLocked FROM Images WHERE Id=" + imageId));
            Assert.True(File.Exists(copied));
        }

        ImageDeletePreview preview = await service.PreviewDeleteAsync(imageId);
        ImageMutationResult deleted = await service.DeleteAsync(imageId, preview.ConfirmationToken);

        await using SqliteConnection verifyDelete = await Open();
        Assert.True(deleted.Changed);
        Assert.Equal(0, await Scalar(verifyDelete, "SELECT COUNT(*) FROM Images WHERE Id=" + imageId));
        Assert.False(File.Exists(copied));
    }

    [Fact]
    public async Task ImageGenerationEnqueuesTaskCenterItem()
    {
        string video = Path.Combine(root, "movie.mp4");
        await File.WriteAllBytesAsync(video, new byte[] { 0, 1, 2, 3, 4 });
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'GEN-001','Generate',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO MediaFiles(Id,MovieId,FilePath,NormalizedPath,FileName,Extension,MediaType,SourceType,FileSize,ExistsState,IsPrimary,CreatedAt,UpdatedAt) VALUES(1,1,$path,$path,'movie.mp4','.mp4','Video','Test',5,'Exists',1,$at,$at)", ("$path", video), ("$at", at));
        }
        var workflow = new ImageWorkflowService(Database, ImageRoot, Resolver());
        var generator = new ImageGenerationTaskService(Database, Resolver(), workflow, new TaskLogService(Database), new FfmpegLocator(Database, root));

        ImageTaskLaunchResult launch = await generator.EnqueueAsync(1, "Screenshot");

        Assert.Equal("Screenshot", launch.Type);
        await using SqliteConnection verify = await Open();
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE Id=" + launch.TaskId + " AND TaskType='Screenshot' AND Status='Pending'"));
        Assert.True(await Scalar(verify, $"SELECT COUNT(*) FROM TaskLogs WHERE TaskId={launch.TaskId}") >= 1);
    }

    public Task DisposeAsync() { try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private MediaStoragePathResolver Resolver() => new(Database, root);
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static Task InsertMovie(SqliteConnection connection, long id, string code, string title)
    {
        string at = DateTimeOffset.UtcNow.ToString("O");
        return Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES($id,$code,$title,0,0,'pending','Test',$at,$at)", ("$id", id), ("$code", code), ("$title", title), ("$at", at));
    }
    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] values) { await using var command=connection.CreateCommand();command.CommandText=sql;foreach((string name,object? value) in values)command.Parameters.AddWithValue(name,value??DBNull.Value);await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L); }
    private static async Task<string?> Text(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return (await command.ExecuteScalarAsync())?.ToString(); }
    private static byte[] CreatePng(int width, int height, SKColor color) { using var bitmap=new SKBitmap(width,height);using(var canvas=new SKCanvas(bitmap)){canvas.Clear(color);}using SKImage image=SKImage.FromBitmap(bitmap);using SKData data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray(); }

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response), disposeHandler: true);
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
        }
    }
}
