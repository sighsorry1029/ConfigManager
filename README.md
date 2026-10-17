# ConfigManager

[GitHub: sighsorry1029/ConfigManager](https://github.com/sighsorry1029/ConfigManager)

## License and attribution

This fork preserves work and attribution from the BepInEx team,
[shudnal](https://github.com/shudnal/ConfigurationManager),
[aedenthorn](https://github.com/aedenthorn/BepInEx.ConfigurationManager), and
[Azumatt](https://github.com/AzumattDev/BepInEx.ConfigurationManager).

## Path of Valheiman compatibility

ConfigManager 1.0.4 restores Path of Valheiman 5.0.4's effective world-setting values and client edit locks when its ConfigManager patches were skipped during startup. The optional integration uses PoV's own value and permission functions; no PoV files or configuration formats are changed.

PoV's early `ConfigurationManager.ConfigSettingEntry` warning can still appear when it loads first. ConfigManager initializes the fallback afterward and logs the result. PoV's separate Evasion mastery-category warning is unrelated.

## 1.0.4

* Restore optional Path of Valheiman setting-value and edit-lock integration regardless of which plugin loads first, without requiring PoV changes.
* Recheck PoV locks when applying changes from an already open editor, keep locked custom drawers read-only, and avoid duplicating PoV's existing value/write patches.
