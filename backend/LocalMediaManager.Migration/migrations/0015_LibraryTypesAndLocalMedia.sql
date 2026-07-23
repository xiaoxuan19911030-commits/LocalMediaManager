ALTER TABLE Libraries ADD COLUMN LibraryType TEXT NOT NULL DEFAULT 'Standard'
    CHECK (LibraryType IN ('Standard', 'Local'));

ALTER TABLE Movies ADD COLUMN ScreenshotStatus TEXT NOT NULL DEFAULT 'None'
    CHECK (ScreenshotStatus IN ('None', 'Pending', 'Processing', 'Completed', 'Failed'));

ALTER TABLE Movies ADD COLUMN CoverSource TEXT NOT NULL DEFAULT 'None'
    CHECK (CoverSource IN ('None', 'Uploaded', 'Screenshot', 'Scraped'));

CREATE INDEX IX_Libraries_LibraryType ON Libraries(LibraryType);
