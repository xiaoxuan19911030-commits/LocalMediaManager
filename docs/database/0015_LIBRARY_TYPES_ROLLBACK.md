# Migration 0015 rollback

Migration `0015_LibraryTypesAndLocalMedia.sql` is forward-only in the production migration ledger. A rollback must start from the automatic pre-upgrade database backup.

For a disposable verification database, the equivalent schema rollback is a SQLite table rebuild that removes:

- `Libraries.LibraryType` and `IX_Libraries_LibraryType`
- `Movies.ScreenshotStatus`
- `Movies.CoverSource`

The supported production rollback is restoring the pre-migration database backup together with the matching application build. Released migration files and their checksums must never be edited or removed.
