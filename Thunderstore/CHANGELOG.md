## 1.0.3
* README change.
* Update the BepInExPack dependency to 5.4.2351.

## 1.0.2

### Fixed
* Restore Harmony initialization on Valheim 1.0.7 by targeting its new inventory pointer-down and touch-click handlers after `InventoryGrid.OnRightClick` was removed.
* Block touch drag-to-drop actions that bypass ordinary button events while the settings window is open, while preserving release callbacks that clear pressed and drag state.

### Maintenance
* Build against original Valheim assemblies and validate Harmony targets, inventory guard boundaries, and menu/scaler reflection fields against client and dedicated-server metadata.
* Run managed regression tests as Debug by default and update the BepInExPack dependency to 5.4.2350.

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
