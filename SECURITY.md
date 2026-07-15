# Security Policy

## Supported versions

Only the latest published version receives security fixes while the project is pre-1.0.

## Reporting a vulnerability

Please do not open a public issue for vulnerabilities involving local file access, Bridge authentication, database corruption, credential exposure or unsafe file operations. Use GitHub's private security advisory feature for this repository.

Include the affected version, reproduction steps, expected impact and whether user data or files can be modified. Do not include real private media paths, credentials or databases in the report.

## Security boundaries

- The Bridge binds to loopback and mutating endpoints require a per-launch session token.
- React does not access SQLite or the filesystem directly.
- Sensitive provider credentials must not be stored in ordinary settings files.
- Dangerous file operations must be previewed and confirmed and must provide an audit/rollback strategy.

