CREATE TABLE MovieCoverCropSettings (
    MovieId INTEGER PRIMARY KEY REFERENCES Movies(Id) ON DELETE CASCADE,
    CoverCropMode TEXT NOT NULL DEFAULT 'AutoFace'
        CHECK (CoverCropMode IN ('AutoFace', 'Left', 'Center', 'Right')),
    CoverFocusX REAL,
    CoverFocusY REAL,
    CoverVersion TEXT,
    UpdatedAt TEXT NOT NULL
);
