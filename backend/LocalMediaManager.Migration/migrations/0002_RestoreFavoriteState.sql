UPDATE UserMovieState
SET IsFavorite = 1,
    UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE MovieId IN (
    SELECT mt.MovieId
    FROM MovieTags mt
    INNER JOIN Tags t ON t.Id = mt.TagId
    WHERE lower(trim(t.NormalizedName)) IN ('已收藏', '我的收藏')
);

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '2')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;

