## 1.0.1

### Added
* Add disposable external setting registration for owners without a BaseUnityPlugin config. Owners retain value storage, validation, and persistence, with safe registration replacement and callback failure isolation.
* Support live read-only external settings through null setters, with editing and Reset disabled.
* Add inline external number editing with Enter/focus-loss commit and Esc cancellation. Retain rejected input and reflect owner reloads when not editing.
* Support external string editing, enum choices, and grouped flags while preserving dynamic descriptions and nullable values.

### Fixed
* Preserve null Reset values, case-only string edits, and pending text/list editor changes.
* Refresh cached file trees after changes, including trash operations.
* Preserve exclusive file-save conflict checks and add staged writes with backup recovery on write failures.
* Clean up owned menu buttons and listeners on disable, restoring menu navigation and cursor state.

### Maintenance
* Simplify metadata copying and reuse editor, enum, and flag metadata caches. Refresh external setting lists only when registrations change.
* Add managed regression coverage for external settings, editing, file conflicts, rollback, and cache invalidation; run it before release packaging.
* Add automatic local plugin deployment for Debug builds with DeployToGame=true.

## 1.0.0
* Initial release
