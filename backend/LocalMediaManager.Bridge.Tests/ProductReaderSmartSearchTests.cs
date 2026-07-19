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
        await Execute(connection, "INSERT INTO Directors(Id,Name,NormalizedName) VALUES(1,'导演A','导演a'),(2,'导演B','导演b')");
        await Execute(connection, "INSERT INTO MovieDirectors(MovieId,DirectorId) VALUES(1,1),(2,2)");
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'长发','长发','Scraper',$at,$at),(2,'收藏候选','收藏候选','User',$at,$at),(3,'legacy stamp','legacy stamp','LegacyStamp',$at,$at),(4,'space tag + alpha','space tag + alpha','NFO',$at,$at),(5,'已收藏','已收藏','LegacyLabel',$at,$at),(6,'新加入','新加入','LegacyStamp',$at,$at)", ("$at", At));
        await Execute(connection, "INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at),(1,2,$at),(2,3,$at),(1,4,$at),(1,5,$at),(2,6,$at)", ("$at", At));
        await Execute(connection, "INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'办公室','办公室')");
        await Execute(connection, "INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");
        await Execute(connection, "INSERT INTO Studios(Id,Name,NormalizedName) VALUES(1,'S1','s1'),(2,'','empty-studio')");
        await Execute(connection, "INSERT INTO MovieStudios(MovieId,StudioId,RelationType) VALUES(1,1,'Studio'),(1,1,'Publisher'),(2,2,'Studio')");
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
        EntityPageDto genres = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "genres", "", "count", 24, 0);
        EntityPageDto studios = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "studios", "", "count", 24, 0);

        Assert.Equal(1, directors.Items.Single(item => item.Name == "导演A").MovieCount);
        Assert.Equal(1, series.Items.Single(item => item.Name == "SONE").MovieCount);
        Assert.Equal(1, movieTags.Items.Single(item => item.Name == "长发").MovieCount);
        Assert.Equal(1, genres.Items.Single(item => item.Name == "办公室").MovieCount);
        Assert.Equal(1, studios.Items.Single(item => item.Name == "S1").MovieCount);
    }

    [Fact]
    public async Task StudioListCanBeScopedToLibraryAndSorted()
    {
        EntityPageDto scoped = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "studios", "", "count", 24, 0, libraryId: 1);
        EntityPageDto archived = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "studios", "", "count", 24, 0, libraryId: 2);
        EntityPageDto ascending = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "studios", "", "count-asc", 24, 0);

        Assert.Contains(scoped.Items, item => item.Name == "S1" && item.MovieCount == 1);
        Assert.DoesNotContain(archived.Items, item => item.Name == "S1");
        Assert.True(ascending.Items.First().MovieCount <= ascending.Items.Last().MovieCount);
    }

    [Fact]
    public async Task DirectorListCanBeSearchedScopedToLibraryAndSorted()
    {
        EntityPageDto searched = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "directors", "导演A", "count", 24, 0);
        EntityPageDto scoped = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "directors", "", "count", 24, 0, libraryId: 1);
        EntityPageDto archived = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "directors", "", "count", 24, 0, libraryId: 2);
        EntityPageDto descending = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "directors", "", "name-desc", 24, 0);

        Assert.Equal(["导演A"], searched.Items.Select(item => item.Name).ToArray());
        Assert.Contains(scoped.Items, item => item.Name == "导演A" && item.MovieCount == 1);
        Assert.Contains(scoped.Items, item => item.Name == "导演B" && item.MovieCount == 1);
        Assert.DoesNotContain(archived.Items, item => item.Name is "导演A" or "导演B");
        Assert.Equal(["导演B", "导演A"], descending.Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task TagCategoriesDoNotFilterHistoricalTagsBySource()
    {
        EntityPageDto movieTags = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "movie-tags", "", "name", 24, 0);
        EntityPageDto customTags = await ProductReader.ReadEntitiesPageAsync(Database, "http://localhost", "tags", "", "name", 24, 0);

        Assert.Contains(movieTags.Items, item => item.Name == "长发");
        Assert.Contains(movieTags.Items, item => item.Name == "收藏候选");
        Assert.Contains(movieTags.Items, item => item.Name == "legacy stamp");
        Assert.Contains(movieTags.Items, item => item.Name == "space tag + alpha");
        Assert.DoesNotContain(movieTags.Items, item => item.Name == "已收藏");
        Assert.Contains(customTags.Items, item => item.Name == "收藏候选");
        Assert.Contains(customTags.Items, item => item.Name == "legacy stamp");
        Assert.Contains(customTags.Items, item => item.Name == "长发");
        Assert.Contains(customTags.Items, item => item.Name == "space tag + alpha");
        Assert.DoesNotContain(customTags.Items, item => item.Name == "新加入");
        Assert.DoesNotContain(customTags.Items, item => item.Name == "已收藏");
    }

    [Fact]
    public async Task CategoryFiltersComposeWithSearchAndFilterBar()
    {
        MediaPageDto director = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "评分>=4", null, null, 1, null, null, null, true, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto directorWithFilterBar = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "", null, null, 1, null, null, null, null, null, 0, "5", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto series = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "标签:长发", null, null, null, null, null, 1, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto movieTag = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "自定义标签:收藏候选", null, null, null, 1, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto genre = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "评分>=4", null, null, null, null, null, null, true, null, 0, "all", "all", "all", "all", null, "newest", 24, 0, genreId: 1);
        MediaPageDto studio = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "评分>=4", null, null, null, null, null, null, true, null, 0, "all", "all", "all", "all", null, "newest", 24, 0, studioId: 1);
        MediaPageDto studioWithFilterBar = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "", null, null, null, null, null, null, null, null, 0, "5", "all", "all", "all", 1, "newest", 24, 0, studioId: 1);

        Assert.Equal([1], director.Items.Select(item => item.DataId).ToArray());
        Assert.Empty(directorWithFilterBar.Items);
        Assert.Equal([1], series.Items.Select(item => item.DataId).ToArray());
        Assert.Equal([1], movieTag.Items.Select(item => item.DataId).ToArray());
        Assert.Equal([1], genre.Items.Select(item => item.DataId).ToArray());
        Assert.Equal([1], studio.Items.Select(item => item.DataId).ToArray());
        Assert.Empty(studioWithFilterBar.Items);
    }

    [Fact]
    public async Task CategoryFiltersHandleSpecialCharacters()
    {
        MediaPageDto result = await ProductReader.AdvancedSearchAsync(Database, "http://localhost",
            "标签:space tag + alpha", null, null, null, null, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);

        Assert.Equal([1], result.Items.Select(item => item.DataId).ToArray());
    }

    [Fact]
    public async Task DuplicateResultsExposeOrganizerDisplayFieldsAndKeepReasons()
    {
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        await Execute(connection, "UPDATE Movies SET Code='DUP-001' WHERE Id IN (1,2)");
        await Execute(connection, "UPDATE MediaFiles SET FileSize=4096,ResolutionWidth=1920,ResolutionHeight=1080 WHERE Id=1");
        await Execute(connection, "UPDATE MediaFiles SET FileSize=1024,ResolutionWidth=1280,ResolutionHeight=720 WHERE Id=2");

        DuplicateResultsDto result = await ProductReader.ReadDuplicateResultsAsync(Database, "code", 10);

        DuplicateGroupDto group = Assert.Single(result.Groups);
        Assert.Equal("code", group.Rule);
        Assert.Equal(2, group.Count);
        DuplicateMovieDto movie = Assert.Single(group.Items, item => item.MovieId == 1);
        Assert.Equal("SONE-104.mp4", movie.FileName);
        Assert.Equal(4096, movie.FileSize);
        Assert.Equal(1920, movie.ResolutionWidth);
        Assert.Equal(1080, movie.ResolutionHeight);
        Assert.True(movie.Favorite);
        Assert.True(movie.UserRatingSet);
        Assert.False(string.IsNullOrWhiteSpace(movie.LibraryName));
        Assert.Equal("Local", movie.SourceType);
        Assert.Equal("建议保留", movie.Recommendation);
        Assert.Contains("分辨率最高", movie.RecommendationReasons);
        Assert.Contains("文件最大", movie.RecommendationReasons);
        Assert.Contains("收藏", movie.RecommendationReasons);
        Assert.Contains("已评分", movie.RecommendationReasons);
        Assert.Contains("元数据更完整", movie.RecommendationReasons);
    }

    [Fact]
    public async Task RandomMovieUsesCurrentQueryScope()
    {
        await AssertRandomInSet(await RandomMovie(), [1, 2, 3]);
        await AssertRandomInSet(await RandomMovie(libraryId: 2), [3]);
        await AssertRandomInSet(await RandomMovie(favorite: true), [1, 3]);
        await AssertRandomInSet(await RandomMovie(movieTagId: 1), [1]);
        await AssertRandomInSet(await RandomMovie(seriesId: 1), [1]);
        await AssertRandomInSet(await RandomMovie(studioId: 1), [1]);
        await AssertRandomInSet(await RandomMovie(customTagId: 2), [1]);
        await AssertRandomInSet(await RandomMovie(query: "ABP"), [3]);
        await AssertRandomInSet(await RandomMovie(ratingFilter: "5"), [3]);
        await AssertRandomInSet(await RandomMovie(query: "评分>=4", studioId: 1, favorite: true, libraryId: 1), [1]);
    }

    [Fact]
    public async Task RandomMovieHandlesEmptyAndSingleResultScopes()
    {
        RandomMovieDto empty = await RandomMovie(directorId: 999);
        RandomMovieDto single = await RandomMovie(query: "SONE-105");

        Assert.Null(empty.Item);
        Assert.Equal(0, empty.Total);
        Assert.Equal(2, single.Item?.DataId);
        Assert.Equal(1, single.Total);
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

    private Task<RandomMovieDto> RandomMovie(string query = "", long? directorId = null, long? movieTagId = null, long? customTagId = null, long? seriesId = null,
        bool? favorite = null, string ratingFilter = "all", long? libraryId = null, long? genreId = null, long? studioId = null) =>
        ProductReader.ReadRandomMovieAsync(Database, "http://localhost",
            query, null, null, directorId, movieTagId, customTagId, seriesId, favorite, null, 0, ratingFilter, "all", "all", "all", libraryId, "newest", genreId, studioId);

    private static Task AssertRandomInSet(RandomMovieDto result, long[] expected)
    {
        Assert.NotNull(result.Item);
        Assert.Contains(result.Item!.DataId, expected);
        Assert.Equal(expected.Length, result.Total);
        return Task.CompletedTask;
    }

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
