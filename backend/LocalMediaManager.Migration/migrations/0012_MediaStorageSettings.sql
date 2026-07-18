PRAGMA foreign_keys = ON;

INSERT OR IGNORE INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('mediaStorage.rootPath','""','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.posters','"Posters"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.thumbnails','"Thumbnails"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.fanart','"Fanart"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.previews','"Previews"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.screenshots','"Screenshots"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.gif','"GIF"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.directory.nfo','"NFO"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.template.movieFolder','"{MovieCode}"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('mediaStorage.template.fileName','"{MovieCode}"','string',strftime('%Y-%m-%dT%H:%M:%fZ','now'));

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '12')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
