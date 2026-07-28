# Changelog

All notable changes to **Console Autocomplete** are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [SemVer](https://semver.org/).

When cutting a release:

1. Bump `ModVersion` in `src/Constants/ModInfo.cs`
2. Add a new `## [X.Y.Z] - YYYY-MM-DD` section **below**
3. Run `Scripts/Build-Release.ps1`
4. Push tag `vX.Y.Z` (GitHub Actions drafts the release from this file)

---

## [Unreleased]

### Changed

- Panel docks flush against the console bar and spans its full width; the gap that used to sit
  around the panel is now inner padding, so rows line up with the console prompt
- Description and suggestion rows are split by a thin rule with more space between them
- Suggestion rows are indented to the column of the argument they complete
  (rows for `give <item>` line up under `<item>` in the structure header)
- Source labels are separated from the value by a wider gap and a plain `-` instead of an em-dash
- Scroll cues sit at the right edge of the row block instead of inside the first/last row's text
- **Up / Down** wraps around, so Up on the first entry jumps to the last one

### Fixed

- Overlay positioning fell back to a fixed offset on IL2CPP because `Transform as RectTransform`
  returns null for interop wrappers; it now resolves the rect via `GetComponent`

## [0.1.0] - 2026-07-23

First public release. Dual MelonLoader backend (IL2CPP + Mono) with a small loader plugin.

### Added

- Live console autocomplete while typing (command words and arguments)
- Gray **ghost text** for the current selection; **Tab** to apply
- **Up / Down** moves through suggestions (vanilla history when the panel is closed)
- **Structure helper** under the console bar (`give <item> [quantity]`, etc.)
- Suggestion rows show source labels (`Vanilla`, `Legal Produce v…`, `CommandProbe v…`)
- Argument providers for common commands (`give`, `spawnvehicle`, `packageproduct`, teleports, NPCs, weather, enums, …)
- Per-save usage ranking stored under `{save}/Modded/ConsoleAutocomplete/usage_stats.json`
- Mod item attribution via `Registry.AddToRegistry` stack walk (e.g. Legal Produce crops)

### Fixed

- IL2CPP command indexing (`Console.Commands` property + list enumeration)
- Overlay was drawing **above** the top console bar (off-screen); panel now hangs **below** the input
- Packaging suggestions for `packageproduct` on IL2CPP (type filter + jar/baggie fallback)
- TMP Up-arrow jumping the caret while suggestions are open

### How to use (players)

1. Install **MelonLoader 0.7.2+** (0.7.3 recommended) for Schedule I
2. Copy from the release zip into your game folder:
   - `Plugins/ConsoleAutocomplete.Loader.dll`
   - `Mods/ConsoleAutocomplete.IL2CPP.dll`
   - `Mods/ConsoleAutocomplete.dll` (Mono branch; optional if you only play IL2CPP)
3. Launch the game - the loader enables the matching DLL and disables the other
4. Open the developer console and start typing (e.g. `give tom`, `spawnvehicle `, `acprobe`)

### Expected behavior

- A dark panel appears **under** the console bar with structure text, source line, and up to 8 suggestions
- Tab completes the highlighted row; typed Legal Produce / mix item IDs appear once they exist in the registry

### Notes

- Debug builds log `[ConsoleAutocomplete] [dbg]` lines; Release is quieter
- Does **not** index S1API-only commands that never enter native `Console.Commands`
