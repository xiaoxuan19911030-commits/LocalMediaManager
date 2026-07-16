PRAGMA foreign_keys = ON;

ALTER TABLE Images ADD COLUMN Ownership TEXT NOT NULL DEFAULT 'Legacy'
    CHECK (Ownership IN ('Legacy','Provider','User','Generated','Cache'));
ALTER TABLE Images ADD COLUMN IsLocked INTEGER NOT NULL DEFAULT 0 CHECK (IsLocked IN (0,1));
ALTER TABLE Images ADD COLUMN IsDerived INTEGER NOT NULL DEFAULT 0 CHECK (IsDerived IN (0,1));
ALTER TABLE Images ADD COLUMN ContentType TEXT;
ALTER TABLE Images ADD COLUMN ValidationStatus TEXT NOT NULL DEFAULT 'Unknown'
    CHECK (ValidationStatus IN ('Unknown','Valid','Missing','Corrupt','Unsupported'));
ALTER TABLE Images ADD COLUMN ValidatedAt TEXT;
ALTER TABLE Images ADD COLUMN LastAccessedAt TEXT;

CREATE INDEX IX_Images_Movie_Type_Primary
    ON Images(MovieId, ImageType, IsPrimary DESC, IsLocked DESC, Id);
CREATE INDEX IX_Images_Actor_Type_Primary
    ON Images(ActorId, ImageType, IsPrimary DESC, IsLocked DESC, Id);
CREATE INDEX IX_Images_Ownership_Derived
    ON Images(Ownership, IsDerived, ImageType);

CREATE TABLE ImageCacheEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MovieId INTEGER REFERENCES Movies(Id) ON DELETE CASCADE,
    ActorId INTEGER REFERENCES Actors(Id) ON DELETE CASCADE,
    SourceImagePath TEXT NOT NULL,
    CachePath TEXT NOT NULL UNIQUE,
    CacheKind TEXT NOT NULL,
    Width INTEGER NOT NULL DEFAULT 0,
    Height INTEGER NOT NULL DEFAULT 0,
    FileSize INTEGER NOT NULL DEFAULT 0,
    FileHash TEXT,
    CreatedAt TEXT NOT NULL,
    LastAccessedAt TEXT,
    CHECK ((MovieId IS NOT NULL AND ActorId IS NULL) OR (MovieId IS NULL AND ActorId IS NOT NULL))
);

CREATE INDEX IX_ImageCacheEntries_Movie_Kind ON ImageCacheEntries(MovieId, CacheKind);
CREATE INDEX IX_ImageCacheEntries_Actor_Kind ON ImageCacheEntries(ActorId, CacheKind);

INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('images.cache.enabled','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('images.cache.maxBytes','1073741824','integer',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('images.thumbnail.width','360','integer',strftime('%Y-%m-%dT%H:%M:%fZ','now'))
ON CONFLICT(Key) DO NOTHING;

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '6')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
