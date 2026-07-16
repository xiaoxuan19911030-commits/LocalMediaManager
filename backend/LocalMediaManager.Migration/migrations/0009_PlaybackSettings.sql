PRAGMA foreign_keys = ON;

INSERT OR IGNORE INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('playback.playerPath','""','string',strftime('%Y-%m-%dT%H:%M:%fZ','now'));

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '9')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
