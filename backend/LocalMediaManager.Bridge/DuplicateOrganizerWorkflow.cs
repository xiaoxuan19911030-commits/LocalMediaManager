using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record DuplicateDeleteGroupCommand(string GroupKey, long KeepMovieId, IReadOnlyList<long> CandidateMovieIds);
public sealed record DuplicateDeletePlanCommand(IReadOnlyList<DuplicateDeleteGroupCommand> Groups, string Mode = "media", bool DeleteDatabaseInfo = true);
public sealed record DuplicateDeleteExecuteRequest(IReadOnlyList<DuplicateDeleteGroupCommand> Groups, string Mode, bool DeleteDatabaseInfo,
    string ConfirmationToken, bool ConfirmOriginalMedia = false, int? ConfirmCount = null);
public sealed record DuplicateDeletePreview(SafeDeletePreview SafeDelete, IReadOnlyList<DuplicateMergePreview> Merges,
    bool CanExecute, string ConfirmationToken, IReadOnlyList<string> Warnings, IReadOnlyList<string> Blockers);
public sealed record DuplicateMergePreview(string GroupKey, long KeepMovieId, IReadOnlyList<long> DeleteMovieIds,
    bool FavoriteWillMerge, double? RatingToApply, bool RatingConflict, string? RatingConflictDetail,
    IReadOnlyList<string> TagsToMerge, long PlayCountToApply, string? LastPlayedAtToApply, bool NotesConflict,
    string? NotesConflictDetail, IReadOnlyList<string> Warnings);

public sealed class DuplicateOrganizerWorkflowService(string databasePath, SafeDeleteWorkflowService safeDelete)
{
    public async Task<DuplicateDeletePreview> PreviewAsync(DuplicateDeletePlanCommand command, CancellationToken token = default)
    {
        DuplicateDeleteGroupCommand[] groups = NormalizeGroups(command.Groups);
        long[] deleteIds = groups.SelectMany(group => group.CandidateMovieIds.Where(id => id != group.KeepMovieId)).Distinct().Order().ToArray();
        if (deleteIds.Length == 0) throw new ArgumentException("请选择至少 1 部待处理重复影片。");
        SafeDeletePreview safePreview = await safeDelete.PreviewAsync(new(deleteIds, command.Mode, command.DeleteDatabaseInfo), token);

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var merges = new List<DuplicateMergePreview>();
        var blockers = new List<string>();
        var conflictWarnings = new List<string>();
        foreach (DuplicateDeleteGroupCommand group in groups) {
            DuplicateMergePreview merge = await BuildMergePreviewAsync(connection, group, token);
            merges.Add(merge);
            if (merge.RatingConflict) conflictWarnings.Add($"{merge.GroupKey}: 评分冲突，系统不会静默覆盖保留项已有评分。");
            if (merge.NotesConflict) conflictWarnings.Add($"{merge.GroupKey}: 用户备注冲突，系统不会静默合并不同备注。");
        }

        var warnings = new List<string> {
            "Safe Delete 真实行为：media 模式会将原始影片、登记图片和 NFO 移入系统回收站；metadata 模式只删除数据库信息。",
            "执行前会重新生成 Safe Delete 预览并校验确认令牌，重复范围或文件状态变化时会拒绝执行。",
            "收藏、标签、播放次数和最后播放时间可自动合并到保留项；评分和备注冲突不会静默覆盖。"
        };
        warnings.AddRange(conflictWarnings);
        warnings.AddRange(safePreview.Warnings);
        string confirmationToken = Token(groups, safePreview, merges);
        return new(safePreview, merges, true, confirmationToken, warnings, blockers);
    }

    public async Task<SafeDeleteLaunchResult> ExecuteAsync(DuplicateDeleteExecuteRequest request, CancellationToken token = default)
    {
        var preview = await PreviewAsync(new(request.Groups, request.Mode, request.DeleteDatabaseInfo), token);
        VerifyToken(preview.ConfirmationToken, request.ConfirmationToken);
        if (!preview.CanExecute) throw new InvalidOperationException("重复处理预览存在阻塞项，请重新预览后再执行。");

        await ApplyMergesAsync(preview.Merges, token);
        long[] deleteIds = preview.Merges.SelectMany(merge => merge.DeleteMovieIds).Distinct().Order().ToArray();
        return await safeDelete.ExecuteAsync(new(deleteIds, request.Mode, request.DeleteDatabaseInfo),
            new(preview.SafeDelete.ConfirmationToken, request.ConfirmOriginalMedia), token);
    }

    private async Task<DuplicateMergePreview> BuildMergePreviewAsync(SqliteConnection connection, DuplicateDeleteGroupCommand group, CancellationToken token)
    {
        long[] movieIds = group.CandidateMovieIds.Append(group.KeepMovieId).Distinct().Order().ToArray();
        await EnsureMoviesExistAsync(connection, movieIds, token);
        long[] deleteIds = group.CandidateMovieIds.Where(id => id != group.KeepMovieId).Distinct().Order().ToArray();
        if (deleteIds.Length == 0) throw new ArgumentException($"{group.GroupKey}: 没有待处理候选。");

        var states = new List<UserState>();
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = $"""
                SELECT MovieId,COALESCE(IsFavorite,0),COALESCE(UserRating,0),COALESCE(HasUserRating,CASE WHEN COALESCE(UserRating,0)>0 THEN 1 ELSE 0 END),
                       COALESCE(PlayCount,0),LastPlayedAt,Notes
                  FROM UserMovieState
                 WHERE MovieId IN ({Placeholders(movieIds, "s")})
                """;
            AddIds(command, movieIds, "s");
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                states.Add(new(reader.GetInt64(0), reader.GetInt64(1)==1, reader.GetDouble(2), reader.GetInt64(3)==1,
                    reader.GetInt64(4), Text(reader, 5), Text(reader, 6)));
        }

        UserState keep = states.FirstOrDefault(state => state.MovieId == group.KeepMovieId) ?? new(group.KeepMovieId, false, 0, false, 0, null, null);
        bool favorite = states.Any(state => state.Favorite);
        var rated = states.Where(state => state.HasRating).Select(state => state.Rating).Distinct().Order().ToArray();
        bool ratingConflict = rated.Length > 1;
        double? rating = rated.Length == 1 ? rated[0] : keep.HasRating ? keep.Rating : null;
        string? ratingDetail = ratingConflict ? $"候选中存在多个评分：{string.Join(", ", rated.Select(value => value.ToString("0.##")))}；保留项当前评分为 {(keep.HasRating ? keep.Rating.ToString("0.##") : "未评分")}。" : null;
        long playCount = states.Sum(state => state.PlayCount);
        string? lastPlayed = states.Select(state => state.LastPlayedAt).Where(value => !string.IsNullOrWhiteSpace(value)).Order(StringComparer.Ordinal).LastOrDefault();
        var notes = states.Select(state => state.Notes?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        bool notesConflict = notes.Length > 1;
        string? notesDetail = notesConflict ? "多个候选存在不同用户备注，不能静默合并。" : null;

        var tags = new List<string>();
        await using (SqliteCommand tagsCommand = connection.CreateCommand()) {
            tagsCommand.CommandText = $"""
                SELECT DISTINCT t.Name
                  FROM MovieTags mt JOIN Tags t ON t.Id=mt.TagId
                 WHERE mt.MovieId IN ({Placeholders(movieIds, "t")})
                 ORDER BY t.Name COLLATE NOCASE
                """;
            AddIds(tagsCommand, movieIds, "t");
            await using SqliteDataReader reader = await tagsCommand.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) tags.Add(reader.GetString(0));
        }

        var warnings = new List<string>();
        if (favorite && !keep.Favorite) warnings.Add("任一候选已收藏，保留项将设为收藏。");
        if (tags.Count > 0) warnings.Add("自定义标签将取并集并绑定到保留项。");
        if (playCount > keep.PlayCount) warnings.Add("播放次数将合并到保留项。");
        if (!string.IsNullOrWhiteSpace(lastPlayed) && lastPlayed != keep.LastPlayedAt) warnings.Add("最后播放时间将保留最新值。");
        return new(group.GroupKey, group.KeepMovieId, deleteIds, favorite && !keep.Favorite, rating, ratingConflict, ratingDetail,
            tags, playCount, lastPlayed, notesConflict, notesDetail, warnings);
    }

    private async Task ApplyMergesAsync(IReadOnlyList<DuplicateMergePreview> merges, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        string now = DateTimeOffset.UtcNow.ToString("O");
        foreach (DuplicateMergePreview merge in merges) {
            long[] allIds = merge.DeleteMovieIds.Append(merge.KeepMovieId).Distinct().ToArray();
            await ExecuteAsync(connection, transaction, $"""
                INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt)
                SELECT $keep,TagId,$at FROM MovieTags WHERE MovieId IN ({Placeholders(allIds, "tag")})
                """, token, AddIdsToParams(allIds, "tag", ("$keep", merge.KeepMovieId), ("$at", now)));
            await ExecuteAsync(connection, transaction, """
                INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,HasUserRating,PlayCount,LastPlayedAt,LastPositionSeconds,Notes,UpdatedAt)
                VALUES($keep,$favorite,COALESCE($rating,0),$hasRating,$play,$last,0,NULL,$at)
                ON CONFLICT(MovieId) DO UPDATE SET
                    IsFavorite=CASE WHEN $favorite=1 THEN 1 ELSE IsFavorite END,
                    UserRating=CASE WHEN $hasRating=1 AND COALESCE(HasUserRating,0)=0 THEN $rating ELSE UserRating END,
                    HasUserRating=CASE WHEN $hasRating=1 THEN 1 ELSE COALESCE(HasUserRating,0) END,
                    PlayCount=MAX(PlayCount,$play),
                    LastPlayedAt=CASE WHEN $last IS NOT NULL AND (LastPlayedAt IS NULL OR LastPlayedAt<$last) THEN $last ELSE LastPlayedAt END,
                    UpdatedAt=$at
                """, token, ("$keep", merge.KeepMovieId), ("$favorite", merge.FavoriteWillMerge ? 1 : 0),
                ("$rating", merge.RatingToApply), ("$hasRating", merge.RatingToApply is null ? 0 : 1),
                ("$play", merge.PlayCountToApply), ("$last", merge.LastPlayedAtToApply), ("$at", now));
            await ExecuteAsync(connection, transaction,
                "INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,CreatedAt) VALUES('DuplicateMerge','Movie',$id,NULL,$after,$at)",
                token, ("$id", merge.KeepMovieId), ("$after", JsonSerializer.Serialize(merge)), ("$at", now));
        }
        await transaction.CommitAsync(token);
    }

    private static DuplicateDeleteGroupCommand[] NormalizeGroups(IReadOnlyList<DuplicateDeleteGroupCommand> groups)
    {
        if (groups.Count == 0) throw new ArgumentException("请选择至少 1 个重复组。");
        return groups.Select(group => {
            if (group.KeepMovieId <= 0) throw new ArgumentException($"{group.GroupKey}: 必须选择保留项。");
            long[] ids = group.CandidateMovieIds.Distinct().Where(id => id > 0).ToArray();
            if (!ids.Contains(group.KeepMovieId)) throw new ArgumentException($"{group.GroupKey}: 保留项必须属于当前重复组。");
            if (ids.Length < 2) throw new ArgumentException($"{group.GroupKey}: 重复组至少需要 2 部影片。");
            return group with { CandidateMovieIds = ids };
        }).ToArray();
    }

    private static async Task EnsureMoviesExistAsync(SqliteConnection connection, long[] movieIds, CancellationToken token)
    {
        long count = await ScalarLongAsync(connection, $"SELECT COUNT(*) FROM Movies WHERE Id IN ({Placeholders(movieIds, "m")})", token, AddIdsToParams(movieIds, "m"));
        if (count != movieIds.Length) throw new KeyNotFoundException("重复组包含已不存在的影片，请重新扫描重复结果。");
    }

    private static string Token(IReadOnlyList<DuplicateDeleteGroupCommand> groups, SafeDeletePreview safeDelete, IReadOnlyList<DuplicateMergePreview> merges)
    {
        string state = JsonSerializer.Serialize(new {
            groups = groups.Select(group => new { group.GroupKey, group.KeepMovieId, CandidateMovieIds = group.CandidateMovieIds.Order() }),
            safeDelete.ConfirmationToken,
            merges
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lmm-duplicate-organizer|" + state))).ToLowerInvariant();
    }

    private static void VerifyToken(string expected, string supplied)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied ?? "")))
            throw new UnauthorizedAccessException("重复处理预览已变化，请重新预览。");
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync(token);
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", token);
        return connection;
    }

    private static string Placeholders(IReadOnlyList<long> ids, string prefix) => string.Join(",", ids.Select((_, index) => $"${prefix}{index}"));
    private static void AddIds(SqliteCommand command, IReadOnlyList<long> ids, string prefix)
    {
        for (int index = 0; index < ids.Count; index++) command.Parameters.AddWithValue($"${prefix}{index}", ids[index]);
    }
    private static (string, object?)[] AddIdsToParams(IReadOnlyList<long> ids, string prefix, params (string, object?)[] extra) =>
        extra.Concat(ids.Select((id, index) => ($"${prefix}{index}", (object?)id))).ToArray();
    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private sealed record UserState(long MovieId, bool Favorite, double Rating, bool HasRating, long PlayCount, string? LastPlayedAt, string? Notes);
}
