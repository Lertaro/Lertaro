# Temporary plugin parameter migration

`LegacyPluginSettingsMigration` is the only bridge from the old embedded
`PluginSettings` object. It runs only while `PluginSettingsStorageVersion` is absent.
The separate file (including recoverable backups) always wins after interruption.
Only after that file is valid is the main JSON rewritten with version 1 and without
the old parameters. Unknown host fields are preserved. A migrated installation
never falls back to embedded parameters when the new file is missing or corrupt.

After the migration window, delete this module and its calls in
`UserSettingsPersistence` (parse, load, restore) and `SettingsBackup.Prepare`.
Keep version validation and `PluginSettingsStore`. Old JSON/ZIP imports will then
be rejected because they lack version 1 and the separate parameter file.
Fresh installations should initialize an empty parameter file and version 1 only
when no host settings or backups exist; do not turn missing migrated files into
empty defaults.

Native plugin parameters live in `plugin-settings.json`; plugin activation and
component choices remain host preferences in `user-settings.json`. Plugin-owned
files retain their existing locations and backup categories.
