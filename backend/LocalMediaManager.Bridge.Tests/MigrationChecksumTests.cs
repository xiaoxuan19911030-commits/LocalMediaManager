using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MigrationChecksumTests
{
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public async Task AppliedMigrationAcceptsEquivalentLineEndings(string recordedLineEnding, string fileLineEnding)
    {
        const string sql = "CREATE TABLE Sample(\n    Id INTEGER PRIMARY KEY\n);\n";
        await WithDatabaseAsync(async (connection, directory) => {
            string recordedSql = sql.Replace("\n", recordedLineEnding);
            await RecordMigrationAsync(connection, 14, Hash(recordedSql));
            await File.WriteAllTextAsync(Path.Combine(directory, "0014_Sample.sql"), sql.Replace("\n", fileLineEnding));

            IReadOnlyList<int> applied = await DatabaseUpgradeRunner.ApplyPendingAsync(connection, directory);

            Assert.Empty(applied);
        });
    }

    [Fact]
    public async Task AppliedMigrationStillRejectsSqlContentChanges()
    {
        const string original = "CREATE TABLE Sample(Id INTEGER PRIMARY KEY);\n";
        await WithDatabaseAsync(async (connection, directory) => {
            await RecordMigrationAsync(connection, 14, Hash(original));
            await File.WriteAllTextAsync(Path.Combine(directory, "0014_Sample.sql"),
                "CREATE TABLE Sample(Id TEXT PRIMARY KEY);\r\n");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                DatabaseUpgradeRunner.ApplyPendingAsync(connection, directory));
        });
    }

    [Fact]
    public async Task NewMigrationStoresCanonicalLineEndingChecksum()
    {
        const string sql = "CREATE TABLE Sample(\n    Id INTEGER PRIMARY KEY\n);\n";
        await WithDatabaseAsync(async (connection, directory) => {
            await File.WriteAllTextAsync(Path.Combine(directory, "0014_Sample.sql"), sql.Replace("\n", "\r\n"));

            IReadOnlyList<int> applied = await DatabaseUpgradeRunner.ApplyPendingAsync(connection, directory);

            Assert.Equal([14], applied);
            Assert.Equal(Hash(sql), await ScalarAsync(connection, "SELECT Checksum FROM SchemaMigrations WHERE Version=14"));
        });
    }

    private static async Task WithDatabaseAsync(Func<SqliteConnection, string, Task> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "lmm-migration-checksum-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await ExecuteAsync(connection, "CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL, Checksum TEXT NOT NULL);");
            await action(connection, directory);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RecordMigrationAsync(SqliteConnection connection, int version, string checksum)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SchemaMigrations(Version,Name,AppliedAt,Checksum) VALUES($version,'0014_Sample','now',$checksum)";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$checksum", checksum);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
