ALTER TABLE UserMovieState ADD COLUMN HasUserRating INTEGER NOT NULL DEFAULT 0 CHECK (HasUserRating IN (0,1));

UPDATE UserMovieState
SET HasUserRating = CASE WHEN UserRating > 0 THEN 1 ELSE 0 END;

CREATE TABLE DeletedRatingMemory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FileName TEXT NOT NULL,
    NormalizedFileName TEXT NOT NULL COLLATE NOCASE,
    Rating REAL NOT NULL CHECK (Rating >= 0 AND Rating <= 5),
    RememberedAt TEXT NOT NULL,
    RestoredAt TEXT,
    SourceMovieId INTEGER,
    UNIQUE(NormalizedFileName)
);

CREATE TABLE OperationAudit (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OperationType TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId INTEGER,
    BeforeJson TEXT,
    AfterJson TEXT,
    RollbackToken TEXT,
    CreatedAt TEXT NOT NULL,
    RevertedAt TEXT
);

CREATE INDEX IX_DeletedRatingMemory_NormalizedFileName
    ON DeletedRatingMemory(NormalizedFileName);
CREATE INDEX IX_OperationAudit_CreatedAt
    ON OperationAudit(CreatedAt DESC);

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '3')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;

