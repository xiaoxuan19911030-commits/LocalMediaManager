using Microsoft.Data.Sqlite;
using System.Text.Json;

string dataRoot = Environment.GetEnvironmentVariable("LMM_NEXT_DATA_ROOT") ?? @"D:\自用软件\部署安装目录\本地媒体管理器\数据";
string legacyRoot = Environment.GetEnvironmentVariable("LMM_LEGACY_ROOT") ?? dataRoot;
string userRoot = Path.Combine(legacyRoot, "data", Environment.UserName);
string businessDb = Environment.GetEnvironmentVariable("LMM_LEGACY_DATABASE_PATH") ?? Path.Combine(userRoot, "app_datas.sqlite");
string configDb = Environment.GetEnvironmentVariable("LMM_LEGACY_CONFIG_DATABASE_PATH") ?? Path.Combine(userRoot, "app_configs.sqlite");
string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "analysis"));
string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "analyze";

if (command == "migrate") {
    bool confirmSwitch = args.Contains("--confirm-switch", StringComparer.OrdinalIgnoreCase);
    string imageRoot = Environment.GetEnvironmentVariable("LMM_IMAGE_ROOT") ?? Path.Combine(dataRoot, "MediaStorage");
    try {
        MigrationReport migration = await MigrationRunner.RunAsync(businessDb, configDb, imageRoot, dataRoot, confirmSwitch);
        Console.WriteLine(JsonSerializer.Serialize(migration, new JsonSerializerOptions { WriteIndented = true }));
        return migration.AllowedToSwitch ? 0 : 3;
    } catch (Exception exception) {
        string failedAt = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        string reportDirectory = Path.Combine(dataRoot, "data", "reports", $"failed_{failedAt}");
        Directory.CreateDirectory(reportDirectory);
        string officialDatabase = Path.Combine(dataRoot, "data", "LocalMediaManager.db");
        var failure = new {
            ToolVersion = "0.7.8",
            Status = "Failed",
            FailedAt = DateTimeOffset.Now,
            LegacyBusinessDatabase = businessDb,
            LegacyConfigDatabase = configDb,
            OfficialDatabase = officialDatabase,
            OfficialDatabasePreserved = File.Exists(officialDatabase),
            ErrorType = exception.GetType().Name,
            ErrorMessage = exception.Message,
        };
        string json = JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "migration-failure.json"), json);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "migration-failure.md"),
            $"# Migration failure\n\n- Status: Failed\n- Time: {failure.FailedAt:O}\n- Legacy database: `{businessDb}`\n- Official database preserved: {failure.OfficialDatabasePreserved}\n- Error: {failure.ErrorType}: {failure.ErrorMessage}\n");
        Console.Error.WriteLine($"Migration failed. The official database was not replaced. Report: {reportDirectory}");
        Console.Error.WriteLine($"{failure.ErrorType}: {failure.ErrorMessage}");
        return 4;
    }
}
if (command == "upgrade") {
    bool confirm = args.Contains("--confirm", StringComparer.OrdinalIgnoreCase);
    DatabaseUpgradeReport upgrade = await DatabaseUpgradeRunner.UpgradeAsync(dataRoot, confirm);
    Console.WriteLine(JsonSerializer.Serialize(upgrade, new JsonSerializerOptions { WriteIndented = true }));
    return upgrade.Status == "Completed" ? 0 : 3;
}
if (command == "validate") {
    string database = Path.Combine(dataRoot, "data", "LocalMediaManager.db");
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = database, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared,
    }.ToString());
    await connection.OpenAsync();
    async Task<object?> Value(string sql) { await using var query = connection.CreateCommand(); query.CommandText = sql; return await query.ExecuteScalarAsync(); }
    async Task<long> Rows(string sql) { await using var query = connection.CreateCommand(); query.CommandText = sql; await using var reader = await query.ExecuteReaderAsync(); long count = 0; while (await reader.ReadAsync()) count++; return count; }
    var validation = new {
        Database = database,
        DatabaseBytes = new FileInfo(database).Length,
        Movies = await Value("SELECT COUNT(*) FROM Movies"),
        Actors = await Value("SELECT COUNT(*) FROM Actors"),
        ActorIdMin = await Value("SELECT COALESCE(MIN(Id),0) FROM Actors"),
        ActorIdMax = await Value("SELECT COALESCE(MAX(Id),0) FROM Actors"),
        ActorIdZero = await Value("SELECT COUNT(*) FROM Actors WHERE Id=0"),
        MigrationCount = await Value("SELECT COUNT(*) FROM SchemaMigrations"),
        MigrationMax = await Value("SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations"),
        Migration14Checksum = await Value("SELECT Checksum FROM SchemaMigrations WHERE Version=14"),
        Integrity = await Value("PRAGMA integrity_check"),
        ForeignKeyErrors = await Rows("PRAGMA foreign_key_check"),
        ActorColumns = await Rows("SELECT 1 FROM pragma_table_info('Actors')"),
        ActorIndexes = await Rows("SELECT 1 FROM pragma_index_list('Actors')"),
        ActorsTableSql = await Value("SELECT sql FROM sqlite_master WHERE type='table' AND name='Actors'"),
    };
    Console.WriteLine(JsonSerializer.Serialize(validation, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (command != "analyze") {
    Console.Error.WriteLine("Use: analyze [output], migrate [--confirm-switch], upgrade [--confirm], or validate.");
    return 2;
}

Directory.CreateDirectory(output);
var report = new LegacyAnalysisReport(
    ToolVersion: "0.7.8",
    GeneratedAt: DateTimeOffset.Now,
    Databases: [await AnalyzeAsync("business", businessDb), await AnalyzeAsync("configuration", configDb)]);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(Path.Combine(output, "legacy-database-analysis.json"), JsonSerializer.Serialize(report, jsonOptions));
await File.WriteAllTextAsync(Path.Combine(output, "LEGACY_DATABASE_DICTIONARY.md"), RenderMarkdown(report));
Console.WriteLine(JsonSerializer.Serialize(new {
    output,
    databases = report.Databases.Select(db => new { db.Name, db.Path, db.Sha256, tables = db.Tables.Count }),
}, jsonOptions));
return 0;

static async Task<DatabaseAnalysis> AnalyzeAsync(string name, string path)
{
    if (!File.Exists(path))
        return new DatabaseAnalysis(name, path, null, 0, [], ["Database file not found"]);

    string hash;
    await using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)).ToLowerInvariant();

    var tables = new List<TableAnalysis>();
    var warnings = new List<string>();
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared,
    }.ToString());
    await connection.OpenAsync();
    await using var list = connection.CreateCommand();
    list.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
    var tableRows = new List<(string Name, string Sql)>();
    await using (var reader = await list.ExecuteReaderAsync())
        while (await reader.ReadAsync())
            tableRows.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));

    foreach ((string tableName, string createSql) in tableRows) {
        long rowCount = await ScalarLongAsync(connection, $"SELECT COUNT(*) FROM {Q(tableName)}");
        var columns = new List<ColumnAnalysis>();
        await using (var pragma = connection.CreateCommand()) {
            pragma.CommandText = $"PRAGMA table_info({Q(tableName)})";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                string column = reader.GetString(1);
                long nullCount = rowCount == 0 ? 0 : await ScalarLongAsync(connection,
                    $"SELECT COUNT(*) FROM {Q(tableName)} WHERE {Q(column)} IS NULL");
                long emptyCount = rowCount == 0 ? 0 : await ScalarLongAsync(connection,
                    $"SELECT COUNT(*) FROM {Q(tableName)} WHERE CAST({Q(column)} AS TEXT) = ''");
                columns.Add(new ColumnAnalysis(
                    reader.GetInt32(0), column, reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.GetInt32(3) != 0, reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString(),
                    reader.GetInt32(5) != 0, nullCount, emptyCount));
            }
        }

        var foreignKeys = new List<ForeignKeyAnalysis>();
        await using (var pragma = connection.CreateCommand()) {
            pragma.CommandText = $"PRAGMA foreign_key_list({Q(tableName)})";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                foreignKeys.Add(new ForeignKeyAnalysis(reader.GetString(3), reader.GetString(2), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6)));
        }

        var indexes = new List<IndexAnalysis>();
        await using (var pragma = connection.CreateCommand()) {
            pragma.CommandText = $"PRAGMA index_list({Q(tableName)})";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                string indexName = reader.GetString(1);
                bool unique = reader.GetInt32(2) != 0;
                var indexColumns = new List<string>();
                await using var indexPragma = connection.CreateCommand();
                indexPragma.CommandText = $"PRAGMA index_info({Q(indexName)})";
                await using var indexReader = await indexPragma.ExecuteReaderAsync();
                while (await indexReader.ReadAsync()) indexColumns.Add(indexReader.GetString(2));
                indexes.Add(new IndexAnalysis(indexName, unique, indexColumns));
            }
        }
        tables.Add(new TableAnalysis(tableName, rowCount, createSql, columns, foreignKeys, indexes));
    }

    string integrity = await ScalarTextAsync(connection, "PRAGMA integrity_check") ?? "unknown";
    if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)) warnings.Add($"integrity_check: {integrity}");
    return new DatabaseAnalysis(name, path, hash, new FileInfo(path).Length, tables, warnings);
}

static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
}

static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return (await command.ExecuteScalarAsync())?.ToString();
}

static string Q(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

static string RenderMarkdown(LegacyAnalysisReport report)
{
    var text = new System.Text.StringBuilder();
    text.AppendLine("# Legacy database dictionary").AppendLine();
    text.AppendLine($"Generated: {report.GeneratedAt:O}").AppendLine();
    text.AppendLine("This report was generated from the real SQLite schema and real row/null counts. Both legacy databases were opened read-only.").AppendLine();
    foreach (DatabaseAnalysis database in report.Databases) {
        text.AppendLine($"## {database.Name} database").AppendLine();
        text.AppendLine($"- Path: `{database.Path}`");
        text.AppendLine($"- SHA-256: `{database.Sha256 ?? "unavailable"}`");
        text.AppendLine($"- Size: {database.Size} bytes");
        text.AppendLine($"- Tables: {database.Tables.Count}").AppendLine();
        foreach (TableAnalysis table in database.Tables) {
            text.AppendLine($"### `{table.Name}` ({table.RowCount} rows)").AppendLine();
            text.AppendLine("| Column | Declared type | PK | Not null | Default | Nulls | Empty strings |");
            text.AppendLine("|---|---|---:|---:|---|---:|---:|");
            foreach (ColumnAnalysis column in table.Columns)
                text.AppendLine($"| `{column.Name}` | `{column.Type}` | {(column.PrimaryKey ? "yes" : "no")} | {(column.NotNull ? "yes" : "no")} | `{column.DefaultValue ?? ""}` | {column.NullCount} | {column.EmptyCount} |");
            text.AppendLine();
            if (table.ForeignKeys.Count == 0) text.AppendLine("Foreign keys: none declared.").AppendLine();
            else foreach (ForeignKeyAnalysis key in table.ForeignKeys)
                text.AppendLine($"- FK `{key.From}` -> `{key.Table}.{key.To}`; update `{key.OnUpdate}`, delete `{key.OnDelete}`");
            if (table.Indexes.Count == 0) text.AppendLine("Indexes: none declared.").AppendLine();
            else foreach (IndexAnalysis index in table.Indexes)
                text.AppendLine($"- Index `{index.Name}` ({string.Join(", ", index.Columns)}), unique: {index.Unique}");
            text.AppendLine().AppendLine("```sql").AppendLine(table.CreateSql).AppendLine("```").AppendLine();
        }
    }
    return text.ToString();
}

internal sealed record LegacyAnalysisReport(string ToolVersion, DateTimeOffset GeneratedAt, IReadOnlyList<DatabaseAnalysis> Databases);
internal sealed record DatabaseAnalysis(string Name, string Path, string? Sha256, long Size, IReadOnlyList<TableAnalysis> Tables, IReadOnlyList<string> Warnings);
internal sealed record TableAnalysis(string Name, long RowCount, string CreateSql, IReadOnlyList<ColumnAnalysis> Columns, IReadOnlyList<ForeignKeyAnalysis> ForeignKeys, IReadOnlyList<IndexAnalysis> Indexes);
internal sealed record ColumnAnalysis(int Ordinal, string Name, string Type, bool NotNull, string? DefaultValue, bool PrimaryKey, long NullCount, long EmptyCount);
internal sealed record ForeignKeyAnalysis(string From, string Table, string To, string OnUpdate, string OnDelete);
internal sealed record IndexAnalysis(string Name, bool Unique, IReadOnlyList<string> Columns);
