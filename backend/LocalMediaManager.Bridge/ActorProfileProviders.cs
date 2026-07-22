using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public interface IActorProfileProvider
{
    string Name { get; }
    Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken);
}

public sealed record ActorProfilePreview(long ActorId, IReadOnlyList<ActorProfileCandidate> Candidates, IReadOnlyList<string> Warnings);
public sealed record ActorProfileCompleteCommand(string? Search = null, int Limit = 24, bool AllActors = false);
public sealed record ActorProfileCompleteResult(int Checked, int UpdatedProfiles, int DownloadedAvatars, int Skipped, IReadOnlyList<string> Warnings);
public sealed record ActorProfileCompleteLaunchResult(long TaskId, string Status, int TotalItems, string Message);

public sealed class ActorProfileProviderService(string databasePath, MetadataProviderSettingsService settings, MinnanoActorProfileProvider minnano,
    WikipediaJpActorProfileProvider wikipedia, ActorProfileService profiles, MovieImageImporter? imageImporter = null, JavBusProvider? javBus = null)
{
    public async Task<ActorProfilePreview> PreviewAsync(long actorId, string? source, CancellationToken cancellationToken)
    {
        if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));
        (string Name, IReadOnlyList<string> Aliases) actor = await ReadActorAsync(actorId, cancellationToken);
        var candidates = new List<ActorProfileCandidate>();
        var warnings = new List<string>();
        foreach ((IActorProfileProvider provider, WebMetadataSettingsDto providerSettings) in await ProvidersAsync(source)) {
            if (!providerSettings.Enabled) continue;
            try {
                candidates.AddRange((await provider.SearchAsync(actor.Name, actor.Aliases, providerSettings, cancellationToken))
                    .Where(candidate => candidate.Confidence >= 0.85));
            } catch (Exception error) when (error is not OperationCanceledException) {
                warnings.Add($"{provider.Name}: {SafeError(error)}");
            }
        }
        return new(actorId, candidates.OrderByDescending(value => value.Confidence).ToArray(), warnings);
    }

    public Task<ActorProfileMergeResult> ApplyAsync(long actorId, ActorProfileCandidate candidate, CancellationToken cancellationToken) =>
        profiles.MergeAsync(actorId, candidate, cancellationToken);

    public async Task<ActorProfileCompleteResult> CompleteMissingAsync(ActorProfileCompleteCommand command, CancellationToken cancellationToken,
        Func<int, int, ActorProfileTarget, Task>? onActorStart = null,
        Func<ActorProfileTarget, string, Task>? onActorLog = null)
    {
        IReadOnlyList<ActorProfileTarget> targets = await ReadTargetsAsync(command, cancellationToken);
        int updatedProfiles = 0;
        int downloadedAvatars = 0;
        int skipped = 0;
        var warnings = new List<string>();

        int index = 0;
        foreach (ActorProfileTarget actor in targets) {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            if (onActorStart is not null) await onActorStart(index, targets.Count, actor);
            try {
                ActorProfilePreview preview = await PreviewAsync(actor.Id, null, cancellationToken);
                ActorProfileCandidate? candidate = preview.Candidates.FirstOrDefault(value => value.Confidence >= 0.95)
                    ?? (preview.Candidates.Count == 1 ? preview.Candidates[0] : null);
                if (candidate is null) {
                    string message = preview.Warnings.FirstOrDefault() ?? $"{actor.Name}: 未找到可自动确认的资料候选。";
                    warnings.Add(message);
                    if (onActorLog is not null) await onActorLog(actor, message);
                    int javBusAvatars = await TryDownloadJavBusActorAvatarAsync(actor, warnings, onActorLog, cancellationToken);
                    downloadedAvatars += javBusAvatars;
                    if (javBusAvatars == 0) skipped++;
                    continue;
                }

                ActorProfileMergeResult merge = await ApplyAsync(actor.Id, candidate, cancellationToken);
                if (merge.UpdatedFields.Count > 0) updatedProfiles++;
                if (onActorLog is not null)
                    await onActorLog(actor, merge.UpdatedFields.Count > 0
                        ? $"已从 {candidate.Source} 更新资料：{string.Join(", ", merge.UpdatedFields)}。"
                        : $"已匹配 {candidate.Source}，资料无需更新。");

                ActorProfileCandidate? avatarCandidate = !string.IsNullOrWhiteSpace(candidate.Profile.AvatarUrl)
                    ? candidate
                    : preview.Candidates.FirstOrDefault(value => value.Confidence >= 0.9
                        && !string.IsNullOrWhiteSpace(value.Profile.AvatarUrl));
                if (avatarCandidate is not null && !string.IsNullOrWhiteSpace(avatarCandidate.Profile.AvatarUrl) && imageImporter is not null) {
                    MovieImageImportResult avatar = await imageImporter.ImportActorImagesAsync(
                        [new(actor.Name, avatarCandidate.Profile.AvatarUrl)], avatarCandidate.Source, 20, overwrite: false, cancellationToken);
                    downloadedAvatars += avatar.ActorImagesDownloaded;
                    warnings.AddRange(avatar.Warnings);
                    if (onActorLog is not null) {
                        if (avatar.ActorImagesDownloaded > 0) await onActorLog(actor, $"演员头像已从 {avatarCandidate.Source} 补齐。");
                        foreach (string warning in avatar.Warnings) await onActorLog(actor, warning);
                    }
                }
                else {
                    downloadedAvatars += await TryDownloadJavBusActorAvatarAsync(actor, warnings, onActorLog, cancellationToken);
                }
            } catch (Exception error) when (error is not OperationCanceledException) {
                skipped++;
                string message = $"{actor.Name}: {SafeError(error)}";
                warnings.Add(message);
                if (onActorLog is not null) await onActorLog(actor, message);
            }
        }

        return new(targets.Count, updatedProfiles, downloadedAvatars, skipped, warnings.Take(8).ToArray());
    }

    private async Task<IReadOnlyList<(IActorProfileProvider, WebMetadataSettingsDto)>> ProvidersAsync(string? source)
    {
        var values = new List<(IActorProfileProvider, WebMetadataSettingsDto)> {
            (minnano, await settings.ReadMinnanoAsync()),
            (wikipedia, await settings.ReadWikipediaJpAsync()),
        };
        return string.IsNullOrWhiteSpace(source) ? values : values.Where(item => item.Item1.Name.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async Task<int> TryDownloadJavBusActorAvatarAsync(ActorProfileTarget actor, List<string> warnings,
        Func<ActorProfileTarget, string, Task>? onActorLog, CancellationToken cancellationToken)
    {
        if (javBus is null || imageImporter is null) return 0;
        JavBusSettingsDto javBusSettings = await settings.ReadJavBusAsync();
        if (!javBusSettings.Enabled || !javBusSettings.DownloadImages) return 0;

        (string Name, IReadOnlyList<string> Aliases) actorInfo = await ReadActorAsync(actor.Id, cancellationToken);
        IReadOnlyList<string> codes = await ReadRelatedMovieCodesAsync(actor.Id, cancellationToken);
        if (codes.Count == 0) {
            if (onActorLog is not null) await onActorLog(actor, "未找到可用于 JavBus 头像兜底的关联影片番号。");
            return 0;
        }

        var context = new MetadataProviderContext(await settings.ReadMetaTubeAsync(), javBusSettings, "JavBus",
            await settings.ReadDmmAsync(), await settings.ReadJavDbAsync(), await settings.ReadNetworkAsync()) {
            MdcNg = await settings.ReadMdcNgAsync(),
        };
        HashSet<string> names = new[] { actorInfo.Name }.Concat(actorInfo.Aliases)
            .Select(NormalizeActorName)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string code in codes.Take(5)) {
            try {
                MetadataSearchResult? result = (await javBus.SearchAsync(code, context, cancellationToken)).FirstOrDefault();
                if (result is null) continue;
                ProviderMetadata? metadata = await javBus.GetMetadataAsync(result, context, cancellationToken);
                ActorImageMetadata? image = metadata?.ActorImages?
                    .FirstOrDefault(value => names.Contains(NormalizeActorName(value.Name)));
                if (image is null) continue;

                MovieImageImportResult imported = await imageImporter.ImportActorImagesAsync(
                    [new(actorInfo.Name, image.ImageUrl)], "JavBus", javBusSettings.TimeoutSeconds, overwrite: false, cancellationToken);
                warnings.AddRange(imported.Warnings);
                if (onActorLog is not null) {
                    if (imported.ActorImagesDownloaded > 0)
                        await onActorLog(actor, $"演员头像已从 JavBus 关联影片 {code} 补齐。");
                    foreach (string warning in imported.Warnings) await onActorLog(actor, warning);
                }
                return imported.ActorImagesDownloaded;
            } catch (Exception error) when (error is not OperationCanceledException) {
                string warning = $"JavBus 头像兜底 {code}: {SafeError(error)}";
                warnings.Add(warning);
                if (onActorLog is not null) await onActorLog(actor, warning);
            }
        }

        if (onActorLog is not null) await onActorLog(actor, "JavBus 关联影片中未找到该演员头像。");
        return 0;
    }

    private async Task<(string, IReadOnlyList<string>)> ReadActorAsync(long actorId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name,Alias FROM Actors WHERE Id=$id AND Id>0";
        command.Parameters.AddWithValue("$id", actorId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException($"Actor {actorId} was not found.");
        string name = reader.GetString(0);
        string[] aliases = reader.IsDBNull(1) ? [] : reader.GetString(1).Split([',', '，', '/', '／'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (name, aliases);
    }

    internal Task<IReadOnlyList<ActorProfileTarget>> ReadTargetsAsync(ActorProfileCompleteCommand command, CancellationToken cancellationToken)
    {
        string search = command.AllActors ? "" : command.Search?.Trim() ?? "";
        int limit = command.AllActors ? int.MaxValue : Math.Clamp(command.Limit, 1, 50);
        return ReadMissingActorsAsync(search, limit, cancellationToken);
    }

    internal async Task<int> CountTargetsAsync(ActorProfileCompleteCommand command, CancellationToken cancellationToken)
    {
        string search = command.AllActors ? "" : command.Search?.Trim() ?? "";
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM Actors a WHERE {MissingActorPredicate}";
        count.Parameters.AddWithValue("$search", search);
        count.Parameters.AddWithValue("$like", $"%{EscapeLike(search)}%");
        int total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        return command.AllActors ? total : Math.Min(total, Math.Clamp(command.Limit, 1, 50));
    }

    private const string MissingActorPredicate = """
        a.Id>0
        AND ($search='' OR a.Name LIKE $like ESCAPE '\' OR COALESCE(a.Alias,'') LIKE $like ESCAPE '\')
        AND (
             a.BirthDate IS NULL OR a.HeightCm IS NULL OR a.Cup IS NULL OR trim(COALESCE(a.Description,''))=''
             OR NOT EXISTS(SELECT 1 FROM Images i WHERE i.ActorId=a.Id AND i.ImageType='ActorAvatar' AND trim(COALESCE(i.FilePath,''))<>'' AND COALESCE(i.SourceProvider,'')<>'LegacyFile')
        )
        """;

    private async Task<IReadOnlyList<ActorProfileTarget>> ReadMissingActorsAsync(string search, int limit, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT a.Id,a.Name
              FROM Actors a
             WHERE {MissingActorPredicate}
             ORDER BY (
                    SELECT COUNT(*) FROM MovieActors ma WHERE ma.ActorId=a.Id
               ) DESC,a.Name
             LIMIT $limit
            """;
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(search)}%");
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ActorProfileTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    private async Task<IReadOnlyList<string>> ReadRelatedMovieCodesAsync(long actorId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT trim(COALESCE(m.Code,'')) AS Code
              FROM MovieActors ma JOIN Movies m ON m.Id=ma.MovieId
             WHERE ma.ActorId=$actor AND trim(COALESCE(m.Code,''))<>''
             GROUP BY Code
             ORDER BY MAX(COALESCE(m.UpdatedAt,m.CreatedAt,'')) DESC LIMIT 8
            """;
        command.Parameters.AddWithValue("$actor", actorId);
        var result = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private static string SafeError(Exception error) => error switch {
        TaskCanceledException => "请求超时，请检查代理后重试。",
        InvalidDataException => "页面结构已变化，当前结果未写入。",
        InvalidOperationException => error.Message,
        _ => "网络请求失败，完整错误已写入 Debug 日志。",
    };
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public sealed record ActorProfileTarget(long Id, string Name);
    private static string NormalizeActorName(string value) => Regex.Replace(value ?? "", @"[\s・･·（）()]", "").ToUpperInvariant();
}

public sealed class ActorProfileCompleteTaskService(
    string databasePath,
    ActorProfileProviderService profiles,
    TaskLogService logs) : BackgroundService
{
    private readonly Dictionary<long, CancellationTokenSource> cancellations = new();

    public async Task<ActorProfileCompleteLaunchResult> EnqueueAsync(ActorProfileCompleteCommand command, CancellationToken cancellationToken = default)
    {
        var normalized = command.AllActors
            ? new ActorProfileCompleteCommand("", 24, true)
            : new ActorProfileCompleteCommand(command.Search?.Trim(), Math.Clamp(command.Limit, 1, 50));
        int total = await profiles.CountTargetsAsync(normalized, cancellationToken);
        string payload = JsonSerializer.Serialize(normalized);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using SqliteCommand existing = connection.CreateCommand();
        existing.CommandText = "SELECT Id,Status,TotalItems FROM Tasks WHERE TaskType='ActorProfileComplete' AND Status NOT IN ('Completed','CompletedWithErrors','Failed','Cancelled') ORDER BY Id DESC LIMIT 1";
        await using (SqliteDataReader reader = await existing.ExecuteReaderAsync(cancellationToken)) {
            if (await reader.ReadAsync(cancellationToken))
                return new(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), "演员信息补全任务已在任务中心。");
        }
        await using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES('ActorProfileComplete','Pending','Pending',0,$total,0,$payload,$at,$at);
            SELECT last_insert_rowid();
            """;
        create.Parameters.AddWithValue("$total", total);
        create.Parameters.AddWithValue("$payload", payload);
        create.Parameters.AddWithValue("$at", Now());
        long id = Convert.ToInt64(await create.ExecuteScalarAsync(cancellationToken));
        await logs.WriteAsync(id, "Info", normalized.AllActors
            ? $"演员信息补全任务已创建，将检查全部 {total} 个资料不完整的演员。"
            : $"演员信息补全任务已创建，本批将检查 {total} 个演员。", cancellationToken);
        return new(id, "Pending", total, "演员信息补全任务已进入任务中心。");
    }

    public Task<TaskMutationResult> PauseAsync(long id) => UpdateCommandAsync(id, "Paused", "Paused", "演员信息补全任务已暂停。", false);
    public Task<TaskMutationResult> ResumeAsync(long id) => UpdateCommandAsync(id, "Pending", "Pending", "演员信息补全任务已继续。", false);
    public async Task<TaskMutationResult> CancelAsync(long id)
    {
        if (cancellations.TryGetValue(id, out CancellationTokenSource? source)) source.Cancel();
        return await UpdateCommandAsync(id, "Cancelled", "Cancelled", "演员信息补全任务已取消。", true);
    }
    public async Task<ActorProfileCompleteLaunchResult> RetryAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='ActorProfileComplete'", ("$id", id));
        if (status is null) throw new KeyNotFoundException("演员信息补全任务不存在。");
        if (status is not ("Failed" or "Cancelled"))
            throw new InvalidOperationException("只有失败或已取消的演员信息补全任务可以重试。");
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Pending',Stage='Pending',Progress=0,CompletedItems=0,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", id));
        long total = await ScalarLongAsync(connection, "SELECT TotalItems FROM Tasks WHERE Id=$id", ("$id", id));
        await logs.WriteAsync(id, "Info", "演员信息补全任务已重新进入队列。");
        return new(id, "Pending", (int)total, "演员信息补全任务已重试。");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            long? id = await ClaimAsync(stoppingToken);
            if (id is null) {
                await Task.Delay(1000, stoppingToken);
                continue;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cancellations[id.Value] = linked;
            try { await RunAsync(id.Value, linked.Token); }
            catch (OperationCanceledException) { await MarkCancelledAsync(id.Value); }
            catch (Exception error) { await FailAsync(id.Value, error); }
            finally { cancellations.Remove(id.Value); }
        }
    }

    private async Task RunAsync(long id, CancellationToken token)
    {
        ActorProfileCompleteCommand command = await ReadCommandAsync(id, token);
        int completed = 0;
        await logs.WriteAsync(id, "Info", "开始补全演员资料与头像。", token);
        ActorProfileCompleteResult result = await profiles.CompleteMissingAsync(command, token,
            async (index, total, actor) => {
                completed = index - 1;
                await UpdateProgressAsync(id, "Running", total, completed, total == 0 ? 100 : Math.Round(completed * 100d / total, 2), actor.Name, token);
                await logs.WriteAsync(id, "Info", $"正在检查：{actor.Name}", token);
            },
            async (actor, message) => await logs.WriteAsync(id, message.Contains("失败") || message.Contains("验证") || message.Contains("未找到") ? "Warning" : "Info", $"{actor.Name}: {message}", token));
        completed = result.Checked;
        string summary = $"已检查 {result.Checked} 个演员，更新资料 {result.UpdatedProfiles} 个，补头像 {result.DownloadedAvatars} 张，跳过 {result.Skipped} 个。";
        string status = result.Skipped > 0 && (result.UpdatedProfiles > 0 || result.DownloadedAvatars > 0) ? "CompletedWithErrors" : "Completed";
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, """
            UPDATE Tasks SET Status=$status,Stage='Completed',Progress=100,CompletedItems=$completed,ResultJson=$result,
                ResultSummary=$summary,ErrorMessage=NULL,CompletedAt=$at,UpdatedAt=$at
             WHERE Id=$id
            """, ("$status", status), ("$completed", completed), ("$result", JsonSerializer.Serialize(result)), ("$summary", summary), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, "Info", summary, token);
        foreach (string warning in result.Warnings) await logs.WriteAsync(id, "Warning", warning, token);
    }

    private async Task<long?> ClaimAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        long id = await ScalarLongAsync(connection, "SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType='ActorProfileComplete' AND Status IN ('Pending','Retrying')", ("$none", 0));
        if (id == 0) return null;
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Preparing',Stage='Preparing',StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id AND Status IN ('Pending','Retrying')", ("$at", Now()), ("$id", id));
        return id;
    }

    private async Task<ActorProfileCompleteCommand> ReadCommandAsync(long id, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? payload = await ScalarTextAsync(connection, "SELECT PayloadJson FROM Tasks WHERE Id=$id", ("$id", id));
        try { return JsonSerializer.Deserialize<ActorProfileCompleteCommand>(payload ?? "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private async Task UpdateProgressAsync(long id, string stage, int total, int completed, double progress, string? provider, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Running',Stage=$stage,Progress=$progress,TotalItems=$total,CompletedItems=$completed,Provider=$provider,UpdatedAt=$at WHERE Id=$id",
            ("$stage", stage), ("$progress", progress), ("$total", total), ("$completed", completed), ("$provider", provider), ("$at", Now()), ("$id", id));
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage='上次运行异常中断，已恢复到队列。',UpdatedAt=$at WHERE TaskType='ActorProfileComplete' AND Status IN ('Preparing','Running')", ("$at", Now()));
    }
    private async Task MarkCancelledAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, "Warning", "演员信息补全任务已取消。");
    }
    private async Task FailAsync(long id, Exception error)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id", ("$error", error.Message), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, "Error", error.Message);
    }
    private async Task<TaskMutationResult> UpdateCommandAsync(long id, string status, string stage, string message, bool cancel)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='ActorProfileComplete'", ("$id", id)) == 0)
            throw new KeyNotFoundException("演员信息补全任务不存在。");
        await ExecuteAsync(connection, "UPDATE Tasks SET Status=$status,Stage=$stage,CancellationRequested=$cancel,CompletedAt=CASE WHEN $status='Cancelled' THEN $at ELSE CompletedAt END,UpdatedAt=$at WHERE Id=$id",
            ("$status", status), ("$stage", stage), ("$cancel", cancel ? 1 : 0), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, status == "Cancelled" ? "Warning" : "Info", message);
        return new(id, status, message);
    }
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }
    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync())?.ToString();
    }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
}

public sealed class MinnanoActorProfileProvider(IHttpClientFactory clients) : IActorProfileProvider
{
    public const string DefaultBaseUrl = "https://www.minnano-av.com/";
    public string Name => "Minnano";
    public async Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken)
    {
        using HttpClient client = ActorHttp(clients, Name, settings);
        Uri search = new(new Uri(settings.BaseUrl), $"search_result.php?search_word={Uri.EscapeDataString(name)}");
        string html = await GetTextAsync(client, search, cancellationToken);
        var names = new[] { name }.Concat(aliases).Select(NormalizeName).Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ActorProfileCandidate>();
        foreach ((string url, string candidateName) in MinnanoParser.Results(html, search)) {
            if (!names.Contains(NormalizeName(candidateName))) continue;
            string detail = await GetTextAsync(client, new Uri(url), cancellationToken);
            ActorProfileData profile = MinnanoParser.Profile(detail);
            candidates.Add(new(Name, candidateName, url, NormalizeName(candidateName) == NormalizeName(name) ? 0.98 : 0.9, profile));
            if (candidates.Count >= 3) break;
        }
        return candidates;
    }
    public Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken) =>
        TestAsync(clients, Name, settings, cancellationToken);

    internal static HttpClient ActorHttp(IHttpClientFactory clients, string name, WebMetadataSettingsDto settings)
    {
        HttpClient client = clients.CreateClient(name);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        if (!string.IsNullOrWhiteSpace(settings.Cookie)) client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Cookie);
        return client;
    }
    internal static async Task<string> GetTextAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        try {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ProviderParsing.StatusMessage(uri.Host, response.StatusCode));
            string text = await response.Content.ReadAsStringAsync(token);
            if (ProviderParsing.LooksBlocked(text)) throw new InvalidOperationException("来源返回了验证或登录页面。");
            return text;
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(uri.Host, uri, 30,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, null, uri.GetLeftPart(UriPartial.Authority) + "/"), CancellationToken.None);
            throw new ProviderNetworkException(uri.Host, uri, $"{error.Message}. {trace}", error);
        }
    }
    internal static async Task<ProviderConnectionResult> TestAsync(IHttpClientFactory clients, string name, WebMetadataSettingsDto settings, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        try {
            Uri uri = new(settings.BaseUrl);
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(name, uri, settings.TimeoutSeconds,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), token);
            bool success = trace.Contains("HTTP 200", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Cloudflare: detected", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Blocked Page:", StringComparison.OrdinalIgnoreCase);
            return new(success, name, trace, watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            return new(false, name, error is TaskCanceledException ? $"{name} HTTP Timeout" : $"{name} 网络请求失败：{error.Message}", watch.ElapsedMilliseconds);
        }
    }
    private static string NormalizeName(string value) => Regex.Replace(value ?? "", @"[\s・･·]", "").ToUpperInvariant();
}

public sealed class WikipediaJpActorProfileProvider(IHttpClientFactory clients) : IActorProfileProvider
{
    public const string DefaultBaseUrl = "https://ja.wikipedia.org/";
    public string Name => "Wikipedia JP";
    public async Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken)
    {
        using HttpClient client = MinnanoActorProfileProvider.ActorHttp(clients, Name, settings);
        Uri uri = new(new Uri(settings.BaseUrl), $"w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(name)}&gsrlimit=5&prop=extracts|info&exintro=1&explaintext=1&inprop=url&format=json&formatversion=2");
        string json = await MinnanoActorProfileProvider.GetTextAsync(client, uri, cancellationToken);
        return WikipediaJpParser.Parse(json, name, aliases);
    }
    public Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken) =>
        MinnanoActorProfileProvider.TestAsync(clients, Name, settings, cancellationToken);
}

internal static class MinnanoParser
{
    public static IReadOnlyList<(string Url, string Name)> Results(string html, Uri baseUri) =>
        Regex.Matches(html, @"<a[^>]*href=[""']([^""']*(?:actress|av_actress)[^""']*)[""'][^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)
            .Select(match => (ProviderParsing.Absolute(match.Groups[1].Value, baseUri)!, ProviderParsing.Text(match.Groups[2].Value)))
            .Where(value => value.Item1 is not null && value.Item2.Length > 0).Distinct().ToArray();

    public static ActorProfileData Profile(string html)
    {
        string? birthday = ProviderParsing.Date(ProviderParsing.Labeled(html, "生年月日", "誕生日"));
        int? height = Number(ProviderParsing.Labeled(html, "身長"));
        string? cup = Regex.Match(ProviderParsing.Labeled(html, "カップ", "罩杯") ?? "", @"\b([A-N])\b", RegexOptions.IgnoreCase) is { Success: true } match ? match.Groups[1].Value.ToUpperInvariant() : null;
        string[] aliases = ProviderParsing.Links(html, "alias", "actress").Take(12).ToArray();
        string? avatar = ProviderParsing.Meta(html, "og:image");
        return new(birthday, height, cup, null, ProviderParsing.Labeled(html, "デビュー", "活動期間"), null, aliases, avatar);
    }
    private static int? Number(string? value) => Regex.Match(value ?? "", @"\d{3}") is { Success: true } match && int.TryParse(match.Value, out int number) && number is >= 100 and <= 250 ? number : null;
}

internal static class WikipediaJpParser
{
    public static IReadOnlyList<ActorProfileCandidate> Parse(string json, string name, IReadOnlyList<string> aliases)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out JsonElement query) || !query.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) return [];
        var names = new[] { name }.Concat(aliases).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ActorProfileCandidate>();
        foreach (JsonElement page in pages.EnumerateArray()) {
            string title = page.TryGetProperty("title", out JsonElement titleValue) ? titleValue.GetString() ?? "" : "";
            string extract = page.TryGetProperty("extract", out JsonElement extractValue) ? extractValue.GetString() ?? "" : "";
            if (!names.Contains(Normalize(title)) || IsDisambiguation(extract)) continue;
            string url = page.TryGetProperty("fullurl", out JsonElement urlValue) ? urlValue.GetString() ?? "" : "";
            string? birth = Regex.Match(extract, @"(?<y>19\d{2}|20\d{2})年(?<m>\d{1,2})月(?<d>\d{1,2})日") is { Success: true } date
                ? $"{date.Groups["y"].Value}-{int.Parse(date.Groups["m"].Value):00}-{int.Parse(date.Groups["d"].Value):00}" : null;
            int? height = Regex.Match(extract, @"身長\s*(\d{3})\s*cm", RegexOptions.IgnoreCase) is { Success: true } heightMatch ? int.Parse(heightMatch.Groups[1].Value) : null;
            string? birthplace = Regex.Match(extract, @"(?:出身地|出生地)[は:：\s]*([^。、\n]{2,40})") is { Success: true } place ? place.Groups[1].Value.Trim() : null;
            string description = extract.Length <= 500 ? extract : extract[..500];
            results.Add(new("Wikipedia JP", title, url, 0.96, new(birth, height, null, birthplace, null, description, [])));
        }
        return results;
    }
    private static string Normalize(string value) => Regex.Replace(value ?? "", @"[\s・･·（）()]", "").ToUpperInvariant();
    private static bool IsDisambiguation(string value) => value.Contains("曖昧さ回避", StringComparison.Ordinal) || value.Contains("同名", StringComparison.Ordinal) && value.Length < 150;
}
