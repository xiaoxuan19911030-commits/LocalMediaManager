using System.IO.Compression;
using System.Text.Json;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class DataSafetyServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-data-safety-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "data", "LocalMediaManager.db");
    private string Config => Path.Combine(root, "config", "app_configs.sqlite");
    private string Images => Path.Combine(root, "images");
    private DataSafetyService Service => new(Database, Config, Images);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        Directory.CreateDirectory(Images);
        await using (var connection = new SqliteConnection($"Data Source={Database}")) {
            await connection.OpenAsync();
            foreach (string file in new[] { "0001_InitialSchema.sql", "0005_MetadataSyncWorkflow.sql", "0009_PlaybackSettings.sql" }) {
                await using var command = connection.CreateCommand();
                command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
                await command.ExecuteNonQueryAsync();
            }
        }
        await using (var connection = new SqliteConnection($"Data Source={Config}")) {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE app_configs(ConfigName TEXT, ConfigValue TEXT); INSERT INTO app_configs VALUES('ProxyConfig',$json)";
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(new { Password = "secret", HttpTimeout = 30 }));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SettingsExportMasksSensitiveFields()
    {
        SettingsExportDto export = await Service.ExportSettingsAsync(new(true, "http://127.0.0.1:8080/", 30, true, false, false, true));
        string json = JsonSerializer.Serialize(export);

        Assert.DoesNotContain("ProxyConfig.Password", json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public async Task SettingsImportPreviewReportsChanges()
    {
        var payload = JsonSerializer.SerializeToElement(new { settings = new { fields = new[] { new { key = "Search.DefaultSort", category = "search" } } } });

        SettingsImportPreviewDto preview = await Service.PreviewSettingsImportAsync(payload);

        Assert.True(preview.Valid);
        Assert.Contains("search", preview.Categories);
        Assert.Contains("Search.DefaultSort", preview.Changes);
    }

    [Fact]
    public async Task BackupCreateAndValidateIncludesDatabaseAndConfig()
    {
        BackupResultDto created = await Service.CreateBackupAsync(new(true, false));
        BackupValidationDto validation = await Service.ValidateBackupAsync(created.BackupPath);

        Assert.True(File.Exists(created.BackupPath));
        Assert.True(validation.Valid);
        Assert.True(validation.HasDatabase);
        Assert.True(validation.HasConfig);
    }

    [Fact]
    public async Task BackupIncludesCommittedWalRowsThroughSqliteSnapshot()
    {
        await using var writer = new SqliteConnection($"Data Source={Database}");
        await writer.OpenAsync();
        await using (SqliteCommand command = writer.CreateCommand()) {
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE BackupMarker(Value TEXT); INSERT INTO BackupMarker VALUES('committed-in-wal');";
            await command.ExecuteNonQueryAsync();
        }

        BackupResultDto created = await Service.CreateBackupAsync(new(false, false));
        string extracted = Path.Combine(root, "extracted-backup.db");
        using (ZipArchive archive = ZipFile.OpenRead(created.BackupPath))
            archive.GetEntry("database/LocalMediaManager.db")!.ExtractToFile(extracted);
        await using var snapshot = new SqliteConnection($"Data Source={extracted};Mode=ReadOnly");
        await snapshot.OpenAsync();
        await using SqliteCommand verify = snapshot.CreateCommand();
        verify.CommandText = "SELECT Value FROM BackupMarker";

        Assert.Equal("committed-in-wal", await verify.ExecuteScalarAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(Database)!, "backups"),
            ".lmm-database-*.sqlite"));
    }

    [Fact]
    public async Task RestorePlanRequiresValidBackupAndWritesPlan()
    {
        BackupResultDto created = await Service.CreateBackupAsync(new(true, false));

        RestorePlanDto plan = await Service.CreateRestorePlanAsync(new(created.BackupPath, "database"));

        Assert.True(File.Exists(plan.PlanPath));
        Assert.Equal("database", plan.Mode);
        Assert.Contains(plan.Warnings, item => item.Contains("数据库"));
    }

    [Fact]
    public async Task RestorePlanAcceptsLegacyModeAliases()
    {
        BackupResultDto created = await Service.CreateBackupAsync(new(true, false));

        RestorePlanDto plan = await Service.CreateRestorePlanAsync(new(created.BackupPath, "database-only"));

        Assert.Equal("database", plan.Mode);
    }

    [Fact]
    public async Task RestorePlanRejectsUnknownMode()
    {
        BackupResultDto created = await Service.CreateBackupAsync(new(true, false));

        await Assert.ThrowsAsync<ArgumentException>(() => Service.CreateRestorePlanAsync(new(created.BackupPath, "everything")));
    }

    [Fact]
    public async Task InvalidBackupIsRejected()
    {
        string invalid = Path.Combine(root, "bad.zip");
        using (ZipFile.Open(invalid, ZipArchiveMode.Create)) { }

        BackupValidationDto validation = await Service.ValidateBackupAsync(invalid);

        Assert.False(validation.Valid);
        Assert.Contains(validation.Errors, item => item.Contains("数据库"));
    }

    [Fact]
    public async Task DiagnosticsReturnCoreChecks()
    {
        SystemDiagnosticDto result = await Service.DiagnosticsAsync();

        Assert.Contains(result.Checks, item => item.Key == "database");
        Assert.Contains(result.Checks, item => item.Key == "integrity" && item.Status == "success");
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
