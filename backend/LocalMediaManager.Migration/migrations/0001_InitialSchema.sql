PRAGMA foreign_keys = ON;

CREATE TABLE SchemaMigrations (
    Version INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    AppliedAt TEXT NOT NULL,
    Checksum TEXT NOT NULL
);

CREATE TABLE DatabaseMetadata (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE Libraries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0,1)),
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE LibraryFolders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LibraryId INTEGER NOT NULL REFERENCES Libraries(Id) ON DELETE CASCADE,
    FolderPath TEXT NOT NULL,
    NormalizedPath TEXT NOT NULL COLLATE NOCASE,
    IncludeSubfolders INTEGER NOT NULL DEFAULT 1 CHECK (IncludeSubfolders IN (0,1)),
    IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0,1)),
    ScanMode TEXT NOT NULL DEFAULT 'normal',
    LastScannedAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(NormalizedPath)
);

CREATE TABLE Movies (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT,
    Title TEXT,
    OriginalTitle TEXT,
    SortTitle TEXT,
    ReleaseDate TEXT,
    DurationSeconds INTEGER NOT NULL DEFAULT 0 CHECK (DurationSeconds >= 0),
    Description TEXT,
    ProviderRating REAL,
    IsScraped INTEGER NOT NULL DEFAULT 0 CHECK (IsScraped IN (0,1)),
    ScrapeStatus TEXT NOT NULL DEFAULT 'unknown',
    NfoPath TEXT,
    LegacySource TEXT NOT NULL DEFAULT 'Jvedio5',
    LegacyId INTEGER,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    ImportedAt TEXT,
    UNIQUE(LegacySource, LegacyId)
);

CREATE TABLE MediaFiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    LibraryId INTEGER REFERENCES Libraries(Id) ON DELETE SET NULL,
    FilePath TEXT NOT NULL,
    NormalizedPath TEXT NOT NULL COLLATE NOCASE,
    FileName TEXT NOT NULL,
    Extension TEXT,
    FileSize INTEGER NOT NULL DEFAULT 0 CHECK (FileSize >= 0),
    MediaType TEXT NOT NULL DEFAULT 'Video',
    SourceType TEXT NOT NULL DEFAULT 'Unknown',
    IsPrimary INTEGER NOT NULL DEFAULT 1 CHECK (IsPrimary IN (0,1)),
    ExistsState TEXT NOT NULL DEFAULT 'Unknown',
    DurationSeconds INTEGER NOT NULL DEFAULT 0 CHECK (DurationSeconds >= 0),
    ResolutionWidth INTEGER,
    ResolutionHeight INTEGER,
    VideoCodec TEXT,
    AudioCodec TEXT,
    Bitrate INTEGER,
    FileHash TEXT,
    LastSeenAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(MovieId, NormalizedPath)
);

CREATE TABLE Actors (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL COLLATE NOCASE,
    SortName TEXT,
    Alias TEXT,
    Gender INTEGER,
    BirthDate TEXT,
    Description TEXT,
    ExternalId TEXT,
    LegacySource TEXT NOT NULL DEFAULT 'Jvedio5',
    LegacyId INTEGER,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(LegacySource, LegacyId)
);

CREATE TABLE MovieActors (
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    ActorId INTEGER NOT NULL REFERENCES Actors(Id) ON DELETE CASCADE,
    RoleName TEXT NOT NULL DEFAULT '',
    SortOrder INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(MovieId, ActorId, RoleName)
);

CREATE TABLE Tags (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Description TEXT,
    Color TEXT,
    Source TEXT NOT NULL DEFAULT 'User',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE MovieTags (
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    TagId INTEGER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY(MovieId, TagId)
);

CREATE TABLE Genres (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL COLLATE NOCASE UNIQUE
);

CREATE TABLE MovieGenres (
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    GenreId INTEGER NOT NULL REFERENCES Genres(Id) ON DELETE CASCADE,
    PRIMARY KEY(MovieId, GenreId)
);

CREATE TABLE Studios (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Description TEXT
);

CREATE TABLE MovieStudios (
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    StudioId INTEGER NOT NULL REFERENCES Studios(Id) ON DELETE CASCADE,
    RelationType TEXT NOT NULL DEFAULT 'Studio',
    PRIMARY KEY(MovieId, StudioId, RelationType)
);

CREATE TABLE Series (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Description TEXT,
    ExternalId TEXT
);

CREATE TABLE MovieSeries (
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    SeriesId INTEGER NOT NULL REFERENCES Series(Id) ON DELETE CASCADE,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(MovieId, SeriesId)
);

CREATE TABLE Images (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MovieId INTEGER REFERENCES Movies(Id) ON DELETE CASCADE,
    ActorId INTEGER REFERENCES Actors(Id) ON DELETE CASCADE,
    ImageType TEXT NOT NULL,
    FilePath TEXT,
    SourceUrl TEXT,
    Width INTEGER,
    Height INTEGER,
    FileSize INTEGER,
    FileHash TEXT,
    IsPrimary INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimary IN (0,1)),
    SourceProvider TEXT,
    DownloadedAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CHECK ((MovieId IS NOT NULL AND ActorId IS NULL) OR (MovieId IS NULL AND ActorId IS NOT NULL)),
    UNIQUE(MovieId, ActorId, ImageType, FilePath)
);

CREATE TABLE UserMovieState (
    MovieId INTEGER PRIMARY KEY REFERENCES Movies(Id) ON DELETE CASCADE,
    IsFavorite INTEGER NOT NULL DEFAULT 0 CHECK (IsFavorite IN (0,1)),
    UserRating REAL NOT NULL DEFAULT 0 CHECK (UserRating >= 0 AND UserRating <= 5),
    PlayCount INTEGER NOT NULL DEFAULT 0 CHECK (PlayCount >= 0),
    LastPlayedAt TEXT,
    LastPositionSeconds INTEGER NOT NULL DEFAULT 0 CHECK (LastPositionSeconds >= 0),
    Notes TEXT,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE PlayHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MovieId INTEGER REFERENCES Movies(Id) ON DELETE SET NULL,
    MediaFileId INTEGER REFERENCES MediaFiles(Id) ON DELETE SET NULL,
    StartedAt TEXT NOT NULL,
    EndedAt TEXT,
    PositionSeconds INTEGER NOT NULL DEFAULT 0,
    DurationSeconds INTEGER NOT NULL DEFAULT 0,
    Completed INTEGER NOT NULL DEFAULT 0 CHECK (Completed IN (0,1)),
    PlayerName TEXT,
    LegacyId INTEGER
);

CREATE TABLE ExternalIds (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NOT NULL,
    Provider TEXT NOT NULL,
    ExternalId TEXT NOT NULL,
    UNIQUE(EntityType, EntityId, Provider, ExternalId)
);

CREATE TABLE LegacyIdMappings (
    EntityType TEXT NOT NULL,
    LegacySource TEXT NOT NULL,
    LegacyId TEXT NOT NULL,
    NewId INTEGER NOT NULL,
    PRIMARY KEY(EntityType, LegacySource, LegacyId)
);

CREATE TABLE MigrationWarnings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT,
    LegacyId TEXT,
    WarningCode TEXT NOT NULL,
    Message TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE Tasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskType TEXT NOT NULL,
    Status TEXT NOT NULL,
    Progress REAL NOT NULL DEFAULT 0,
    TotalItems INTEGER NOT NULL DEFAULT 0,
    CompletedItems INTEGER NOT NULL DEFAULT 0,
    PayloadJson TEXT,
    ResultJson TEXT,
    ErrorMessage TEXT,
    CreatedAt TEXT NOT NULL,
    StartedAt TEXT,
    CompletedAt TEXT
);

CREATE INDEX IX_Movies_Code ON Movies(Code COLLATE NOCASE);
CREATE INDEX IX_Movies_Title ON Movies(Title COLLATE NOCASE);
CREATE INDEX IX_Movies_ReleaseDate ON Movies(ReleaseDate);
CREATE INDEX IX_Movies_UpdatedAt ON Movies(UpdatedAt);
CREATE INDEX IX_MediaFiles_MovieId ON MediaFiles(MovieId);
CREATE INDEX IX_MediaFiles_NormalizedPath ON MediaFiles(NormalizedPath);
CREATE INDEX IX_MediaFiles_LibraryId ON MediaFiles(LibraryId);
CREATE INDEX IX_Actors_NormalizedName ON Actors(NormalizedName);
CREATE INDEX IX_MovieActors_ActorId ON MovieActors(ActorId);
CREATE INDEX IX_MovieTags_TagId ON MovieTags(TagId);
CREATE INDEX IX_PlayHistory_MovieId_StartedAt ON PlayHistory(MovieId, StartedAt DESC);
CREATE INDEX IX_ExternalIds_Provider_ExternalId ON ExternalIds(Provider, ExternalId);
