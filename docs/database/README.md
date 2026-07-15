# Database documentation

This directory contains the public Database v1 design, legacy-to-v1 field mapping and settings storage strategy.

The migration analyzer can generate a schema dictionary, data-quality findings and a JSON analysis report from a local legacy database. Those generated reports may contain private paths, library sizes, hashes and aggregate user-data statistics, so they are intentionally excluded from the public repository under `docs/database/generated/`.

Never commit a real database, generated migration report, media filename, local user profile path or provider credential.
