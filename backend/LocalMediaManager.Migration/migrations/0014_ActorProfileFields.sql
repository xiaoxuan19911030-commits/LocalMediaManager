ALTER TABLE Actors ADD COLUMN HeightCm INTEGER
    CHECK (HeightCm IS NULL OR (HeightCm >= 100 AND HeightCm <= 250));
ALTER TABLE Actors ADD COLUMN Cup TEXT;
ALTER TABLE Actors ADD COLUMN BirthPlace TEXT;
ALTER TABLE Actors ADD COLUMN ActivityPeriod TEXT;
ALTER TABLE Actors ADD COLUMN ProfileFieldSourcesJson TEXT NOT NULL DEFAULT '{}'
    CHECK (json_valid(ProfileFieldSourcesJson));
