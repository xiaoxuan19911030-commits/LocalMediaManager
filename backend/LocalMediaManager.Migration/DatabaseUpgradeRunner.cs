using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

internal static class DatabaseUpgradeRunner
{
    internal static async Task<DatabaseUpgradeReport> UpgradeAsync(string dataRoot, bool confirm)
    {
        string database = Path.Combine(dataRoot, "data", "LocalMediaManager.db");
        if (!File.Exists(database)) throw new FileNotFoundException("Local Media Manager database was not found.", database);
        if (!confirm) return new(database, null, [], "Preview", "Use --confirm to apply pending migrations.");

        string stamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        string backupDirectory = Path.Combine(dataRoot, "data", "backups", "database", $"upgrade_{stamp}");
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(backupDirectory, "LocalMediaManager.db");
        File.Copy(database, backup, overwrite: false);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        IReadOnlyList<int> applied = await ApplyPendingAsync(connection, Path.Combine(AppContext.BaseDirectory, "migrations"));
        string integrity = Convert.ToString(await ScalarAsync(connection, "PRAGMA integrity_check")) ?? "unknown";
        long foreignKeyErrors = await CountRowsAsync(connection, "PRAGMA foreign_key_check");
        if (!integrity.Equals("ok", StringComparison.OrdinalIgnoreCase) || foreignKeyErrors != 0)
            throw new InvalidDataException($"Database validation failed: integrity={integrity}, foreignKeys={foreignKeyErrors}.");
        return new(database, backup, applied, "Completed", $"integrity={integrity}; foreignKeys={foreignKeyErrors}");
    }

    internal static async Task<IReadOnlyList<int>> ApplyPendingAsync(SqliteConnection connection, string migrationDirectory)
    {
        var applied = new List<int>();
        var existing = new Dictionary<int, string>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = "SELECT Version,Checksum FROM SchemaMigrations ORDER BY Version";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existing[reader.GetInt32(0)] = reader.GetString(1);
        }

        foreach (string file in Directory.GetFiles(migrationDirectory, "*.sql").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(name.Split('_', 2)[0], out int version)) continue;
            string sql = await File.ReadAllTextAsync(file);
            string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
            if (existing.TryGetValue(version, out string? recordedChecksum)) {
                // Versions 1-13 predate checksum enforcement and their source snapshots have historical drift.
                // Never rewrite those records; enforce immutability for 0014 and every subsequent migration.
                if (version >= 14 && !recordedChecksum.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Migration checksum mismatch: version={version}, name={name}.");
                continue;
            }
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var migration = connection.CreateCommand()) {
                migration.Transaction = (SqliteTransaction)transaction;
                migration.CommandText = sql;
                await migration.ExecuteNonQueryAsync();
            }
            await using (var record = connection.CreateCommand()) {
                record.Transaction = (SqliteTransaction)transaction;
                record.CommandText = "INSERT INTO SchemaMigrations(Version,Name,AppliedAt,Checksum) VALUES($version,$name,$at,$checksum)";
                record.Parameters.AddWithValue("$version", version);
                record.Parameters.AddWithValue("$name", name);
                record.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                record.Parameters.AddWithValue("$checksum", checksum);
                await record.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            existing[version] = checksum;
            applied.Add(version);
        }
        return applied;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> CountRowsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        long count = 0;
        while (await reader.ReadAsync()) count++;
        return count;
    }
}

internal sealed record DatabaseUpgradeReport(
    string Database,
    string? Backup,
    IReadOnlyList<int> AppliedVersions,
    string Status,
    string Validation);

