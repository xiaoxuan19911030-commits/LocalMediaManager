PRAGMA foreign_keys = ON;

ALTER TABLE LibraryFolders
    ADD COLUMN ExcludePatternsJson TEXT NOT NULL DEFAULT '[]';

CREATE TABLE TaskLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
    Level TEXT NOT NULL DEFAULT 'Info',
    Message TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE INDEX IX_TaskLogs_TaskId_CreatedAt
    ON TaskLogs(TaskId, CreatedAt, Id);

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '4')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
