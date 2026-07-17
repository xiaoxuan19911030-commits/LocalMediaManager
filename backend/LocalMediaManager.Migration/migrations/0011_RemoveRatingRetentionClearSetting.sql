DELETE FROM AppSettings
WHERE Key = 'ratingHistory.deleteOnClear';

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '11')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
