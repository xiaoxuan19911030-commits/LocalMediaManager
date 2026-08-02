CREATE TABLE GeneratedCoverInfo (
    MovieId INTEGER PRIMARY KEY REFERENCES Movies(Id) ON DELETE CASCADE,
    SourceVideoPath TEXT NOT NULL,
    SourceVideoSize INTEGER NOT NULL DEFAULT 0,
    SourceVideoModifiedTime TEXT NOT NULL,
    GeneratedCoverPath TEXT NOT NULL,
    FramePosition REAL NOT NULL,
    Score REAL NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE INDEX IX_GeneratedCoverInfo_SourceVideo ON GeneratedCoverInfo(SourceVideoPath);

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '17')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
