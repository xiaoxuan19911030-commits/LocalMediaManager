using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class LibraryTypeMigrationTests
{
    [Fact]
    public async Task MigrationDefaultsExistingLibrariesAndMoviesAndEnforcesEnums()
    {
        string database = Path.Combine(Path.GetTempPath(), $"lmm-library-type-{Guid.NewGuid():N}.db");
        try {
            await using var connection = new SqliteConnection($"Data Source={database}");
            await connection.OpenAsync();
            await ExecuteAsync(connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "0001_InitialSchema.sql")));
            await ExecuteAsync(connection, "INSERT INTO Libraries(Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt) VALUES('Existing',1,0,'now','now'); INSERT INTO Movies(Code,Title,DurationSeconds,IsScraped,ScrapeStatus,CreatedAt,UpdatedAt) VALUES('OLD-001','Old',0,0,'pending','now','now');");
            await ExecuteAsync(connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "0015_LibraryTypesAndLocalMedia.sql")));

            Assert.Equal("Standard", await ScalarTextAsync(connection, "SELECT LibraryType FROM Libraries"));
            Assert.Equal("None", await ScalarTextAsync(connection, "SELECT ScreenshotStatus FROM Movies"));
            Assert.Equal("None", await ScalarTextAsync(connection, "SELECT CoverSource FROM Movies"));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "UPDATE Libraries SET LibraryType='Music'"));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "UPDATE Movies SET ScreenshotStatus='Unknown'"));
        } finally {
            SqliteConnection.ClearAllPools();
            try { File.Delete(database); } catch { }
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
