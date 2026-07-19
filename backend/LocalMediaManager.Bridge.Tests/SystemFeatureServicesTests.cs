using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class SystemFeatureServicesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-system-feature-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LogCleanupDeletesOldHistoryButKeepsActiveLogs()
    {
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        string active = Path.Combine(logs, "bridge.log");
        string old = Path.Combine(logs, "bridge.log.1");
        string recent = Path.Combine(logs, "migration.log.1");
        File.WriteAllText(active, "active");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-45));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-2));
        var service = new LogMaintenanceService(logs);

        LogCleanupPreviewDto preview = service.Preview(30);
        LogCleanupResult result = service.Cleanup(new(30, false, preview.ConfirmationToken));

        Assert.Equal(1, result.DeletedFiles);
        Assert.True(result.FreedBytes > 0);
        Assert.True(File.Exists(active));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void LogCleanupIncludeAllHistoryStillKeepsActiveLogs()
    {
        string logs = Path.Combine(root, "logs-all");
        Directory.CreateDirectory(logs);
        string active = Path.Combine(logs, "migration.log");
        string history = Path.Combine(logs, "custom.log.1");
        File.WriteAllText(active, "active");
        File.WriteAllText(history, "history");
        var service = new LogMaintenanceService(logs);

        LogCleanupPreviewDto preview = service.Preview(7, includeAllHistory: true);
        LogCleanupResult result = service.Cleanup(new(7, true, preview.ConfirmationToken));

        Assert.Equal(1, result.DeletedFiles);
        Assert.True(File.Exists(active));
        Assert.False(File.Exists(history));
    }

    [Fact]
    public void LogCleanupPermanentRetentionDoesNotDeleteHistoryByAge()
    {
        string logs = Path.Combine(root, "logs-permanent");
        Directory.CreateDirectory(logs);
        string old = Path.Combine(logs, "bridge.log.2");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-365));
        var service = new LogMaintenanceService(logs);

        LogCleanupPreviewDto preview = service.Preview(0);
        LogCleanupResult result = service.Cleanup(new(0, false, preview.ConfirmationToken));

        Assert.Equal(0, preview.DeletableCount);
        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(old));
    }

    [Theory]
    [InlineData("0.4.3", "0.4.3", 0)]
    [InlineData("0.4.3", "0.4.4", -1)]
    [InlineData("0.5.0", "0.4.3", 1)]
    public void UpdateVersionComparisonHandlesCurrentNewerAndAvailable(string current, string latest, int expected)
    {
        Assert.Equal(expected, Math.Sign(UpdateCheckService.CompareVersions(current, latest)));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }
}
