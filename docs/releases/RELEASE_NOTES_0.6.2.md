# Local Media Manager 0.6.2

Release date: 2026-07-20

## Features

- MovieWall ratings can now be changed directly from movie cards and list rows.

## UI Improvements

- MovieWall filters open by default and remember the last expanded/collapsed state.
- MovieWall header spacing is reduced so more movie content is visible.
- Edit mode actions are consolidated into the MovieWall toolbar.
- Right-click menus are narrower, denser, icon-free, and easier to scan.
- Batch menu actions are simplified to sync information, screenshot, GIF, rename placeholder, and delete movie.

## Bug Fixes

- Fixed manually queued metadata sync tasks staying pending when provider execution settings were inconsistent.
- Fixed right-click menus staying open when another movie is right-clicked.
- Fixed current-page select toggling to avoid duplicate selected movie IDs.

## Performance

- Random movie uses the existing scoped random query and does not load all movie IDs into the frontend.

## Compatibility

- No database schema changes.
- No migration required.
- Compatible with 0.6.0 and 0.6.1 databases, settings, tasks, plugins, ratings, favorites, custom tags, playback history, and media resources.

## Upgrade Notes

- No rescan required.
- No metadata resync required.
- Existing pending sync tasks can be picked up by the worker after upgrade when the provider is enabled.

## Known Issues

- Details page visual polish from the original request is intentionally deferred because the final instruction was to leave Details unchanged.

## Package

- Local Media Manager_0.6.2_x64-setup.exe
