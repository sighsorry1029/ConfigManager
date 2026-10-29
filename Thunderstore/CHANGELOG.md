## 1.0.7

### Changed
* Show the detected shortcut combination as each key is pressed in the main window and setting editor, including combinations of three or more keys. Clear the preview on cancellation or ambiguous input.
* Preserve the existing recording and storage behavior: the last pressed key is the main key, releasing a key in the combination confirms it once, and the setting editor still requires Apply to save. Previewing intermediate combinations does not change the actual setting.
* Update the BepInExPack dependency to 5.4.2360.

## 1.0.6

### Added
* Add a single Background dimming option (0-100) under General, shared by the main window, setting editor, and file editor. Adjust it through the settings UI or configuration file.
* Keep the original appearance at the default value of 0. Higher values reduce game-background visibility behind window contents, and 100 blocks it completely in the content area. Preserve title bars, borders, text, and existing panel and hover colors.

## 1.0.5

### Changed
* Record new built-in shortcuts with the last pressed key as the main key, then confirm when any key in that combination is released. Ask for another attempt when presses occur within the same frame and their order cannot be determined.
* Display shortcuts as modifiers followed by the main key. Preserve existing bindings, BepInEx configuration serialization, custom hotkey drawers, and the detail editor's explicit Apply action.
* Keep partially typed ranged numeric values in an edit buffer instead of clamping each keystroke. Validate and clamp completed input when submitted, while preserving read-only checks and Path of Valheiman edit locks.
* Include single KeyCode settings in the Keybinds only filter.

### Fixed
* Cancel abandoned shortcut recordings on window close, focus loss, or editor changes. Prevent a recording key from also triggering the manager's hotkeys, and prevent simultaneous built-in key recording in the main and detail windows.
* Isolate settings-collection failures to the affected plugin so incomplete metadata or a failing entry does not hide other plugins' settings.
* Restore the GUI coordinate matrix only when the matching Harmony prefix changed it, including when manager visibility changes during the call.

### Performance and maintenance
* Reuse GUI styles until their inputs change and save window geometry after interaction, with related configuration updates written together.
* Add managed regression coverage for shortcut press/release ordering, recording ownership, serialization, numeric editing, and batched window configuration saves.

## 1.0.4

### Fixed
* Restore Path of Valheiman's effective world-setting values and client edit locks when its ConfigManager integration was skipped because ConfigManager loaded later. Use PoV's own policy functions without modifying its DLL or configuration.
* Recheck edit permissions for open setting editors and block writes and Reset when PoV reports a lock. Display locked values without invoking custom drawers that could write directly to configuration entries. Preserve ordinary settings and skip value/write fallback paths already handled by PoV's native patches.

### Compatibility
* Bind the optional integration once after plugin initialization and release cached delegates on shutdown. If a bound PoV callback fails, keep its settings read-only instead of allowing unchecked edits.
* The original early type-lookup warning and the unrelated Evasion mastery-category warning may still appear. This update restores the settings integration; it does not alter PoV's startup or skill-tree data.

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
