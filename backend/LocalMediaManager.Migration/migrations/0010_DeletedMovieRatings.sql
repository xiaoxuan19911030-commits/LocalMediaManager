CREATE TABLE IF NOT EXISTS DeletedMovieRatings (
    NormalizedMovieCode TEXT PRIMARY KEY,
    Rating REAL NOT NULL CHECK (Rating >= 0 AND Rating <= 5),
    UpdatedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_DeletedMovieRatings_NormalizedMovieCode
    ON DeletedMovieRatings(NormalizedMovieCode);

INSERT INTO DeletedMovieRatings(NormalizedMovieCode, Rating, UpdatedAt)
SELECT NormalizedMovieCode, Rating, UpdatedAt
FROM (
    SELECT
        upper(substr(clean, 1, hyphen - 1)) || '-' || substr(clean, hyphen + 1) AS NormalizedMovieCode,
        UserRating AS Rating,
        UpdatedAt,
        row_number() OVER (
            PARTITION BY upper(substr(clean, 1, hyphen - 1)) || '-' || substr(clean, hyphen + 1)
            ORDER BY UpdatedAt DESC, MovieId DESC
        ) AS rn
    FROM (
        SELECT
            s.MovieId,
            s.UserRating,
            s.UpdatedAt,
            instr(replace(replace(upper(trim(m.Code)), '_', '-'), ' ', '-'), '-') AS hyphen,
            replace(replace(upper(trim(m.Code)), '_', '-'), ' ', '-') AS clean
        FROM UserMovieState s
        JOIN Movies m ON m.Id = s.MovieId
        WHERE COALESCE(s.HasUserRating, CASE WHEN s.UserRating > 0 THEN 1 ELSE 0 END) = 1
          AND s.UserRating > 0
          AND trim(COALESCE(m.Code, '')) <> ''
    )
    WHERE hyphen > 1
      AND substr(clean, 1, hyphen - 1) GLOB '[A-Z][A-Z]*'
      AND substr(clean, hyphen + 1) GLOB '[0-9]*'
)
WHERE rn = 1
  AND NormalizedMovieCode IS NOT NULL
  AND NormalizedMovieCode NOT LIKE '%-'
ON CONFLICT(NormalizedMovieCode) DO UPDATE SET
    Rating = excluded.Rating,
    UpdatedAt = excluded.UpdatedAt
WHERE excluded.UpdatedAt >= DeletedMovieRatings.UpdatedAt;

INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
('ratingHistory.enabled','true','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now')),
('ratingHistory.deleteOnClear','false','boolean',strftime('%Y-%m-%dT%H:%M:%fZ','now'))
ON CONFLICT(Key) DO NOTHING;

INSERT INTO DatabaseMetadata(Key, Value)
VALUES('SchemaVersion', '10')
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
