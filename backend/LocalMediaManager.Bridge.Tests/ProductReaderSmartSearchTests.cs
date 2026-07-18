using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ProductReaderSmartSearchTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-smart-search-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }

        await Execute(connection, """
            CREATE TABLE Directors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NormalizedName TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE MovieDirectors (
                MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
                DirectorId INTEGER NOT NULL REFERENCES Directors(Id) ON DELETE CASCADE,
                PRIMARY KEY(MovieId, DirectorId)
            );
            """);
        await Execute(connection, "INSERT INTO Libraries(Id,Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt) VALUES(1,'本地影片',1,0,$at,$at),(2,'归档库',1,1,$at,$at)", ("$at", At));
        await Execute(connection, """
            INSERT INTO Movies(Id,Code,Title,OriginalTitle,ReleaseDate,Description,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt)
            VALUES
            (1,'SONE-104','办公室长发故事','Office Long Hair','2024-05-01','办公室里的长发主题',0,1,'complete','Test',$at,$at,$at),
            (2,'SONE-105','办公室故事','Office Story','2023-01-01','只有办公室',0,1,'complete','Test',$at,$at,$at),
            (3,'ABP-001','其他影片','Other','2022-01-01','其他简介',0,1,'complete','Test',$at,$at,$at)
            """, ("$at", At));
        await Execute(connection, """
            INSERT INTO MediaFiles(Id,MovieId,LibraryId,FilePath,NormalizedPath,FileName,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt)
            VALUES
            (1,1,1,'C:\media\SONE-104.mp4','c:\media\sone-104.mp4','SONE-104.mp4',1,'Video','Local',1,'Present',0,$at,$at),
            (2,2,1,'C:\media\SONE-105.mp4','c:\media\sone-105.mp4','SONE-105.mp4',1,'Video','Local',1,'Present',0,$at,$at),
            (3,3,2,'D:\archive\ABP-001.mp4','d:\archive\abp-001.mp4','ABP-001.mp4',1,'Video','Local',1,'Present',0,$at,$at),
            (4,1,1,'C:\media\SONE-104-copy.mp4','c:\media\sone-104-copy.mp4','SONE-104-copy.mp4',1,'Video','Local',1,'Present',0,$at,$at)
            """, ("$at", At));
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'三上悠亚','三上悠亚','Test',$at,$at),(2,'其他演员','其他演员','Test',$at,$at)", ("$at", At));
        await Execute(connection, "INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,1,'',0),(2,2,'',0)");
        await Execute(connection, "INSERT INTO Directors(Id,Name,NormalizedName) VALUES(1,'导演A','导演a')");
        await Execute(connection, "INSERT INTO MovieDirectors(MovieId,DirectorId) VALUES(1,1)");
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'长发','长发','Scraper',$at,$at),(2,'收藏候选','收藏候选','User',$at,$at),(3,'legacy stamp','legacy stamp','LegacyStamp',$at,$at),(4,'space tag + alpha','space tag + alpha','NFO',$at,$at)", ("$at", At));
        await Execute(connection, "INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at),(1,2,$at),(2,3,$at),(1,4,$at)", ("$at", At));
        await Execute(connection, "INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'办公室','办公室')");
        await Execute(connection, "INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");
        await Execute(connection, "INSERT INTO Studios(Id,Name,NormalizedName) VALUES(1,'S1','s1')");
        await Execute(connection, "INSERT INTO MovieStudios(MovieId,StudioId,RelationType) VALUES(1,1,'Studio')");
        await Execute(connection, "INSERT INTO Series(Id,Name,NormalizedName) VALUES(1,'SONE','sone')");
        await Execute(connection, "INSERT INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES(1,1,0)");
        await Execute(connection, """
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating)
            VALUES(1,1,4.5,0,0,$at,1),(2,0,3,1,0,$at,1),(3,1,5,2,0,$at,1)
            """, ("$at", At));
    }

    [Theory]
    [InlineData("SONE104")]
    [InlineData("演员:三上悠亚")]
    [InlineData("导演:导演A")]
    [InlineData("标签:长发 自定义标签:收藏候选")]
    [InlineData("系列:SONE 厂商:S1")]
    [InlineData("媒体库:本地影片 年份>=2024")]
    public async Task StructuredSearchFindsExpectedMovie(string query)
    {
        MediaPageDto result = await Search(query);

        Assert.Equal([1], result.Items.Select(item => item.DataId).ToArray());
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task PlainKeywordsUseAndAcrossTerms()
    {
        MediaPageDto result = await Search("办公室 长发");

        Assert.Equal([1], result.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task SearchConditionsMergeWithFilterBarConditions()
    {
        MediaPageDto result = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "演员:三上悠亚 评分>=4", null, null, null, null, null, null, true, null, 0, "all", "all", "all", "all", 1, "newest", 24, 0);

        Assert.Equal([1], result.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task EntityListsReturnDistinctMovieCounts()
    {
        EntityPageDto directors = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "directors", "", "count", 24, 0);
        EntityPageDto series = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "series", "", "count", 24, 0);
        EntityPageDto movieTags = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "movie-tags", "", "count", 24, 0);

        Assert.Equal(1, directors.Items.Single(item => item.Name == "导演A").MovieCount);
        Assert.Equal(1, series.Items.Single(item => item.Name == "SONE").MovieCount);
        Assert.Equal(1, movieTags.Items.Single(item => item.Name == "长发").MovieCount);
    }

    [Fact]
    public async Task MovieTagsAndCustomTagsStaySeparated()
    {
        EntityPageDto movieTags = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "movie-tags", "", "name", 24, 0);
        EntityPageDto customTags = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "tags", "", "name", 24, 0);

        Assert.Contains(movieTags.Items, item => item.Name == "长发");
        Assert.DoesNotContain(movieTags.Items, item => item.Name == "收藏候选");
        Assert.Contains(customTags.Items, item => item.Name == "收藏候选");
        Assert.Contains(customTags.Items, item => item.Name == "legacy stamp");
        Assert.DoesNotContain(customTags.Items, item => item.Name == "长发");
    }

    [Fact]
    public async Task CategoryFiltersComposeWithSearchAndFilterBar()
    {
        MediaPageDto director = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "评分>=4", null, null, 1, null, null, null, true, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto series = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "标签:长发", null, null, null, null, null, 1, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto movieTag = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "自定义标签:收藏候选", null, null, null, 1, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);

        Assert.Equal([1], director.Items.Select(item => item.DataId).ToArray());
        Assert.Equal([1], series.Items.Select(item => item.DataId).ToArray());
        Assert.Equal([1], movieTag.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task CategoryFiltersHandleSpecialCharacters()
    {
        MediaPageDto result = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "标签:space tag + alpha", null, null, null, null, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);

        Assert.Equal([1], result.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task FalseBooleanAndRatingComparisonAreApplied()
    {
        MediaPageDto result = await Search("未收藏 评分<4");

        Assert.Equal([2], result.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task InvalidStructuredConditionDoesNotThrow()
    {
        MediaPageDto result = await Search("评分>>4");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    private Task<MediaPageDto> Search(string query) =>
        ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            query, null, null, null, null, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);

    private const string At = "2026-07-18T00:00:00Z";

    private static async Task Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
