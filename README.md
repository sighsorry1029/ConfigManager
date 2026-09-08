# ConfigManager

[GitHub: sighsorry1029/ConfigManager](https://github.com/sighsorry1029/ConfigManager)

A Valheim fork of [shudnal/ConfigurationManager](https://github.com/shudnal/ConfigurationManager)
1.1.13 (`1b83eac`), based on
[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager).

## External settings

Preloader patchers and other owners without a `BaseUnityPlugin.Config` can register
settings in the normal list. The owner keeps its values, validation, configuration
file, and save/reload policy; ConfigManager does not create a second config file.

```csharp
IDisposable registration = ConfigurationManager.ConfigurationManager.RegisterExternalSettings(
    "example.owner", "Example owner", "1.0.0",
    new[]
    {
        new ConfigurationManager.ExternalSetting("General", "Enabled", "Enable this feature.",
            typeof(bool), true, () => ReadCurrentEnabled(), value => SaveEnabled((bool)value))
    });
// Dispose when the owner shuts down or withdraws its settings.
registration.Dispose();
```

The public `ExternalSetting` constructor accepts category, name, description,
setting type, default value, `Func<object>` getter, and `Action<object>` setter.
Pass a null setter to display a read-only value. Its public `ReadOnly` property is
true; the getter stays live, while checkboxes, Edit, Reset and direct write helpers
cannot change the value. Read-only status text uses its description as a tooltip.
Optional integrations may find this constructor and `RegisterExternalSettings`
through reflection after ConfigManager has loaded. `RegisterCustomSettingDrawer`
only changes drawing for an existing setting and does not register settings.

Use a stable, unique owner GUID and unique category/name pairs. Re-registering the
same GUID replaces its entries atomically. An older handle cannot remove the new
registration. Invalid registrations leave the previous one intact. Registration
does not call getters/setters; the UI calls them on the Unity main thread. Keep
getters inexpensive and return the latest value. Throw from a setter when saving
or validation fails; the error remains visible without interrupting other entries.
ConfigManager cannot undo side effects that an owner performed before throwing.

Bool and enum values use the existing controls. External numbers are edited inline:
Enter or leaving the field submits the value, while Esc cancels the edit and shows
the latest owner value. Incomplete or rejected input stays buffered with its error;
redrawing the field does not retry a failed save. A clean, unfocused field follows
owner config reloads without rebuilding the list. External strings keep the Edit
window with an input buffer and explicit Apply. Reset uses the same owner setter
(or stages the default in the Edit window). External entries also respect the
hidden-settings file.

Ordinary external enums reuse their choices and labels while reading the latest
owner value. External flags use cached nonzero masks and static description labels
in both the inline toggles and Edit window. The owner declares the flag groups and
default; zero is hidden, and ConfigManager adds no synthetic None or All toggles.
An explicitly declared nonzero composite remains an option. Custom drawers,
acceptable-value lists, and derived description attributes keep their existing
rendering behavior. Closing or rebuilding the manager's UI clears the enum cache.

The list refreshes when registrations change, not every frame. Registrations live
until their handles are disposed and survive recreation of the manager window or
plugin instance. Disposed/replaced entries release their callbacks; an already
running callback may finish. The existing file editor remains a separate way to
edit `.cfg` text and does not infer typed settings from files or comment metadata.

### Validation

Build and run the managed regression harness without packaging or deployment:

```powershell
./RunExternalSettingsTests.ps1
# Also run the release dependency/API checks (requires Visual Studio MSBuild):
./build.ps1 -SkipPackaging
```

The test script builds the plugin first and accepts `-BepInExPath` and
`-ManagedAssembliesPath`. `-SkipPluginBuild` explicitly tests an existing DLL;
`-AssemblyPath` can select that DLL. `build.ps1` accepts `-ValheimGamePath` and
runs these tests before packaging. Incorrect test-harness argument counts fail.
The test executable runs with isolated temporary BepInEx state and a local
`loadFromRemoteSources` setting so downloaded game assemblies can load under
.NET Framework without modifying the game installation.

Tests cover registration, replacement/disposal, read-only values and write guards, callback failures, typed null
Reset, inline numeric Enter/focus-loss commits, Escape, rejected input, owner reloads,
numeric focus transfer/withdrawal, case-only string Apply, text/list transitions,
enum cache reuse/reload/selection and dynamic descriptions, external attribute
compatibility, all sixteen four-group flag combinations, typed checkbox saves and
Edit-window Apply failures, and file-editor save/recovery and tree changes.
They do not render Unity UI or launch Valheim. Worker registration tests use
sequential joins and do not establish concurrent-race correctness.

Before release, test actual client and host input blocking (inventory, hotbar,
mouse and gamepad), dropdown hit testing at different GUI scales, editor color
previews, inline numeric Enter/Esc and mouse/Tab focus changes at different scales,
and disable/enable and destroy/recreate with a menu open. Verify one
menu button and one callback, restored navigation/cursor, and unchanged item
counts. Test dedicated-server loading separately; there is no new server policy
or network authority implementation in this change. Optional integrations must
still enforce their own permissions and duplicate-request handling.

## File editor recovery

Saving keeps the exclusive file lock and checks for external changes. New UTF-8
content and a byte-for-byte original backup are prepared before modifying the
destination. A write failure triggers restoration while the lock is still held;
if restoration also fails, the error reports the retained backup path. Temporary
save files use `.configmanager-save-` names next to the edited file. Normal saves
clean them up. This is recovery protection, not an atomic or crash-proof commit;
after a process or machine failure, retain any backup until the file is checked.
Saving requires permission to create files in that directory and enough space
for the staged content and original backup; preparation failures leave the
original file untouched.
The owner of a typed setting still controls its own persistence.

GUI styles continue to rebuild so in-place skin edits by other mods remain
visible. Longer-lived style caching is deferred until those integrations can be
validated in Unity; search tokens and static enum labels are reused locally.

## License and attribution

This project is distributed under [GNU GPL v3 only](https://github.com/sighsorry1029/ConfigManager/blob/main/LICENSE)
(`GPL-3.0-only`). Existing copyright and license notices are preserved;
this fork was modified by sighsorry in 2026.

This fork preserves work and attribution from the BepInEx team,
[shudnal](https://github.com/shudnal/ConfigurationManager),
[aedenthorn](https://github.com/aedenthorn/BepInEx.ConfigurationManager), and
[Azumatt](https://github.com/AzumattDev/BepInEx.ConfigurationManager).
