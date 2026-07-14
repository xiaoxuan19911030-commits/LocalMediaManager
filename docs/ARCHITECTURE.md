# Architecture validation

## Upstream baseline

The inspected Clash Verge Rev development baseline uses React 19, Material UI 9, Emotion, React Router, Vite 8, Tauri 2.11, and Rust. Its reusable infrastructure is the desktop window lifecycle, theme model, routed layout, side navigation, Material UI component conventions, loading/error feedback, responsive composition, and internationalization boundary.

Proxy-specific frontend pages, Mihomo APIs, proxy profiles, rules, connections, traffic, subscription state, DNS, TUN, system proxy integration, updater behavior tied to the original product, and their Rust crates are excluded rather than renamed.

## Selected Bridge boundary

Phase 1 uses an independent .NET 8 loopback HTTP process.

```text
Tauri 2 / React / Material UI
          |
          | http://127.0.0.1:47831
          v
LocalMediaManager.Bridge (.NET 8)
          |
          | SQLite Mode=ReadOnly
          v
existing app_datas.sqlite + existing media/image paths
```

HTTP was selected over Named Pipes for the first prototype because it provides a debuggable, typed JSON boundary that can be exercised independently from Tauri and does not require duplicating a pipe client in Rust and TypeScript. It binds only to loopback. Authentication and per-session tokens are required before any write endpoint is introduced.

The Bridge currently exposes health, library summary, a bounded video list, cover streaming, and player launch. No database migration or write statement exists in the prototype.

## Existing C# reuse assessment

- Reuse as compatibility authority: SQLite schema/mappers, configuration paths, scanning, imports, metadata/NFO, scraper integration, image/cache rules, tags, actors, favorites, ratings, history, player selection, plugins, and server resources.
- Extract after the boundary stabilizes: pure data contracts, query services, path resolution, image resolution, and player service.
- Keep in WPF until later: controls, dialogs, `BitmapSource` handling, routed events, and other presentation-bound code.
- Do not rewrite in Rust during phase 1: scanning, scraping, database writes, media processing, or configuration persistence.

## Safety

- The existing database is opened with `SqliteOpenMode.ReadOnly`.
- The stable WPF installation is never overwritten by the Next build.
- The default Next deployment root is `D:\Jvedio\LocalMediaManagerNext`.
- Any future schema migration requires a copied database, an explicit migration tool, verification, and rollback.

