using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MediaCardDto(long DataId, string Code, string Title, string Path, double Grade,
    bool Favorite, string ReleaseDate, string ImportedAt, string? CoverUrl);
public sealed record DashboardDto(long MovieCount, long FavoriteCount, long PlayedCount, long MissingFileCount,
    long LibraryCount, long ActiveTaskCount, IReadOnlyList<MediaCardDto> RecentImports, IReadOnlyList<MediaCardDto> RecentPlays);
public sealed record SearchEntityDto(long Id, string Name, long MovieCount);
public sealed record GlobalSearchDto(string Query, IReadOnlyList<MediaCardDto> Movies,
    IReadOnlyList<SearchEntityDto> Actors, IReadOnlyList<SearchEntityDto> Tags);
public sealed record LibraryFolderDto(long Id, string Path, bool Enabled, bool IncludeSubfolders, string ScanMode, string? LastScannedAt);
public sealed record LibraryDto(long Id, string Name, string? Description, bool Enabled, long MovieCount,
    long MissingCount, IReadOnlyList<LibraryFolderDto> Folders);
public sealed record TaskDto(long Id, string Type, string Status, double Progress, long TotalItems,
    long CompletedItems, string? ErrorMessage, string CreatedAt, string? StartedAt, string? CompletedAt);
public sealed record NamedDto(long Id, string Name);
public sealed record MediaFileDto(long Id, string Path, string FileName, string? Extension, long FileSize,
    string SourceType, string ExistsState, bool Primary);
public sealed record MovieDetailDto(long Id, string? Code, string? Title, string? OriginalTitle, string? ReleaseDate,
    long DurationSeconds, string? Description, double ProviderRating, bool Scraped, string ScrapeStatus,
    string? NfoPath, string? ImportedAt, string UpdatedAt, bool Favorite, double UserRating, long PlayCount,
    string? LastPlayedAt, long LastPositionSeconds, string? Notes, string? CoverUrl,
    IReadOnlyList<MediaFileDto> MediaFiles, IReadOnlyList<NamedDto> Actors, IReadOnlyList<NamedDto> Tags,
    IReadOnlyList<NamedDto> Genres, IReadOnlyList<NamedDto> Studios, IReadOnlyList<NamedDto> Series);

public static class ProductReader
{
    public static async Task<DashboardDto> ReadDashboardAsync(string databasePath, string bridgeUrl)
    {
        await using var connection = await OpenAsync(databasePath);
        long movies = await ScalarAsync(connection, "SELECT COUNT(*) FROM Movies");
        long favorites = await ScalarAsync(connection, "SELECT COUNT(*) FROM UserMovieState WHERE IsFavorite=1");
        long played = await ScalarAsync(connection, "SELECT COUNT(*) FROM UserMovieState WHERE PlayCount>0");
        long missing = await ScalarAsync(connection, "SELECT COUNT(DISTINCT MovieId) FROM MediaFiles WHERE ExistsState='Missing'");
        long libraries = await ScalarAsync(connection, "SELECT COUNT(*) FROM Libraries WHERE IsEnabled=1");
        long tasks = await ScalarAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Status IN ('Pending','Running','Paused')");
        var recentImports = await ReadCardsAsync(connection, bridgeUrl, "m.ImportedAt DESC, m.Id DESC", 8, false);
        var recentPlays = await ReadCardsAsync(connection, bridgeUrl, "s.LastPlayedAt DESC, m.Id DESC", 8, true);
        return new(movies, favorites, played, missing, libraries, tasks, recentImports, recentPlays);
    }

    public static async Task<GlobalSearchDto> SearchAsync(string databasePath, string bridgeUrl, string query, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        string like = $"%{EscapeLike(query.Trim())}%";
        var movies = new List<MediaCardDto>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT m.Id,COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(m.Title,''),
                       COALESCE(f.FilePath,''),COALESCE(s.UserRating,0),COALESCE(s.IsFavorite,0),
                       COALESCE(m.ReleaseDate,''),COALESCE(m.ImportedAt,m.CreatedAt,''),
                       EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id)
                  FROM Movies m
                  LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
                  LEFT JOIN UserMovieState s ON s.MovieId=m.Id
                 WHERE m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\' OR m.OriginalTitle LIKE $like ESCAPE '\'
                 ORDER BY CASE WHEN m.Code=$exact THEN 0 WHEN m.Code LIKE $prefix ESCAPE '\' THEN 1 ELSE 2 END,m.Id DESC
                 LIMIT $limit
                """;
            command.Parameters.AddWithValue("$like", like); command.Parameters.AddWithValue("$exact", query.Trim());
            command.Parameters.AddWithValue("$prefix", EscapeLike(query.Trim()) + "%"); command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) movies.Add(Card(reader, bridgeUrl));
        }
        var actors = await ReadEntitiesAsync(connection, "Actors", "MovieActors", "ActorId", like, limit);
        var tags = await ReadEntitiesAsync(connection, "Tags", "MovieTags", "TagId", like, limit);
        return new(query.Trim(), movies, actors, tags);
    }

    public static async Task<IReadOnlyList<LibraryDto>> ReadLibrariesAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);
        var libraries = new List<(long Id,string Name,string? Description,bool Enabled,long Movies,long Missing)>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT l.Id,l.Name,l.Description,l.IsEnabled,
                       COUNT(DISTINCT f.MovieId),COUNT(DISTINCT CASE WHEN f.ExistsState='Missing' THEN f.MovieId END)
                  FROM Libraries l LEFT JOIN MediaFiles f ON f.LibraryId=l.Id
                 GROUP BY l.Id ORDER BY l.SortOrder,l.Name
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) libraries.Add((reader.GetInt64(0),reader.GetString(1),Text(reader,2),reader.GetInt64(3)==1,reader.GetInt64(4),reader.GetInt64(5)));
        }
        var result = new List<LibraryDto>();
        foreach (var library in libraries) {
            var folders = new List<LibraryFolderDto>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,FolderPath,IsEnabled,IncludeSubfolders,ScanMode,LastScannedAt FROM LibraryFolders WHERE LibraryId=$id ORDER BY Id";
            command.Parameters.AddWithValue("$id", library.Id);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) folders.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2)==1,reader.GetInt64(3)==1,reader.GetString(4),Text(reader,5)));
            result.Add(new(library.Id,library.Name,library.Description,library.Enabled,library.Movies,library.Missing,folders));
        }
        return result;
    }

    public static async Task<IReadOnlyList<TaskDto>> ReadTasksAsync(string databasePath, int limit)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,TaskType,Status,Progress,TotalItems,CompletedItems,ErrorMessage,CreatedAt,StartedAt,CompletedAt FROM Tasks ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        var tasks = new List<TaskDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tasks.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetDouble(3),
            reader.GetInt64(4),reader.GetInt64(5),Text(reader,6),reader.GetString(7),Text(reader,8),Text(reader,9)));
        return tasks;
    }

    public static async Task<MovieDetailDto?> ReadMovieAsync(string databasePath, string bridgeUrl, long movieId)
    {
        await using var connection = await OpenAsync(databasePath);
        (long Id,string? Code,string? Title,string? Original,string? Release,long Duration,string? Description,double Provider,bool Scraped,string Status,string? Nfo,string? Imported,string Updated,bool Favorite,double Rating,long Plays,string? LastPlayed,long Position,string? Notes,bool HasCover)? movie = null;
        await using (var command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT m.Id,m.Code,m.Title,m.OriginalTitle,m.ReleaseDate,m.DurationSeconds,m.Description,COALESCE(m.ProviderRating,0),m.IsScraped,m.ScrapeStatus,m.NfoPath,m.ImportedAt,m.UpdatedAt,
                       COALESCE(s.IsFavorite,0),COALESCE(s.UserRating,0),COALESCE(s.PlayCount,0),s.LastPlayedAt,COALESCE(s.LastPositionSeconds,0),s.Notes,
                       EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id)
                  FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id
                """;
            command.Parameters.AddWithValue("$id", movieId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync()) movie=(reader.GetInt64(0),Text(reader,1),Text(reader,2),Text(reader,3),Text(reader,4),reader.GetInt64(5),Text(reader,6),reader.GetDouble(7),reader.GetInt64(8)==1,reader.GetString(9),Text(reader,10),Text(reader,11),reader.GetString(12),reader.GetInt64(13)==1,reader.GetDouble(14),reader.GetInt64(15),Text(reader,16),reader.GetInt64(17),Text(reader,18),reader.GetInt64(19)==1);
        }
        if (movie is null) return null;
        var value = movie.Value;
        var files = new List<MediaFileDto>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText="SELECT Id,FilePath,FileName,Extension,FileSize,SourceType,ExistsState,IsPrimary FROM MediaFiles WHERE MovieId=$id ORDER BY IsPrimary DESC,Id"; command.Parameters.AddWithValue("$id",movieId);
            await using var reader=await command.ExecuteReaderAsync(); while(await reader.ReadAsync()) files.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),Text(reader,3),reader.GetInt64(4),reader.GetString(5),reader.GetString(6),reader.GetInt64(7)==1));
        }
        return new(value.Id,value.Code,value.Title,value.Original,value.Release,value.Duration,value.Description,value.Provider,value.Scraped,value.Status,value.Nfo,value.Imported,value.Updated,value.Favorite,value.Rating,value.Plays,value.LastPlayed,value.Position,value.Notes,value.HasCover?$"{bridgeUrl}/api/images/{movieId}/primary":null,
            files,await ReadNamesAsync(connection,"Actors","MovieActors","ActorId",movieId),await ReadNamesAsync(connection,"Tags","MovieTags","TagId",movieId),
            await ReadNamesAsync(connection,"Genres","MovieGenres","GenreId",movieId),await ReadNamesAsync(connection,"Studios","MovieStudios","StudioId",movieId),await ReadNamesAsync(connection,"Series","MovieSeries","SeriesId",movieId));
    }

    private static async Task<List<MediaCardDto>> ReadCardsAsync(SqliteConnection connection, string bridgeUrl, string orderBy, int limit, bool playedOnly)
    {
        await using var command=connection.CreateCommand();
        command.CommandText=$"""
            SELECT m.Id,COALESCE(NULLIF(m.Code,''),NULLIF(m.Title,''),CAST(m.Id AS TEXT)),COALESCE(m.Title,''),COALESCE(f.FilePath,''),
                   COALESCE(s.UserRating,0),COALESCE(s.IsFavorite,0),COALESCE(m.ReleaseDate,''),COALESCE(m.ImportedAt,m.CreatedAt,''),EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id)
              FROM Movies m LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' LEFT JOIN UserMovieState s ON s.MovieId=m.Id
             WHERE {(playedOnly ? "COALESCE(s.PlayCount,0)>0" : "1=1")} ORDER BY {orderBy} LIMIT $limit
            """; command.Parameters.AddWithValue("$limit",limit);
        var items=new List<MediaCardDto>(); await using var reader=await command.ExecuteReaderAsync(); while(await reader.ReadAsync()) items.Add(Card(reader,bridgeUrl)); return items;
    }
    private static MediaCardDto Card(SqliteDataReader reader,string bridgeUrl)=>new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDouble(4),reader.GetInt64(5)==1,reader.GetString(6),reader.GetString(7),reader.GetInt64(8)==1?$"{bridgeUrl}/api/images/{reader.GetInt64(0)}/primary":null);
    private static async Task<IReadOnlyList<SearchEntityDto>> ReadEntitiesAsync(SqliteConnection connection,string table,string relation,string key,string like,int limit){
        await using var command=connection.CreateCommand();command.CommandText=$"SELECT e.Id,e.Name,COUNT(r.MovieId) FROM {table} e LEFT JOIN {relation} r ON r.{key}=e.Id WHERE e.Name LIKE $like ESCAPE '\\' GROUP BY e.Id ORDER BY COUNT(r.MovieId) DESC,e.Name LIMIT $limit";command.Parameters.AddWithValue("$like",like);command.Parameters.AddWithValue("$limit",limit);
        var result=new List<SearchEntityDto>();await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2)));return result;
    }
    private static async Task<IReadOnlyList<NamedDto>> ReadNamesAsync(SqliteConnection connection,string table,string relation,string key,long movieId){
        await using var command=connection.CreateCommand();command.CommandText=$"SELECT e.Id,e.Name FROM {table} e JOIN {relation} r ON r.{key}=e.Id WHERE r.MovieId=$id ORDER BY e.Name";command.Parameters.AddWithValue("$id",movieId);
        var result=new List<NamedDto>();await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new(reader.GetInt64(0),reader.GetString(1)));return result;
    }
    private static async Task<long> ScalarAsync(SqliteConnection connection,string sql){await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync()??0);}
    private static async Task<SqliteConnection> OpenAsync(string path){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Cache=SqliteCacheMode.Shared}.ToString());await connection.OpenAsync();return connection;}
    private static string EscapeLike(string value)=>value.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
    private static string? Text(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetString(index);
}
