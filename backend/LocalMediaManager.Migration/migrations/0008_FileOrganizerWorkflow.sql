PRAGMA foreign_keys = ON;

CREATE TABLE FileOperationJournal (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
    MovieId INTEGER NOT NULL REFERENCES Movies(Id) ON DELETE CASCADE,
    MediaFileId INTEGER NOT NULL REFERENCES MediaFiles(Id) ON DELETE CASCADE,
    SourcePath TEXT NOT NULL,
    DestinationPath TEXT NOT NULL,
    OperationType TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'Planned',
    SourceFingerprint TEXT NOT NULL,
    ErrorMessage TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CompletedAt TEXT
);

CREATE INDEX IX_FileOperationJournal_Task_Status
    ON FileOperationJournal(TaskId, Status, Id);

CREATE INDEX IX_FileOperationJournal_MediaFile
    ON FileOperationJournal(MediaFileId, CreatedAt DESC);

INSERT OR IGNORE INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('organizer.fileNameTemplate','"{Code}"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('organizer.destinationDirectory','""','string',strftime('%Y-%m-%dT%H:%M:%fZ','now'));

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '8')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
