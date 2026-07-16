using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class LibraryWorkflowServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-library-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string MediaRoot => Path.Combine(root, "media");
    private LibraryWorkflowService service = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(MediaRoot);
        service = new LibraryWorkflowService(Database);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ScanImportsOnlyVideosRestoresRatingAndQueuesSync()
    {
        await File.WriteAllBytesAsync(Path.Combine(MediaRoot, "RESTORE-001.mp4"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(MediaRoot, "NEW-002.mkv"), [4, 5]);
        await File.WriteAllBytesAsync(Path.Combine(MediaRoot, "sample-ignore.avi"), [6]);
        await File.WriteAllTextAsync(Path.Combine(MediaRoot, "RESTORE-001.nfo"), "<movie />");
        await File.WriteAllTextAsync(Path.Combine(MediaRoot, "NEW-002.srt"), "subtitle");
        await using (var seed = await Open())
            await Execute(seed, "INSERT INTO DeletedRatingMemory(FileName,NormalizedFileName,Rating,RememberedAt) VALUES('RESTORE-001.mp4','restore-001.mp4',4.5,'2026-01-01')");

        LibraryMutationResult library = await service.CreateLibraryAsync(new(
            "Test Library", "Scan test", true,
            [new(MediaRoot, IncludeSubfolders: true, Enabled: true, ScanMode: "normal", ExcludePatterns: ["sample*"])]));
        ScanLaunchResult scan = await service.StartScanAsync(library.Id, new(FullScan: true, AutoSync: true));
        await service.RunQueuedScanForTestsAsync(scan.TaskId);
        string status = await WaitForTask(scan.TaskId);

        Assert.Equal("Completed", status);
        await using var connection = await Open();
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM Movies"));
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM MediaFiles WHERE MediaType='Video'"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM MediaFiles WHERE FileName LIKE 'sample%'"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM UserMovieState WHERE UserRating=4.5 AND HasUserRating=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM DeletedRatingMemory WHERE RestoredAt IS NOT NULL"));
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync' AND Status='Pending'"));
        Assert.True(await Scalar(connection, $"SELECT COUNT(*) FROM TaskLogs WHERE TaskId={scan.TaskId}") >= 2);
        await service.UpdateLibraryAsync(library.Id, new("Renamed Library", "Updated", true,
            [new(MediaRoot, IncludeSubfolders: true, Enabled: true, ScanMode: "normal", ExcludePatterns: ["sample*"])]));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM LibraryFolders WHERE LastScannedAt IS NOT NULL"));

        ScanLaunchResult repeat = await service.StartScanAsync(library.Id, new(FullScan: false, AutoSync: true));
        await service.RunQueuedScanForTestsAsync(repeat.TaskId);
        Assert.Equal("Completed", await WaitForTask(repeat.TaskId));
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM Movies"));
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync'"));
    }

    [Fact]
    public async Task LibraryDeleteRequiresPreviewBacksUpAndKeepsMovies()
    {
        await File.WriteAllBytesAsync(Path.Combine(MediaRoot, "KEEP-001.mp4"), [1]);
        LibraryMutationResult library = await service.CreateLibraryAsync(new(
            "Keep Movies", null, true, [new(MediaRoot)]));
        ScanLaunchResult scan = await service.StartScanAsync(library.Id, new(AutoSync: false));
        await service.RunQueuedScanForTestsAsync(scan.TaskId);
        Assert.Equal("Completed", await WaitForTask(scan.TaskId));

        LibraryDeletePreview preview = await service.PreviewDeleteLibraryAsync(library.Id);
        Assert.Equal(1, preview.LinkedFiles);
        await service.DeleteLibraryAsync(library.Id, new(preview.ConfirmationToken));

        await using var connection = await Open();
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM Libraries"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Movies"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MediaFiles WHERE LibraryId IS NULL"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "backups", "operations"), "library-delete-*.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ScanTaskLifecycleIsPersistentRecoverableAndIdempotent()
    {
        await File.WriteAllBytesAsync(Path.Combine(MediaRoot, "LIFE-001.mp4"), [1]);
        LibraryMutationResult library = await service.CreateLibraryAsync(new(
            "Lifecycle", null, true, [new(MediaRoot)]));

        ScanLaunchResult paused = await service.StartScanAsync(library.Id, new(AutoSync: true));
        Assert.Equal("Paused", (await service.PauseTaskAsync(paused.TaskId)).Status);
        Assert.False(await service.RunQueuedScanForTestsAsync(paused.TaskId));
        Assert.Equal("Paused", await ReadStatus(paused.TaskId));
        Assert.Equal("Pending", (await service.ResumeTaskAsync(paused.TaskId)).Status);
        Assert.True(await service.RunQueuedScanForTestsAsync(paused.TaskId));
        Assert.Equal("Completed", await ReadStatus(paused.TaskId));

        await using (var connection = await Open()) {
            Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Movies WHERE Code='LIFE-001'"));
            Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync' AND CurrentMovieId=(SELECT Id FROM Movies WHERE Code='LIFE-001')"));
        }

        ScanLaunchResult duplicate = await service.StartScanAsync(library.Id, new(AutoSync: true));
        Assert.True(await service.RunQueuedScanForTestsAsync(duplicate.TaskId));
        await using (var connection = await Open()) {
            Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Movies WHERE Code='LIFE-001'"));
            Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync' AND CurrentMovieId=(SELECT Id FROM Movies WHERE Code='LIFE-001')"));
        }

        ScanLaunchResult cancelled = await service.StartScanAsync(library.Id, new(AutoSync: true));
        Assert.Equal("Cancelled", (await service.CancelTaskAsync(cancelled.TaskId)).Status);
        Assert.False(await service.RunQueuedScanForTestsAsync(cancelled.TaskId));
        Assert.Equal("Retrying", (await service.RetryTaskAsync(cancelled.TaskId)).Status);
        Assert.True(await service.RunQueuedScanForTestsAsync(cancelled.TaskId));
        Assert.Equal("Completed", await ReadStatus(cancelled.TaskId));

        ScanLaunchResult interrupted = await service.StartScanAsync(library.Id, new(AutoSync: true));
        await using (var connection = await Open())
            await Execute(connection, $"UPDATE Tasks SET Status='Running',Stage='Running' WHERE Id={interrupted.TaskId}");
        var restarted = new LibraryWorkflowService(Database);
        await restarted.RecoverInterruptedScansForTestsAsync();
        Assert.Equal("Retrying", await ReadStatus(interrupted.TaskId));
        Assert.True(await restarted.RunQueuedScanForTestsAsync(interrupted.TaskId));

        ScanLaunchResult claimed = await service.StartScanAsync(library.Id, new(AutoSync: true));
        await using (var connection = await Open())
            await Execute(connection, $"UPDATE Tasks SET Status='Preparing',Stage='Preparing' WHERE Id={claimed.TaskId}");
        Assert.False(await service.RunQueuedScanForTestsAsync(claimed.TaskId));
    }

    private async Task<string> WaitForTask(long taskId)
    {
        for (int attempt = 0; attempt < 100; attempt++) {
            await Task.Delay(50);
            await using var connection = await Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Status FROM Tasks WHERE Id=$id";
            command.Parameters.AddWithValue("$id", taskId);
            string status = Convert.ToString(await command.ExecuteScalarAsync()) ?? "Missing";
            if (status is "Completed" or "Failed" or "Cancelled") return status;
        }
        return "Timeout";
    }

    private async Task<string> ReadStatus(long taskId)
    {
        await using var connection = await Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Tasks WHERE Id=$id";
        command.Parameters.AddWithValue("$id", taskId);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "Missing";
    }

    private async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
