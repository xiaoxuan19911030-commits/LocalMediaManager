PRAGMA foreign_keys = ON;

ALTER TABLE Tasks ADD COLUMN Stage TEXT;
ALTER TABLE Tasks ADD COLUMN Provider TEXT;
ALTER TABLE Tasks ADD COLUMN RetryCount INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Tasks ADD COLUMN CurrentMovieId INTEGER REFERENCES Movies(Id) ON DELETE SET NULL;
ALTER TABLE Tasks ADD COLUMN ResultSummary TEXT;
ALTER TABLE Tasks ADD COLUMN UpdatedAt TEXT;
ALTER TABLE Tasks ADD COLUMN CancellationRequested INTEGER NOT NULL DEFAULT 0 CHECK (CancellationRequested IN (0,1));

CREATE INDEX IX_Tasks_Type_Status_CreatedAt ON Tasks(TaskType, Status, CreatedAt, Id);
CREATE INDEX IX_Tasks_CurrentMovieId ON Tasks(CurrentMovieId);

CREATE TABLE AppSettings (
    Key TEXT PRIMARY KEY,
    ValueJson TEXT NOT NULL,
    ValueType TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE MetadataSyncSnapshots (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    Provider TEXT NOT NULL,
    BeforeJson TEXT NOT NULL,
    AppliedJson TEXT,
    CreatedAt TEXT NOT NULL,
    AppliedAt TEXT,
    RolledBackAt TEXT
);

CREATE INDEX IX_MetadataSyncSnapshots_TaskId ON MetadataSyncSnapshots(TaskId);
CREATE INDEX IX_MetadataSyncSnapshots_MovieId_CreatedAt ON MetadataSyncSnapshots(MovieId, CreatedAt DESC);

INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('metadata.metatube.enabled','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.baseUrl','"http://127.0.0.1:8080/"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.timeoutSeconds','30','integer',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.downloadImages','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.writeNfo','false','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.autoExecute','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('metadata.metatube.nonDestructive','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now'));

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '5')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
