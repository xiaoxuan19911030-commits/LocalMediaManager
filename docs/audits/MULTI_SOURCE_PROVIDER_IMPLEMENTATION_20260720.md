# Multi-source provider implementation audit — 2026-07-20

## Scope

- Movie metadata: DMM and JavDB, integrated through the existing `IMetadataProvider` and sync pipeline.
- Actor profiles: Minnano and Wikipedia JP, integrated through the existing Actors table and Data Center.
- Existing MetaTube and JavBus implementations remain in place.

## Reference and license review

The current public `spartawhy117/JvedioNext` default branch contains documentation and screenshots but no provider source files and no repository `LICENSE` file. Its README displays an MIT badge, which is not sufficient evidence for copying unidentified source files.

No JvedioNext code, UI assets, database, or user data was copied. Provider request and parsing code in this repository is an original implementation based on publicly observable source behavior and the requirements of this sprint.

## Database migration

- Migration: `0014_ActorProfileFields`
- Columns added in place to `Actors`: `HeightCm`, `Cup`, `BirthPlace`, `ActivityPeriod`, `ProfileFieldSourcesJson`.
- No Actors table rebuild and no Actor ID rewrite.
- `ProfileFieldSourcesJson` stores one object per tracked field with `source`, `updatedAt`, and `userEdited`.
- Checksum enforcement begins at migration 0014. Versions 1–13 predate enforcement; their recorded checksums are preserved and never rewritten.

## Merge policy

1. User-edited fields are protected.
2. Existing non-empty values are not overwritten.
3. Minnano supplies explicit birth date, height, cup, aliases, activity period, and avatar candidate data.
4. Wikipedia JP supplies explicit birth date, height, birthplace, and a bounded introductory summary.
5. Conflicts are returned by the merge service and do not overwrite existing data.
6. Actor ID zero and low-confidence candidates are rejected.

## Provider safety

- Exact normalized movie-code verification is required before a DMM/JavDB detail result is accepted.
- 403, 429, timeout, verification/login pages, and region-unavailable pages are classified separately.
- Requests are sequential with bounded timeout and retry counts.
- Cookies are optional settings values, stored with the existing `secret` setting type and never emitted by provider logs.
- No captcha, age gate, or region restriction bypass is implemented.

## Real database validation

Before migration:

- Movies: 3237
- Actors: 1671
- Actor IDs: 1–1671
- ActorID=0: 0
- Migrations: 13
- Integrity: `ok`
- Foreign key errors: 0
- Actor columns: 13
- Actor indexes: 2

After migration:

- Movies: 3237
- Actors: 1671
- Actor IDs: 1–1671
- ActorID=0: 0
- Migrations: 14
- Integrity: `ok`
- Foreign key errors: 0
- Actor columns: 18
- Actor indexes: 2
- Migration 0014 checksum: `ab449d802d5675dfb972ccbbc36f8dfcc31cb8240d06483a6f2f766740eda15b`

The repeated upgrade check returned no pending migrations.

## Backup and rollback

Successful pre-upgrade backup:

`D:\Local Media Manager Next Data\data\backups\database\upgrade_20260720_210947\LocalMediaManager.db`

Rollback procedure:

1. Stop Local Media Manager and its Bridge process.
2. Preserve the current database as incident evidence.
3. Restore the backup above to `D:\Local Media Manager Next Data\data\LocalMediaManager.db`.
4. Deploy a build from tag `rollback/actor-profile-migration-pre-20260720`.
5. Run Migration `validate`; require integrity `ok`, zero foreign-key errors, 13 migrations, and 1671 actors before starting the application.

## Online smoke

- DMM: HTTP 200 redirected to the official region-unavailable page; parsing is intentionally rejected.
- JavDB code/actor/tag probes: connection timeout on the current network.
- Minnano: connection reset on the current network.
- Wikipedia JP API: connection timeout on the current network.

These results establish current network behavior only. They are not reported as successful metadata parsing.
