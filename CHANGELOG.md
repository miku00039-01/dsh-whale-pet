# Changelog

All notable changes to **DSH Whale Pet** are documented here.

## [v1.6] - 2026-08-16

### Changed
- **Remove all hardcoded paths**: workspace / node / dsh / PWA shortcut are now configured via `dsh-whale-pet.conf` (INI) with automatic detection on first run; graceful warning when Node.js or DSH is missing.
- **Publish-ready packaging**: added `build.ps1` (icon generation + compile with built-in csc.exe), `README.md` / `README.en.md`, `LICENSE` (MIT), `.gitignore`, GitHub Actions CI (`build on push`, `release on tag`).

## [v1.5] - 2026-08-16

### Added
- Custom exe icon: whale artwork embedded via `/win32icon` (multi-size ICO generated from the transparent PNG).

## [v1.4] - 2026-08-16

### Fixed
- "Close program" now closes the GUI window by **matching the window title** (`DeepSeek Harness ...`). The PWA window is hosted inside the main Chrome process with no `--app-id` in its command line, so the previous process-command-line matching never matched.

## [v1.3] - 2026-08-16

### Added
- "Close program" also closes the DeepSeek Harness PWA window (initial app-id based approach).

## [v1.2] - 2026-08-16

### Changed
- Opening the GUI now launches the **Chrome PWA shortcut** (standalone window, reuses the same window instead of stacking tabs in the browser).

## [v1.1] - 2026-08-16

### Fixed
- Service startup now invokes `node <dsh lib/bin.js> web` directly instead of `npx` (no more npm/npx dialog in automated contexts).
- Added `WS_EX_LAYERED` window style — fixed the pet rendering as a black square (UpdateLayeredWindow had been failing silently).

## [v1.0] - 2026-08-16

### Added
- Initial release: transparent always-on-top whale pet.
- Start / stop / monitor the DSH service on `127.0.0.1:3080`.
- Double-click to summon the GUI; drag to move (position remembered).
- Right-click menu (Open / Close / Status / Exit), tray icon, green/red adaptive status monitoring (5s online / 2s offline).
- Floating status card with service info + pet memory/GDI self-check.
- Single instance (named Mutex); crash log (`dsh-whale-pet-crash.log`).
