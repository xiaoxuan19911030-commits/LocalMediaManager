using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record NfoData(string Code, string? Title, string? OriginalTitle, string? Plot, double? Rating,
    string? ReleaseDate, int? RuntimeMinutes, string? Director, string? Studio, string? Publisher,
    string? Country, IReadOnlyList<string> Actors, IReadOnlyList<string> Tags, IReadOnlyList<string> Genres,
    IReadOnlyList<string> Series, IReadOnlyList<string> ImageReferences, string? Source, string? SourceId)
{
    public string? PosterPath { get; init; }
    public string? FanartPath { get; init; }
    public IReadOnlyDictionary<string, string> ActorImagePaths { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
public sealed record NfoPreview(long MovieId, string Path, string Ownership, bool Locked, bool Exists,
    bool CanApply, string ConfirmationToken, IReadOnlyList<string> Changes, IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings, NfoData Data);
public sealed record NfoConfirmCommand(string ConfirmationToken);
public sealed record NfoMutationResult(bool Changed, string Path, string Ownership, bool Locked, string Message);
public sealed record NfoSettingsDto(string ExportPolicy, string OutputDirectory, bool FillEmptyOnly, bool IncludeImages);

public sealed class NfoService(string databasePath, MediaStoragePathResolver pathResolver)
{
    public async Task<NfoSettingsDto> ReadSettingsAsync(CancellationToken cancellationToken = default)
    {
        NfoSettingsDto defaults = SettingsDefaults.Unified.Nfo;
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'nfo.%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values[reader.GetString(0)] = reader.GetString(1);
        return new(TextSetting(values, "nfo.export.policy", defaults.ExportPolicy),
            TextSetting(values, "nfo.export.outputDirectory", defaults.OutputDirectory), true,
            BoolSetting(values, "nfo.export.includeImages", defaults.IncludeImages));
    }

    public async Task<NfoSettingsDto> SaveSettingsAsync(NfoSettingsDto input, CancellationToken cancellationToken = default)
    {
        string policy = input.ExportPolicy is "SkipExisting" or "SeparateFile" ? input.ExportPolicy : "SkipExisting";
        string output = string.IsNullOrWhiteSpace(input.OutputDirectory) ? "" : Path.GetFullPath(input.OutputDirectory.Trim());
        var clean = new NfoSettingsDto(policy, output, true, input.IncludeImages);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await StoreSettingAsync(connection, transaction, "nfo.export.policy", clean.ExportPolicy, "string");
        await StoreSettingAsync(connection, transaction, "nfo.export.outputDirectory", clean.OutputDirectory, "string");
        await StoreSettingAsync(connection, transaction, "nfo.import.fillEmptyOnly", true, "boolean");
        await StoreSettingAsync(connection, transaction, "nfo.export.includeImages", clean.IncludeImages, "boolean");
        await transaction.CommitAsync(cancellationToken);
        return clean;
    }
    public async Task<(string? Path, bool Created)> WriteAsync(SyncMovie movie, ProviderMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movie.PrimaryFile)) return (null, false);
        string path = (await pathResolver.ResolveForMovieAsync(new MediaStorageMovie(movie.Id, movie.Code, movie.Title), "NFO", ".nfo", null, null, cancellationToken)).FullPath;
        NfoData data = new(metadata.Code, metadata.Title ?? movie.Title, metadata.Title,
            metadata.Description, null, metadata.ReleaseDate,
            metadata.DurationSeconds is > 0 ? metadata.DurationSeconds / 60 : null,
            metadata.Director, metadata.Studio, metadata.Publisher, null, metadata.Actors, [], metadata.Genres,
            string.IsNullOrWhiteSpace(metadata.Series) ? [] : [metadata.Series],
            metadata.Images.Select(image => image.Url).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            metadata.Provider, metadata.ExternalId);
        return await WriteSafeAsync(movie.Id, path, data, metadata.Provider, cancellationToken);
    }

    public async Task<(string? Path, bool Created)> WriteFromDatabaseAsync(long movieId, CancellationToken cancellationToken)
    {
        (NfoData data, string path) = await ReadDatabaseDataAsync(movieId, cancellationToken);
        return await WriteSafeAsync(movieId, path, data, "LocalMediaManager", cancellationToken);
    }

    public async Task<NfoPreview> PreviewExportAsync(long movieId, CancellationToken cancellationToken = default)
    {
        (NfoData data, string path) = await ReadDatabaseDataAsync(movieId, cancellationToken);
        (string ownership, bool locked) = await ReadOwnershipAsync(movieId, path, cancellationToken);
        bool exists = File.Exists(path);
        bool mayUpdate = !exists || ownership == "LMM" && !locked;
        string? hash = exists ? await HashAsync(path, cancellationToken) : null;
        string token = Token("export", movieId, path, hash, JsonSerializer.Serialize(data));
        var warnings = new List<string>();
        if (exists && !mayUpdate) warnings.Add("现有 NFO 视为用户文件并已锁定；默认不会覆盖。可先保留原文件，再使用单独的 .lmm.nfo 输出。 ");
        return new(movieId, path, ownership, locked, exists, mayUpdate, token,
            ["按确定顺序导出标题、番号、简介、评分、日期、演员、标签、类型、系列、厂商和图片引用。"],
            [], warnings, data);
    }

    public async Task<NfoMutationResult> ExportAsync(long movieId, string confirmationToken,
        bool separateWhenLocked = false, CancellationToken cancellationToken = default)
    {
        NfoPreview preview = await PreviewExportAsync(movieId, cancellationToken);
        Verify(preview.ConfirmationToken, confirmationToken);
        string path = preview.Path;
        if (!preview.CanApply) {
            NfoSettingsDto settings = await ReadSettingsAsync(cancellationToken);
            if (!separateWhenLocked && settings.ExportPolicy != "SeparateFile") return new(false, path, preview.Ownership, preview.Locked, "现有用户 NFO 已锁定，未覆盖。");
            path = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".lmm.nfo");
        }
        (string? written, _) = await WriteSafeAsync(movieId, path, preview.Data, "LocalMediaManager", cancellationToken);
        return new(true, written ?? path, "LMM", false, "NFO 已安全写入。");
    }

    public async Task<NfoPreview> PreviewImportAsync(long movieId, CancellationToken cancellationToken = default)
    {
        string path = await ResolveExistingPathAsync(movieId, cancellationToken)
            ?? throw new FileNotFoundException("影片旁或数据库记录中没有可导入的 NFO。");
        NfoData data = await ParseAsync(path, cancellationToken);
        DatabaseMovie current = await ReadMovieAsync(movieId, cancellationToken);
        var changes = new List<string>(); var conflicts = new List<string>();
        Compare("番号", current.Code, data.Code, changes, conflicts);
        Compare("标题", current.Title, data.Title, changes, conflicts);
        Compare("原始标题", current.OriginalTitle, data.OriginalTitle, changes, conflicts);
        Compare("简介", current.Description, data.Plot, changes, conflicts);
        Compare("发行日期", current.ReleaseDate, data.ReleaseDate, changes, conflicts);
        if (current.DurationSeconds == 0 && data.RuntimeMinutes is > 0) changes.Add("时长");
        else if (current.DurationSeconds > 0 && data.RuntimeMinutes is > 0 && current.DurationSeconds / 60 != data.RuntimeMinutes) conflicts.Add("时长");
        if (current.ProviderRating is null && data.Rating.HasValue) changes.Add("来源评分");
        else if (current.ProviderRating.HasValue && data.Rating.HasValue && Math.Abs(current.ProviderRating.Value - data.Rating.Value) > 0.001) conflicts.Add("来源评分");
        changes.AddRange(data.Actors.Select(value => $"演员：{value}"));
        changes.AddRange(data.Tags.Select(value => $"标签：{value}"));
        changes.AddRange(data.Genres.Select(value => $"类型：{value}"));
        string hash = await HashAsync(path, cancellationToken);
        string token = Token("import", movieId, path, hash, JsonSerializer.Serialize(data));
        return new(movieId, path, "User", true, true, true, token, changes.Distinct().ToArray(),
            conflicts.Distinct().ToArray(), conflicts.Count == 0 ? [] : ["冲突字段不会覆盖现有用户数据；仅补全空字段并追加缺失关系。"], data);
    }

    public async Task<NfoMutationResult> ImportAsync(long movieId, string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        NfoPreview preview = await PreviewImportAsync(movieId, cancellationToken);
        Verify(preview.ConfirmationToken, confirmationToken);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string before = JsonSerializer.Serialize(await ReadMovieAsync(connection, movieId, cancellationToken));
        await ExecuteAsync(connection, transaction, """
            UPDATE Movies SET
              Code=CASE WHEN trim(ifnull(Code,''))='' THEN $code ELSE Code END,
              Title=CASE WHEN trim(ifnull(Title,''))='' OR Title=Code THEN COALESCE($title,Title) ELSE Title END,
              OriginalTitle=CASE WHEN trim(ifnull(OriginalTitle,''))='' THEN $original ELSE OriginalTitle END,
              Description=CASE WHEN trim(ifnull(Description,''))='' THEN $plot ELSE Description END,
              ReleaseDate=CASE WHEN trim(ifnull(ReleaseDate,''))='' THEN $release ELSE ReleaseDate END,
              DurationSeconds=CASE WHEN DurationSeconds=0 THEN COALESCE($runtime,0) ELSE DurationSeconds END,
              ProviderRating=CASE WHEN ProviderRating IS NULL OR ProviderRating=0 THEN $rating ELSE ProviderRating END,
              NfoPath=$path,UpdatedAt=$at WHERE Id=$id
            """, ("$code", Empty(preview.Data.Code)), ("$title", Empty(preview.Data.Title)),
            ("$original", Empty(preview.Data.OriginalTitle)), ("$plot", Empty(preview.Data.Plot)),
            ("$release", Empty(preview.Data.ReleaseDate)), ("$runtime", preview.Data.RuntimeMinutes * 60),
            ("$rating", preview.Data.Rating), ("$path", preview.Path), ("$at", Now()), ("$id", movieId));
        foreach (string value in preview.Data.Actors) await AddRelationAsync(connection, transaction, movieId, "Actors", "MovieActors", "ActorId", value, cancellationToken);
        foreach (string value in preview.Data.Tags) await AddRelationAsync(connection, transaction, movieId, "Tags", "MovieTags", "TagId", value, cancellationToken);
        foreach (string value in preview.Data.Genres) await AddRelationAsync(connection, transaction, movieId, "Genres", "MovieGenres", "GenreId", value, cancellationToken);
        foreach (string value in preview.Data.Series) await AddRelationAsync(connection, transaction, movieId, "Series", "MovieSeries", "SeriesId", value, cancellationToken);
        if (!string.IsNullOrWhiteSpace(preview.Data.Studio)) await AddRelationAsync(connection, transaction, movieId, "Studios", "MovieStudios", "StudioId", preview.Data.Studio!, cancellationToken);
        await UpsertDocumentAsync(connection, transaction, movieId, preview.Path, "User", true,
            await HashAsync(preview.Path, cancellationToken), preview.Data.Source, false, cancellationToken);
        string after = JsonSerializer.Serialize(await ReadMovieAsync(connection, movieId, cancellationToken));
        await ExecuteAsync(connection, transaction,
            "INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,CreatedAt) VALUES('NfoImport','Movie',$id,$before,$after,$at)",
            ("$id", movieId), ("$before", before), ("$after", after), ("$at", Now()));
        await transaction.CommitAsync(cancellationToken);
        return new(true, preview.Path, "User", true, $"NFO 已导入；{preview.Conflicts.Count} 个冲突字段保持原值。");
    }

    public static async Task<NfoData> ParseAsync(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Async = true };
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document;
        try { document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken); }
        catch (XmlException error) { throw new InvalidDataException($"NFO XML 格式无效：{error.Message}", error); }
        XElement root = document.Root is { Name.LocalName: "movie" } movie ? movie : throw new InvalidDataException("NFO 根节点必须是 movie。");
        string? One(params string[] names) => root.Elements().FirstOrDefault(element => names.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value.Trim();
        string[] Many(params string[] names) => root.Elements().Where(element => names.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).Select(element => element.Value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] actors = root.Elements().Where(element => element.Name.LocalName.Equals("actor", StringComparison.OrdinalIgnoreCase)).Select(element => element.Elements().FirstOrDefault(child => child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] images = root.Descendants().Where(element => element.Name.LocalName.Equals("thumb", StringComparison.OrdinalIgnoreCase)).Select(element => (element.Attribute("preview")?.Value ?? element.Value).Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        double? rating = double.TryParse(One("rating"), NumberStyles.Float, CultureInfo.InvariantCulture, out double score) ? score : null;
        int? runtime = int.TryParse(One("runtime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ? minutes : null;
        return new((One("id", "num") ?? "").ToUpperInvariant(), One("title"), One("originaltitle"), One("plot", "outline"), rating,
            One("premiered", "release", "releasedate"), runtime, One("director"), One("studio"), One("publisher"), One("country"), actors,
            Many("tag", "set"), Many("genre"), Many("series"), images, One("source"), One("sourceid"));
    }

    private async Task<(string? Path, bool Created)> WriteSafeAsync(long movieId, string path, NfoData data,
        string provider, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        (string ownership, bool locked) = await ReadOwnershipAsync(movieId, path, cancellationToken);
        bool existed = File.Exists(path);
        if (existed && (ownership != "LMM" || locked)) {
            await RegisterExistingUserDocumentAsync(movieId, path, cancellationToken);
            return (path, false);
        }
        string temporary = path + $".{Guid.NewGuid():N}.lmm-write";
        string? backup = existed ? path + $".{Guid.NewGuid():N}.lmm-backup" : null;
        bool installed = false;
        try {
            XDocument document = BuildDocument(data);
            var writerSettings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = Environment.NewLine };
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                await using XmlWriter writer = XmlWriter.Create(output, writerSettings);
                await document.SaveAsync(writer, cancellationToken); await writer.FlushAsync(); await output.FlushAsync(cancellationToken);
            }
            _ = await ParseAsync(temporary, cancellationToken);
            if (existed) File.Replace(temporary, path, backup, true); else File.Move(temporary, path, false);
            installed = true;
            string hash = await HashAsync(path, cancellationToken);
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await UpsertDocumentAsync(connection, transaction, movieId, path, "LMM", false, hash, provider, true, cancellationToken);
            await ExecuteAsync(connection, transaction, "UPDATE Movies SET NfoPath=CASE WHEN trim(ifnull(NfoPath,''))='' THEN $path ELSE NfoPath END,UpdatedAt=$at WHERE Id=$id", ("$path", path), ("$at", Now()), ("$id", movieId));
            await transaction.CommitAsync(cancellationToken);
            if (backup is not null && File.Exists(backup)) File.Delete(backup);
            return (path, !existed);
        }
        catch {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            if (backup is not null && File.Exists(backup)) {
                try { if (File.Exists(path)) File.Delete(path); File.Move(backup, path); } catch { }
            }
            else if (!existed && installed) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
            throw;
        }
    }

    private static XDocument BuildDocument(NfoData data)
    {
        var root = new XElement("movie",
            Element("title", data.Title), Element("originaltitle", data.OriginalTitle), Element("id", data.Code), Element("num", data.Code),
            Element("plot", data.Plot), data.Rating.HasValue ? new XElement("rating", data.Rating.Value.ToString("0.###", CultureInfo.InvariantCulture)) : null,
            Element("premiered", data.ReleaseDate), Element("release", data.ReleaseDate), data.RuntimeMinutes.HasValue ? new XElement("runtime", data.RuntimeMinutes.Value) : null,
            Element("director", data.Director), Element("studio", data.Studio), Element("publisher", data.Publisher), Element("country", data.Country),
            data.Tags.Order(StringComparer.OrdinalIgnoreCase).Select(value => new XElement("tag", value)),
            data.Genres.Order(StringComparer.OrdinalIgnoreCase).Select(value => new XElement("genre", value)),
            data.Series.Order(StringComparer.OrdinalIgnoreCase).Select(value => new XElement("series", value)),
            data.Actors.Order(StringComparer.OrdinalIgnoreCase).Select(value => ActorElement(value, data.ActorImagePaths)),
            Element("thumb", data.PosterPath) is { } poster ? new XElement("thumb", new XAttribute("aspect", "poster"), poster.Value) : null,
            data.FanartPath is { Length: > 0 } fanart ? new XElement("fanart", new XElement("thumb", fanart)) : null,
            data.ImageReferences.Count > 0 ? new XElement("fanart", data.ImageReferences.Order(StringComparer.OrdinalIgnoreCase).Select(value => new XElement("thumb", new XAttribute("preview", value), value))) : null,
            Element("source", data.Source), Element("sourceid", data.SourceId), new XElement("lmmownership", "LMM"));
        return new(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private async Task<(NfoData Data, string Path)> ReadDatabaseDataAsync(long movieId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        DatabaseMovie movie = await ReadMovieAsync(connection, movieId, token);
        string? primary = await ScalarTextAsync(connection, "SELECT FilePath FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 ORDER BY Id LIMIT 1", token, ("$id", movieId));
        if (string.IsNullOrWhiteSpace(primary)) throw new InvalidOperationException("影片没有主媒体文件，无法确定 NFO 输出位置。");
        string path = Path.ChangeExtension(primary, ".nfo");
        var data = new NfoData(movie.Code ?? "", movie.Title, movie.OriginalTitle, movie.Description, movie.ProviderRating,
            movie.ReleaseDate, movie.DurationSeconds > 0 ? movie.DurationSeconds / 60 : null,
            await FirstRelationAsync(connection, movieId, "Directors", "MovieDirectors", "DirectorId", token),
            await FirstRelationAsync(connection, movieId, "Studios", "MovieStudios", "StudioId", token), null, null,
            await RelationsAsync(connection, movieId, "Actors", "MovieActors", "ActorId", token),
            await RelationsAsync(connection, movieId, "Tags", "MovieTags", "TagId", token),
            await RelationsAsync(connection, movieId, "Genres", "MovieGenres", "GenreId", token),
            await RelationsAsync(connection, movieId, "Series", "MovieSeries", "SeriesId", token),
            [], "LocalMediaManager", movieId.ToString(CultureInfo.InvariantCulture)) {
            PosterPath = await ImagePathAsync(connection, movieId, "Poster", token),
            FanartPath = await ImagePathAsync(connection, movieId, "Fanart", token),
            ActorImagePaths = await ActorImagePathsAsync(connection, movieId, token),
        };
        return (data, path);
    }

    private async Task<DatabaseMovie> ReadMovieAsync(long movieId, CancellationToken token) { await using SqliteConnection connection=await OpenAsync(SqliteOpenMode.ReadOnly,token);return await ReadMovieAsync(connection,movieId,token); }
    private static async Task<DatabaseMovie> ReadMovieAsync(SqliteConnection connection, long movieId, CancellationToken token) {
        await using SqliteCommand command=connection.CreateCommand();command.CommandText="SELECT Code,Title,OriginalTitle,Description,ReleaseDate,DurationSeconds,ProviderRating FROM Movies WHERE Id=$id";command.Parameters.AddWithValue("$id",movieId);
        await using SqliteDataReader reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new KeyNotFoundException("影片不存在。");
        return new(Text(reader,0),Text(reader,1),Text(reader,2),Text(reader,3),Text(reader,4),reader.GetInt32(5),reader.IsDBNull(6)?null:reader.GetDouble(6));
    }
    private async Task<string?> ResolveExistingPathAsync(long movieId, CancellationToken token) { await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadOnly,token);string? stored=await ScalarTextAsync(c,"SELECT NfoPath FROM Movies WHERE Id=$id",token,("$id",movieId));if(!string.IsNullOrWhiteSpace(stored)&&File.Exists(stored))return stored;string? media=await ScalarTextAsync(c,"SELECT FilePath FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 ORDER BY Id LIMIT 1",token,("$id",movieId));if(string.IsNullOrWhiteSpace(media))return null;string adjacent=Path.ChangeExtension(media,".nfo");return File.Exists(adjacent)?adjacent:null; }
    private async Task<(string Ownership,bool Locked)> ReadOwnershipAsync(long movieId,string path,CancellationToken token) { await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadOnly,token);await using SqliteCommand x=c.CreateCommand();x.CommandText="SELECT Ownership,IsLocked FROM NfoDocuments WHERE MovieId=$movie AND FilePath=$path ORDER BY Id DESC LIMIT 1";x.Parameters.AddWithValue("$movie",movieId);x.Parameters.AddWithValue("$path",path);await using SqliteDataReader r=await x.ExecuteReaderAsync(token);return await r.ReadAsync(token)?(r.GetString(0),r.GetInt64(1)==1):(File.Exists(path)?"User":"LMM",File.Exists(path)); }
    private async Task RegisterExistingUserDocumentAsync(long movieId,string path,CancellationToken token) { await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite,token);await using var tx=await c.BeginTransactionAsync(token);await UpsertDocumentAsync(c,tx,movieId,path,"User",true,await HashAsync(path,token),null,false,token);await tx.CommitAsync(token); }
    private static async Task UpsertDocumentAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,long movieId,string path,string ownership,bool locked,string hash,string? provider,bool written,CancellationToken token) { await ExecuteAsync(c,tx,"""INSERT INTO NfoDocuments(MovieId,FilePath,Ownership,IsLocked,FileHash,EncodingName,SourceProvider,LastReadAt,LastWrittenAt,CreatedAt,UpdatedAt) VALUES($movie,$path,$owner,$locked,$hash,'utf-8',$provider,$read,$written,$at,$at) ON CONFLICT(MovieId,FilePath) DO UPDATE SET Ownership=excluded.Ownership,IsLocked=excluded.IsLocked,FileHash=excluded.FileHash,SourceProvider=COALESCE(excluded.SourceProvider,NfoDocuments.SourceProvider),LastReadAt=COALESCE(excluded.LastReadAt,NfoDocuments.LastReadAt),LastWrittenAt=COALESCE(excluded.LastWrittenAt,NfoDocuments.LastWrittenAt),UpdatedAt=excluded.UpdatedAt""",("$movie",movieId),("$path",path),("$owner",ownership),("$locked",locked?1:0),("$hash",hash),("$provider",provider),("$read",written?null:Now()),("$written",written?Now():null),("$at",Now())); }
    private static async Task AddRelationAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,long movieId,string table,string relation,string key,string value,CancellationToken token) { string clean=value.Trim();if(clean.Length==0)return;string normalized=Normalize(clean);long id=await ScalarLongAsync(c,tx,$"SELECT COALESCE(MAX(Id),0) FROM {table} WHERE NormalizedName=$name",token,("$name",normalized));if(id==0){(string columns,string values)=table switch{"Actors"=>("Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt","$name,$normalized,'NFO',$at,$at"),"Tags"=>("Name,NormalizedName,Source,CreatedAt,UpdatedAt","$name,$normalized,'NFO',$at,$at"),_=>("Name,NormalizedName","$name,$normalized")};id=await InsertIdAsync(c,tx,$"INSERT INTO {table}({columns}) VALUES({values});SELECT last_insert_rowid();",token,("$name",clean),("$normalized",normalized),("$at",Now()));}string extra=relation switch{"MovieActors"=>",RoleName,SortOrder","MovieTags"=>",CreatedAt","MovieSeries"=>",SortOrder","MovieStudios"=>",RelationType",_=>""};string extraValues=relation switch{"MovieActors"=>",'',999","MovieTags"=>",$at","MovieSeries"=>",999","MovieStudios"=>",'Studio'",_=>""};await ExecuteAsync(c,tx,$"INSERT OR IGNORE INTO {relation}(MovieId,{key}{extra}) VALUES($movie,$id{extraValues})",("$movie",movieId),("$id",id),("$at",Now())); }
    private static async Task<IReadOnlyList<string>> RelationsAsync(SqliteConnection c,long movieId,string table,string relation,string key,CancellationToken token) { await using SqliteCommand x=c.CreateCommand();x.CommandText=$"SELECT e.Name FROM {table} e JOIN {relation} r ON r.{key}=e.Id WHERE r.MovieId=$movie ORDER BY e.Name";x.Parameters.AddWithValue("$movie",movieId);var values=new List<string>();await using SqliteDataReader r=await x.ExecuteReaderAsync(token);while(await r.ReadAsync(token))values.Add(r.GetString(0));return values; }
    private static async Task<string?> FirstRelationAsync(SqliteConnection c,long movieId,string table,string relation,string key,CancellationToken token) { try{return (await RelationsAsync(c,movieId,table,relation,key,token)).FirstOrDefault();}catch(SqliteException){return null;} }
    private static XElement ActorElement(string name, IReadOnlyDictionary<string, string> imagePaths) {
        var actor = new XElement("actor", new XElement("name", name), new XElement("type", "Actor"));
        if (imagePaths.TryGetValue(name, out string? path) && !string.IsNullOrWhiteSpace(path)) actor.Add(new XElement("thumb", path));
        return actor;
    }
    private static async Task<string?> ImagePathAsync(SqliteConnection c,long movieId,string type,CancellationToken token) { await using SqliteCommand x=c.CreateCommand();x.CommandText="SELECT FilePath FROM Images WHERE MovieId=$movie AND ImageType=$type AND trim(ifnull(FilePath,''))<>'' ORDER BY IsPrimary DESC,Id LIMIT 1";x.Parameters.AddWithValue("$movie",movieId);x.Parameters.AddWithValue("$type",type);return(await x.ExecuteScalarAsync(token))?.ToString(); }
    private static async Task<IReadOnlyDictionary<string,string>> ActorImagePathsAsync(SqliteConnection c,long movieId,CancellationToken token) { await using SqliteCommand x=c.CreateCommand();x.CommandText="SELECT a.Name,i.FilePath FROM MovieActors ma JOIN Actors a ON a.Id=ma.ActorId JOIN Images i ON i.ActorId=a.Id WHERE ma.MovieId=$movie AND i.ImageType='ActorAvatar' AND trim(ifnull(i.FilePath,''))<>'' ORDER BY i.IsPrimary DESC,i.Id";x.Parameters.AddWithValue("$movie",movieId);var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);await using SqliteDataReader r=await x.ExecuteReaderAsync(token);while(await r.ReadAsync(token))values.TryAdd(r.GetString(0),r.GetString(1));return values; }
    private static async Task<string?> SettingAsync(SqliteConnection c,string key,CancellationToken token){string? raw=await ScalarTextAsync(c,"SELECT ValueJson FROM AppSettings WHERE Key=$key",token,("$key",key));if(string.IsNullOrWhiteSpace(raw))return raw;try{return JsonSerializer.Deserialize<string>(raw)??raw;}catch(JsonException){return raw;}}
    private static async Task StoreSettingAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string key,object value,string type){await ExecuteAsync(c,tx,"INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,$type,$at) ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt",("$key",key),("$value",JsonSerializer.Serialize(value)),("$type",type),("$at",Now()));}
    private static string TextSetting(IReadOnlyDictionary<string,string> values,string key,string fallback){if(!values.TryGetValue(key,out string? raw))return fallback;try{return JsonSerializer.Deserialize<string>(raw)??fallback;}catch(JsonException){return fallback;}}
    private static bool BoolSetting(IReadOnlyDictionary<string,string> values,string key,bool fallback){if(!values.TryGetValue(key,out string? raw))return fallback;try{return JsonSerializer.Deserialize<bool>(raw);}catch(JsonException){return fallback;}}
    private static void Compare(string label,string? current,string? incoming,List<string> changes,List<string> conflicts){if(string.IsNullOrWhiteSpace(incoming))return;if(string.IsNullOrWhiteSpace(current))changes.Add(label);else if(!string.Equals(current.Trim(),incoming.Trim(),StringComparison.OrdinalIgnoreCase))conflicts.Add(label);}
    private static XElement? Element(string name,string? value)=>string.IsNullOrWhiteSpace(value)?null:new XElement(name,value);
    private static object Empty(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
    private static string Normalize(string value)=>string.Join(' ',value.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string SafeName(string value)=>string.Concat(value.Select(character=>Path.GetInvalidFileNameChars().Contains(character)?'_':character));
    private static string Token(string operation,long movieId,string path,string? hash,string data)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"nfo|{operation}|{movieId}|{Path.GetFullPath(path)}|{hash}|{data}"))).ToLowerInvariant();
    private static void Verify(string expected,string supplied){if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(supplied??"")))throw new UnauthorizedAccessException("NFO 内容或目标已变化，请重新预览后确认。");}
    private static async Task<string> HashAsync(string path,CancellationToken token){await using FileStream stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).ToLowerInvariant();}
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode,CancellationToken token){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=mode,Cache=SqliteCacheMode.Private}.ToString());await c.OpenAsync(token);return c;}
    private static async Task ExecuteAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> InsertIdAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync(token));}
    private static async Task<long> ScalarLongAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync(token)??0L);}
    private static async Task<string?> ScalarTextAsync(SqliteConnection c,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return(await x.ExecuteScalarAsync(token))?.ToString();}
    private static string? Text(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetString(index);
    private static string Now()=>DateTimeOffset.UtcNow.ToString("O");
    private sealed record DatabaseMovie(string? Code,string? Title,string? OriginalTitle,string? Description,string? ReleaseDate,int DurationSeconds,double? ProviderRating);
}
