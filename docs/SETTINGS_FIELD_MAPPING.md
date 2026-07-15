# Legacy settings field mapping

The authoritative settings source is `D:\Jvedio\Jvedio5.0\data\Administrator\app_configs.sqlite`, table `app_configs`. `ConfigName` identifies a configuration group and `ConfigValue` contains JSON. The Bridge opens this database read-only.

The settings DTO records category, section, UI label, exact `ConfigName.Property`, source, type, current value, default metadata, nullability, restart/immediate flags, danger flag, legacy compatibility, safe-write status, sensitivity, and read status.

Mapped groups include:

- `WindowConfig.Settings`: startup, close behavior, language, player, NFO, images, backups, hotkey, listener, debug.
- `WindowConfig.Main`: details navigation and shared window behavior.
- `ScanConfig`: startup scan, import, NFO and image-copy behavior.
- `ProxyConfig`: proxy mode, protocol, server, port, credentials, timeout.
- `DownloadConfig`: metadata and image synchronization scope.
- `FFmpegConfig`: screenshot/GIF/video-processing behavior.
- `RenameConfig`: existing rename rules and favorite-triggered rename.
- `ThemeConfig` and `VideoConfig`: theme and media-card presentation.
- `PluginConfig`, `Servers`, and `JavaServer`: advanced plugin/server configuration.

Fields not yet assigned a product-facing label are returned under Advanced / Compatibility fields using their exact original key. Sensitive values are replaced with `configured`/`not configured`; they are never returned verbatim.

All fields are read-only in this stage. `SafeToWrite` remains false until backup, validation, atomic save, reread, and rollback tests are implemented.
