using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record UserStateCommand(bool? Favorite, double? Rating, bool ClearRating = false);
public sealed record BatchFavoriteCommand(IReadOnlyList<long> MovieIds, bool Favorite);
public sealed record TagCommand(string Name, string? Description, string? Color);
public sealed record MovieTagsCommand(IReadOnlyList<long> AddTagIds, IReadOnlyList<long> RemoveTagIds);
public sealed record BatchTagsCommand(IReadOnlyList<long> MovieIds, IReadOnlyList<long> AddTagIds, IReadOnlyList<long> RemoveTagIds);
public sealed record ActorCommand(string Name, string? Alias, int? Gender, string? BirthDate, string? Description);
public sealed record MovieActorsCommand(IReadOnlyList<long> ActorIds);
public sealed record ConfirmCommand(string ConfirmationToken);
public sealed record MutationResult(bool Changed, long AuditId, string Message);
public sealed record ImpactPreview(string Operation, long EntityId, string Name, long AffectedMovies, string ConfirmationToken, IReadOnlyList<string> Warnings);
public sealed record ActorRepairPreview(long CandidateActors, long AffectedRelations, string ConfirmationToken, IReadOnlyList<string> Warnings);
public sealed record MovieDeletePreview(long MovieId, string Code, string FileName, bool RatingWillBeRemembered, string ConfirmationToken, IReadOnlyList<string> Warnings);

public sealed class ProductWriter(string databasePath)
{
    private readonly ConcurrentDictionary<string, PreviewGrant> grants = new(StringComparer.Ordinal);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    public async Task<MutationResult> SetUserStateAsync(long movieId, UserStateCommand input)
    {
        if (input.Rating is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(input.Rating), "评分必须在 0 到 5 之间。");
        await using var connection = await OpenAsync();
        await EnsureMovieAsync(connection, movieId);
        await using var transaction = await connection.BeginTransactionAsync();
        var before = await ReadStateAsync(connection, transaction, movieId);
        bool favorite = input.Favorite ?? before.Favorite;
        double rating = input.ClearRating ? 0 : input.Rating ?? before.Rating;
        bool hasRating = input.ClearRating ? false : input.Rating.HasValue || before.HasRating;
        await ExecuteAsync(connection, transaction, """
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating)
            VALUES($id,$favorite,$rating,0,0,$at,$hasRating)
            ON CONFLICT(MovieId) DO UPDATE SET
                IsFavorite=excluded.IsFavorite, UserRating=excluded.UserRating,
                HasUserRating=excluded.HasUserRating, UpdatedAt=excluded.UpdatedAt
            """, ("$id", movieId), ("$favorite", favorite ? 1 : 0), ("$rating", rating),
            ("$hasRating", hasRating ? 1 : 0), ("$at", Now()));
        if (input.Favorite.HasValue) await SyncLegacyFavoriteTagAsync(connection, transaction, movieId, favorite);
        long audit = await AuditAsync(connection, transaction, "UserState", "Movie", movieId, before,
            new { Favorite = favorite, Rating = rating, HasRating = hasRating });
        await transaction.CommitAsync();
        return new(true, audit, "用户状态已保存。");
    }

    public async Task<MovieDeletePreview> PreviewDeleteMovieAsync(long movieId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(f.FileName,''),COALESCE(s.HasUserRating,0)
            FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
            LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", movieId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("影片不存在。");
        string code = reader.GetString(0); string fileName = reader.GetString(1); bool remember = reader.GetInt64(2) == 1 && !string.IsNullOrWhiteSpace(fileName);
        string token = Grant("MovieDelete", movieId);
        return new(movieId, code, fileName, remember, token, [
            "只从 Local Media Manager 数据库移除记录，不删除媒体文件。",
            remember ? "当前评分会按文件名写入删除评分记忆。" : "当前没有可记忆的评分。",
            "执行前会创建完整数据库备份。"
        ]);
    }

    public async Task<MutationResult> DeleteMovieAsync(long movieId, ConfirmCommand input)
    {
        Consume(input.ConfirmationToken, "MovieDelete", movieId);
        string backupPath = await BackupDatabaseAsync("movie-delete");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(f.FileName,''),COALESCE(s.UserRating,0),COALESCE(s.HasUserRating,0)
            FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
            LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id LIMIT 1
            """; command.Parameters.AddWithValue("$id", movieId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("影片不存在。");
        string code = reader.GetString(0); string fileName = reader.GetString(1); double rating = reader.GetDouble(2); bool hasRating = reader.GetInt64(3) == 1;
        await reader.DisposeAsync();
        if (hasRating && !string.IsNullOrWhiteSpace(fileName))
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DeletedRatingMemory(FileName,NormalizedFileName,Rating,RememberedAt,SourceMovieId)
                VALUES($file,$normalized,$rating,$at,$movie)
                ON CONFLICT(NormalizedFileName) DO UPDATE SET Rating=excluded.Rating,RememberedAt=excluded.RememberedAt,SourceMovieId=excluded.SourceMovieId,RestoredAt=NULL
                """, ("$file", fileName), ("$normalized", NormalizeFile(fileName)), ("$rating", rating), ("$at", Now()), ("$movie", movieId));
        await ExecuteAsync(connection, transaction, "DELETE FROM Movies WHERE Id=$id", ("$id", movieId));
        long audit = await AuditAsync(connection, transaction, "MovieDelete", "Movie", movieId,
            new { Code = code, FileName = fileName, Rating = hasRating ? rating : (double?)null }, new { BackupPath = backupPath });
        await transaction.CommitAsync();
        return new(true, audit, hasRating ? "影片记录已移除，评分记忆已保存。" : "影片记录已移除。媒体文件未删除。");
    }

    public async Task<MutationResult> SetFavoritesAsync(BatchFavoriteCommand input)
    {
        long[] ids = input.MovieIds.Distinct().Where(id => id > 0).ToArray();
        if (ids.Length is 0 or > 500) throw new ArgumentException("请选择 1 到 500 部影片。");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (long id in ids) {
            await EnsureMovieAsync(connection, id, transaction);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating)
                VALUES($id,$favorite,0,0,0,$at,0)
                ON CONFLICT(MovieId) DO UPDATE SET IsFavorite=excluded.IsFavorite,UpdatedAt=excluded.UpdatedAt
                """, ("$id", id), ("$favorite", input.Favorite ? 1 : 0), ("$at", Now()));
            await SyncLegacyFavoriteTagAsync(connection, transaction, id, input.Favorite);
        }
        long audit = await AuditAsync(connection, transaction, "BatchFavorite", "Movie", null,
            null, new { MovieIds = ids, input.Favorite });
        await transaction.CommitAsync();
        return new(true, audit, $"已更新 {ids.Length} 部影片的收藏状态。");
    }

    public async Task<(long Id, MutationResult Result)> CreateTagAsync(TagCommand input)
    {
        string name = ValidateName(input.Name, "标签");
        string normalized = Normalize(name);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "INSERT INTO Tags(Name,NormalizedName,Description,Color,Source,CreatedAt,UpdatedAt) VALUES($name,$normalized,$description,$color,'User',$at,$at); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$normalized", normalized);
        command.Parameters.AddWithValue("$description", (object?)input.Description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$color", (object?)input.Color?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("$at", Now());
        long id;
        try { id = Convert.ToInt64(await command.ExecuteScalarAsync()); }
        catch (SqliteException error) when (error.SqliteErrorCode == 19) { throw new InvalidOperationException("同名标签已存在。"); }
        long audit = await AuditAsync(connection, transaction, "TagCreate", "Tag", id, null, new { Id = id, Name = name });
        await transaction.CommitAsync();
        return (id, new(true, audit, "标签已创建。"));
    }

    public async Task<MutationResult> UpdateTagAsync(long tagId, TagCommand input)
    {
        string name = ValidateName(input.Name, "标签");
        await using var connection = await OpenAsync();
        var before = await ReadTagAsync(connection, tagId) ?? throw new KeyNotFoundException("标签不存在。");
        if (!string.Equals(before.Source, "User", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("系统标签不能编辑。");
        await using var transaction = await connection.BeginTransactionAsync();
        try {
        await ExecuteAsync(connection, transaction, "UPDATE Tags SET Name=$name,NormalizedName=$normalized,Description=$description,Color=$color,UpdatedAt=$at WHERE Id=$id",
                ("$name", name), ("$normalized", Normalize(name)), ("$description", input.Description?.Trim() ?? before.Description), ("$color", input.Color?.Trim() ?? before.Color), ("$at", Now()), ("$id", tagId));
        } catch (SqliteException error) when (error.SqliteErrorCode == 19) { throw new InvalidOperationException("同名标签已存在。"); }
        long audit = await AuditAsync(connection, transaction, "TagUpdate", "Tag", tagId, before, new { Name = name, input.Description, input.Color });
        await transaction.CommitAsync();
        return new(true, audit, "标签已更新。");
    }

    public async Task<ImpactPreview> PreviewDeleteTagAsync(long tagId)
    {
        await using var connection = await OpenAsync();
        var tag = await ReadTagAsync(connection, tagId) ?? throw new KeyNotFoundException("标签不存在。");
        if (!string.Equals(tag.Source, "User", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("系统标签不能删除。");
        long count = await ScalarAsync(connection, "SELECT COUNT(*) FROM MovieTags WHERE TagId=$id", ("$id", tagId));
        string token = Grant("TagDelete", tagId);
        return new("DeleteTag", tagId, tag.Name, count, token,
            count > 0 ? new List<string> { $"将解除 {count} 部影片的标签关系；影片本身不会删除。" } : new List<string>());
    }

    public async Task<MutationResult> DeleteTagAsync(long tagId, ConfirmCommand input)
    {
        Consume(input.ConfirmationToken, "TagDelete", tagId);
        string backupPath = await BackupDatabaseAsync("tag-delete");
        await using var connection = await OpenAsync();
        var tag = await ReadTagAsync(connection, tagId) ?? throw new KeyNotFoundException("标签不存在。");
        if (!string.Equals(tag.Source, "User", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("系统标签不能删除。");
        await using var transaction = await connection.BeginTransactionAsync();
        var movieIds = await ReadIdsAsync(connection, transaction, "SELECT MovieId FROM MovieTags WHERE TagId=$id", ("$id", tagId));
        await ExecuteAsync(connection, transaction, "DELETE FROM Tags WHERE Id=$id", ("$id", tagId));
        long audit = await AuditAsync(connection, transaction, "TagDelete", "Tag", tagId, new { Tag = tag, MovieIds = movieIds }, new { BackupPath = backupPath });
        await transaction.CommitAsync();
        return new(true, audit, "标签已删除；审计记录可用于恢复。");
    }

    public async Task<MutationResult> RollbackAsync(long auditId)
    {
        await using var connection = await OpenAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT OperationType,BeforeJson,RevertedAt FROM OperationAudit WHERE Id=$id";
        lookup.Parameters.AddWithValue("$id", auditId);
        await using var reader = await lookup.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("审计记录不存在。");
        string operation = reader.GetString(0); string? beforeJson = reader.IsDBNull(1) ? null : reader.GetString(1); bool reverted = !reader.IsDBNull(2);
        await reader.DisposeAsync();
        if (reverted) throw new InvalidOperationException("该操作已经撤销。");
        if (operation != "TagDelete" || string.IsNullOrWhiteSpace(beforeJson)) throw new InvalidOperationException("该操作暂不支持应用内撤销；请使用审计记录中的数据库备份恢复。");
        using JsonDocument document = JsonDocument.Parse(beforeJson);
        JsonElement tag = document.RootElement.GetProperty("Tag");
        long tagId = tag.GetProperty("Id").GetInt64();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "INSERT INTO Tags(Id,Name,NormalizedName,Description,Color,Source,CreatedAt,UpdatedAt) VALUES($id,$name,$normalized,$description,$color,$source,$at,$at)",
            ("$id", tagId), ("$name", tag.GetProperty("Name").GetString()), ("$normalized", Normalize(tag.GetProperty("Name").GetString() ?? "")),
            ("$description", tag.TryGetProperty("Description", out var description) && description.ValueKind != JsonValueKind.Null ? description.GetString() : null),
            ("$color", tag.TryGetProperty("Color", out var color) && color.ValueKind != JsonValueKind.Null ? color.GetString() : null),
            ("$source", tag.GetProperty("Source").GetString()), ("$at", Now()));
        foreach (JsonElement movie in document.RootElement.GetProperty("MovieIds").EnumerateArray())
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) SELECT $movie,$tag,$at WHERE EXISTS(SELECT 1 FROM Movies WHERE Id=$movie)",
                ("$movie", movie.GetInt64()), ("$tag", tagId), ("$at", Now()));
        await ExecuteAsync(connection, transaction, "UPDATE OperationAudit SET RevertedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", auditId));
        long rollbackAudit = await AuditAsync(connection, transaction, "Rollback", "OperationAudit", auditId, null, new { RestoredTagId = tagId });
        await transaction.CommitAsync();
        return new(true, rollbackAudit, "标签删除操作已撤销。");
    }

    public Task<MutationResult> UpdateMovieTagsAsync(long movieId, MovieTagsCommand input) =>
        UpdateTagsCoreAsync([movieId], input.AddTagIds, input.RemoveTagIds);

    public Task<MutationResult> UpdateBatchTagsAsync(BatchTagsCommand input) =>
        UpdateTagsCoreAsync(input.MovieIds, input.AddTagIds, input.RemoveTagIds);

    private async Task<MutationResult> UpdateTagsCoreAsync(IReadOnlyList<long> movieIds, IReadOnlyList<long> addTagIds, IReadOnlyList<long> removeTagIds)
    {
        long[] movies = movieIds.Distinct().Where(id => id > 0).ToArray();
        long[] adds = addTagIds.Distinct().Where(id => id > 0).ToArray();
        long[] removes = removeTagIds.Distinct().Where(id => id > 0 && !adds.Contains(id)).ToArray();
        if (movies.Length is 0 or > 500) throw new ArgumentException("请选择 1 到 500 部影片。");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (long movieId in movies) {
            await EnsureMovieAsync(connection, movieId, transaction);
            foreach (long tagId in adds) {
                await EnsureTagAsync(connection, tagId, transaction);
                await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) VALUES($movie,$tag,$at)", ("$movie", movieId), ("$tag", tagId), ("$at", Now()));
            }
            foreach (long tagId in removes)
                await ExecuteAsync(connection, transaction, "DELETE FROM MovieTags WHERE MovieId=$movie AND TagId=$tag", ("$movie", movieId), ("$tag", tagId));
        }
        long audit = await AuditAsync(connection, transaction, "MovieTags", "Movie", null, null, new { MovieIds = movies, AddTagIds = adds, RemoveTagIds = removes });
        await transaction.CommitAsync();
        return new(true, audit, $"已更新 {movies.Length} 部影片的标签关系。");
    }

    public async Task<MutationResult> UpdateActorAsync(long actorId, ActorCommand input)
    {
        string name = ValidateName(input.Name, "演员");
        await using var connection = await OpenAsync();
        var before = await ReadActorAsync(connection, actorId) ?? throw new KeyNotFoundException("演员不存在。");
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "UPDATE Actors SET Name=$name,NormalizedName=$normalized,Alias=$alias,Gender=$gender,BirthDate=$birth,Description=$description,UpdatedAt=$at WHERE Id=$id",
            ("$name", name), ("$normalized", Normalize(name)), ("$alias", input.Alias?.Trim() ?? before.Alias), ("$gender", input.Gender ?? before.Gender),
            ("$birth", input.BirthDate?.Trim() ?? before.BirthDate), ("$description", input.Description?.Trim() ?? before.Description), ("$at", Now()), ("$id", actorId));
        long audit = await AuditAsync(connection, transaction, "ActorUpdate", "Actor", actorId, before, input);
        await transaction.CommitAsync();
        return new(true, audit, "演员资料已保存。");
    }

    public async Task<MutationResult> SetMovieActorsAsync(long movieId, MovieActorsCommand input)
    {
        long[] actors = input.ActorIds.Distinct().Where(id => id > 0).ToArray();
        await using var connection = await OpenAsync();
        await EnsureMovieAsync(connection, movieId);
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (long actorId in actors) await EnsureActorAsync(connection, actorId, transaction);
        long[] before = (await ReadIdsAsync(connection, transaction, "SELECT ActorId FROM MovieActors WHERE MovieId=$id ORDER BY ActorId", ("$id", movieId))).ToArray();
        await ExecuteAsync(connection, transaction, "DELETE FROM MovieActors WHERE MovieId=$id", ("$id", movieId));
        int order = 0;
        foreach (long actorId in actors)
            await ExecuteAsync(connection, transaction, "INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES($movie,$actor,'',$order)", ("$movie", movieId), ("$actor", actorId), ("$order", order++));
        long audit = await AuditAsync(connection, transaction, "MovieActors", "Movie", movieId, before, actors);
        await transaction.CommitAsync();
        return new(true, audit, "演员关系已保存。");
    }

    public async Task<ActorRepairPreview> PreviewActorRepairAsync()
    {
        await using var connection = await OpenAsync();
        long actors = await ScalarAsync(connection, "SELECT COUNT(*) FROM Actors WHERE Id=0");
        long relations = await ScalarAsync(connection, "SELECT COUNT(*) FROM MovieActors WHERE ActorId=0");
        string token = Grant("ActorRepair", 0);
        var warnings = new List<string>();
        if (actors == 0 && relations == 0) warnings.Add("当前数据库没有 ActorID=0 候选，无需修改。");
        else warnings.Add("仅修复 ActorID=0；空名或无法确定身份的演员不会被自动覆盖。");
        return new(actors, relations, token, warnings);
    }

    public async Task<MutationResult> ApplyActorRepairAsync(ConfirmCommand input)
    {
        Consume(input.ConfirmationToken, "ActorRepair", 0);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long relations = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM MovieActors WHERE ActorId=0");
        if (relations == 0) {
            long taskId = await RecordTaskAsync(connection, transaction, "ActorRepair", "Completed", "没有需要修复的 ActorID=0 关系。");
            await transaction.CommitAsync();
            return new(false, taskId, "没有需要修复的 ActorID=0 关系。");
        }
        await transaction.RollbackAsync();
        string backupPath = await BackupDatabaseAsync("actor-repair");
        await using var repairTransaction = await connection.BeginTransactionAsync();
        var actor = await ReadActorAsync(connection, 0, repairTransaction) ?? throw new InvalidOperationException("ActorID=0 关系缺少对应演员，自动修复已停止。");
        string normalized = Normalize(actor.Name);
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidOperationException("ActorID=0 演员名称为空，无法安全判断身份。");
        long target = await ScalarAsync(connection, repairTransaction, "SELECT COALESCE(MAX(Id),0) FROM Actors WHERE Id<>0 AND NormalizedName=$name", ("$name", normalized));
        if (target == 0) {
            await ExecuteAsync(connection, repairTransaction, "INSERT INTO Actors(Name,NormalizedName,SortName,Alias,Gender,BirthDate,Description,ExternalId,LegacySource,LegacyId,CreatedAt,UpdatedAt) SELECT Name,NormalizedName,SortName,Alias,Gender,BirthDate,Description,ExternalId,LegacySource,NULL,$at,$at FROM Actors WHERE Id=0", ("$at", Now()));
            target = await ScalarAsync(connection, repairTransaction, "SELECT last_insert_rowid()");
        }
        var movieIds = await ReadIdsAsync(connection, repairTransaction, "SELECT MovieId FROM MovieActors WHERE ActorId=0", Array.Empty<(string, object?)>());
        foreach (long movieId in movieIds)
            await ExecuteAsync(connection, repairTransaction, "INSERT OR IGNORE INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) SELECT MovieId,$target,RoleName,SortOrder FROM MovieActors WHERE MovieId=$movie AND ActorId=0", ("$target", target), ("$movie", movieId));
        await ExecuteAsync(connection, repairTransaction, "DELETE FROM MovieActors WHERE ActorId=0");
        await ExecuteAsync(connection, repairTransaction, "DELETE FROM Actors WHERE Id=0");
        long audit = await AuditAsync(connection, repairTransaction, "ActorRepair", "Actor", target, new { Actor = actor, MovieIds = movieIds }, new { TargetActorId = target, BackupPath = backupPath });
        await RecordTaskAsync(connection, repairTransaction, "ActorRepair", "Completed", $"已修复 {relations} 条关系。", relations);
        await repairTransaction.CommitAsync();
        return new(true, audit, $"已修复 {relations} 条 ActorID=0 关系。");
    }

    public async Task RecordPlaybackAsync(long movieId, long mediaFileId, string? playerName, DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPlayedAt,LastPositionSeconds,UpdatedAt,HasUserRating)
            VALUES($movie,0,0,1,$ended,0,$ended,0)
            ON CONFLICT(MovieId) DO UPDATE SET PlayCount=PlayCount+1,LastPlayedAt=$ended,UpdatedAt=$ended
            """, ("$movie", movieId), ("$ended", endedAt.ToString("O")));
        await ExecuteAsync(connection, transaction, "INSERT INTO PlayHistory(MovieId,MediaFileId,StartedAt,EndedAt,Completed,PlayerName) VALUES($movie,$file,$started,$ended,1,$player)",
            ("$movie", movieId), ("$file", mediaFileId), ("$started", startedAt.ToString("O")), ("$ended", endedAt.ToString("O")), ("$player", playerName));
        await transaction.CommitAsync();
    }

    public async Task<bool> RememberDeletedRatingAsync(long movieId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT f.FileName,s.UserRating,s.HasUserRating FROM Movies m
            JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
            LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id LIMIT 1
            """; command.Parameters.AddWithValue("$id", movieId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("影片不存在或没有主文件。");
        string fileName = reader.GetString(0); double rating = reader.IsDBNull(1) ? 0 : reader.GetDouble(1); bool hasRating = !reader.IsDBNull(2) && reader.GetInt64(2) == 1;
        await reader.DisposeAsync();
        if (!hasRating) { await transaction.RollbackAsync(); return false; }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO DeletedRatingMemory(FileName,NormalizedFileName,Rating,RememberedAt,SourceMovieId)
            VALUES($file,$normalized,$rating,$at,$movie)
            ON CONFLICT(NormalizedFileName) DO UPDATE SET Rating=excluded.Rating,RememberedAt=excluded.RememberedAt,SourceMovieId=excluded.SourceMovieId,RestoredAt=NULL
            """, ("$file", fileName), ("$normalized", NormalizeFile(fileName)), ("$rating", rating), ("$at", Now()), ("$movie", movieId));
        await transaction.CommitAsync(); return true;
    }

    public async Task<bool> RestoreDeletedRatingAsync(long movieId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long hasRating = await ScalarAsync(connection, transaction, "SELECT COALESCE(HasUserRating,0) FROM UserMovieState WHERE MovieId=$id", ("$id", movieId));
        if (hasRating == 1) { await transaction.RollbackAsync(); return false; }
        await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT d.Id,d.Rating FROM MediaFiles f JOIN DeletedRatingMemory d ON d.NormalizedFileName=lower(trim(f.FileName))
            WHERE f.MovieId=$movie AND f.IsPrimary=1 AND f.MediaType='Video' AND d.RestoredAt IS NULL LIMIT 1
            """; command.Parameters.AddWithValue("$movie", movieId);
        await using var reader = await command.ExecuteReaderAsync(); if (!await reader.ReadAsync()) { await transaction.RollbackAsync(); return false; }
        long memoryId = reader.GetInt64(0); double rating = reader.GetDouble(1); await reader.DisposeAsync();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating)
            VALUES($movie,0,$rating,0,0,$at,1)
            ON CONFLICT(MovieId) DO UPDATE SET UserRating=$rating,HasUserRating=1,UpdatedAt=$at WHERE HasUserRating=0
            """, ("$movie", movieId), ("$rating", rating), ("$at", Now()));
        await ExecuteAsync(connection, transaction, "UPDATE DeletedRatingMemory SET RestoredAt=$at WHERE Id=$id", ("$at", Now()), ("$id", memoryId));
        await transaction.CommitAsync(); return true;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        // Writes use a private cache so an earlier read-only shared-cache connection can never
        // downgrade the command connection to SQLITE_READONLY.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        return connection;
    }

    private async Task<string> BackupDatabaseAsync(string operation)
    {
        string databaseDirectory = Path.GetDirectoryName(databasePath) ?? throw new InvalidOperationException("无法确定数据库目录。");
        string backupDirectory = Path.Combine(databaseDirectory, "backups", "operations", DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, $"{operation}-LocalMediaManager.db");
        await using var source = await OpenAsync();
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        await destination.OpenAsync();
        source.BackupDatabase(destination);
        return backupPath;
    }

    private static async Task SyncLegacyFavoriteTagAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, long movieId, bool favorite)
    {
        var ids = await ReadIdsAsync(connection, transaction, "SELECT Id FROM Tags WHERE lower(trim(Name)) IN ('已收藏','我的收藏') OR lower(trim(NormalizedName)) IN ('已收藏','我的收藏')", Array.Empty<(string, object?)>());
        foreach (long tagId in ids) {
            if (favorite) await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) VALUES($movie,$tag,$at)", ("$movie", movieId), ("$tag", tagId), ("$at", Now()));
            else await ExecuteAsync(connection, transaction, "DELETE FROM MovieTags WHERE MovieId=$movie AND TagId=$tag", ("$movie", movieId), ("$tag", tagId));
        }
    }

    private string Grant(string operation, long entityId) { string token = Convert.ToHexString(Guid.NewGuid().ToByteArray()); grants[token] = new(operation, entityId, DateTimeOffset.UtcNow.AddMinutes(5)); return token; }
    private void Consume(string token, string operation, long entityId) { if (!grants.TryRemove(token, out var grant) || grant.Operation != operation || grant.EntityId != entityId || grant.ExpiresAt < DateTimeOffset.UtcNow) throw new UnauthorizedAccessException("确认令牌无效或已过期，请重新预览影响范围。"); }
    private static string ValidateName(string value, string label) { string name = value?.Trim() ?? ""; if (name.Length is < 1 or > 100) throw new ArgumentException($"{label}名称长度必须为 1 到 100 个字符。"); return name; }
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeFile(string value) => Path.GetFileName(value).Trim().ToLowerInvariant();
    private static async Task EnsureMovieAsync(SqliteConnection c, long id, System.Data.Common.DbTransaction? tx = null) { if (await ScalarAsync(c, tx, "SELECT COUNT(*) FROM Movies WHERE Id=$id", ("$id", id)) == 0) throw new KeyNotFoundException("影片不存在。"); }
    private static async Task EnsureTagAsync(SqliteConnection c, long id, System.Data.Common.DbTransaction tx) { if (await ScalarAsync(c, tx, "SELECT COUNT(*) FROM Tags WHERE Id=$id", ("$id", id)) == 0) throw new KeyNotFoundException($"标签 {id} 不存在。"); }
    private static async Task EnsureActorAsync(SqliteConnection c, long id, System.Data.Common.DbTransaction tx) { if (await ScalarAsync(c, tx, "SELECT COUNT(*) FROM Actors WHERE Id=$id", ("$id", id)) == 0) throw new KeyNotFoundException($"演员 {id} 不存在。"); }
    private static async Task<(bool Favorite, double Rating, bool HasRating)> ReadStateAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, long id) { await using var cmd=c.CreateCommand();cmd.Transaction=(SqliteTransaction)tx;cmd.CommandText="SELECT IsFavorite,UserRating,HasUserRating FROM UserMovieState WHERE MovieId=$id";cmd.Parameters.AddWithValue("$id",id);await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?(r.GetInt64(0)==1,r.GetDouble(1),r.GetInt64(2)==1):(false,0,false); }
    private static async Task<dynamic?> ReadTagAsync(SqliteConnection c, long id, System.Data.Common.DbTransaction? tx=null) { await using var cmd=c.CreateCommand();cmd.Transaction=tx as SqliteTransaction;cmd.CommandText="SELECT Id,Name,Description,Color,Source FROM Tags WHERE Id=$id";cmd.Parameters.AddWithValue("$id",id);await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?new { Id=r.GetInt64(0),Name=r.GetString(1),Description=r.IsDBNull(2)?null:r.GetString(2),Color=r.IsDBNull(3)?null:r.GetString(3),Source=r.GetString(4)}:null; }
    private static async Task<dynamic?> ReadActorAsync(SqliteConnection c, long id, System.Data.Common.DbTransaction? tx=null) { await using var cmd=c.CreateCommand();cmd.Transaction=tx as SqliteTransaction;cmd.CommandText="SELECT Id,Name,Alias,Gender,BirthDate,Description FROM Actors WHERE Id=$id";cmd.Parameters.AddWithValue("$id",id);await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?new {Id=r.GetInt64(0),Name=r.GetString(1),Alias=r.IsDBNull(2)?null:r.GetString(2),Gender=r.IsDBNull(3)?(int?)null:r.GetInt32(3),BirthDate=r.IsDBNull(4)?null:r.GetString(4),Description=r.IsDBNull(5)?null:r.GetString(5)}:null; }
    private static async Task<long> AuditAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string operation, string entity, long? id, object? before, object? after) { await using var cmd=c.CreateCommand();cmd.Transaction=(SqliteTransaction)tx;cmd.CommandText="INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,CreatedAt) VALUES($op,$entity,$id,$before,$after,$at);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$op",operation);cmd.Parameters.AddWithValue("$entity",entity);cmd.Parameters.AddWithValue("$id",(object?)id??DBNull.Value);cmd.Parameters.AddWithValue("$before",before is null?DBNull.Value:JsonSerializer.Serialize(before));cmd.Parameters.AddWithValue("$after",after is null?DBNull.Value:JsonSerializer.Serialize(after));cmd.Parameters.AddWithValue("$at",Now());return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task<long> RecordTaskAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string type, string status, string result, long completed=0) { await using var cmd=c.CreateCommand();cmd.Transaction=(SqliteTransaction)tx;cmd.CommandText="INSERT INTO Tasks(TaskType,Status,Progress,TotalItems,CompletedItems,ResultJson,CreatedAt,StartedAt,CompletedAt) VALUES($type,$status,100,$total,$completed,$result,$at,$at,$at);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$type",type);cmd.Parameters.AddWithValue("$status",status);cmd.Parameters.AddWithValue("$total",completed);cmd.Parameters.AddWithValue("$completed",completed);cmd.Parameters.AddWithValue("$result",JsonSerializer.Serialize(new { message=result }));cmd.Parameters.AddWithValue("$at",Now());return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task ExecuteAsync(SqliteConnection c, System.Data.Common.DbTransaction? tx, string sql, params (string Name, object? Value)[] args) { await using var cmd=c.CreateCommand();cmd.Transaction=tx as SqliteTransaction;cmd.CommandText=sql;foreach(var a in args)cmd.Parameters.AddWithValue(a.Name,a.Value??DBNull.Value);await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> ScalarAsync(SqliteConnection c, string sql, params (string Name, object? Value)[] args)=>await ScalarAsync(c,null,sql,args);
    private static async Task<long> ScalarAsync(SqliteConnection c, System.Data.Common.DbTransaction? tx, string sql, params (string Name, object? Value)[] args){await using var cmd=c.CreateCommand();cmd.Transaction=tx as SqliteTransaction;cmd.CommandText=sql;foreach(var a in args)cmd.Parameters.AddWithValue(a.Name,a.Value??DBNull.Value);return Convert.ToInt64(await cmd.ExecuteScalarAsync()??0L);}
    private static async Task<IReadOnlyList<long>> ReadIdsAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,params (string Name,object? Value)[] args){await using var cmd=c.CreateCommand();cmd.Transaction=(SqliteTransaction)tx;cmd.CommandText=sql;foreach(var a in args)cmd.Parameters.AddWithValue(a.Name,a.Value??DBNull.Value);var ids=new List<long>();await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())ids.Add(r.GetInt64(0));return ids;}
    private sealed record PreviewGrant(string Operation, long EntityId, DateTimeOffset ExpiresAt);
}
