PRAGMA foreign_keys = ON;

CREATE TABLE NfoDocuments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    FilePath TEXT NOT NULL,
    Ownership TEXT NOT NULL DEFAULT 'User',
    IsLocked INTEGER NOT NULL DEFAULT 1 CHECK (IsLocked IN (0,1)),
    FileHash TEXT,
    EncodingName TEXT NOT NULL DEFAULT 'utf-8',
    SourceProvider TEXT,
    LastReadAt TEXT,
    LastWrittenAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(MovieId, FilePath)
);

CREATE INDEX IX_NfoDocuments_Movie_Ownership
    ON NfoDocuments(MovieId, Ownership, IsLocked);

INSERT OR IGNORE INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('nfo.export.policy','"SkipExisting"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('nfo.export.outputDirectory','""','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('nfo.import.fillEmptyOnly','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('nfo.export.includeImages','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now'));

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '7')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
