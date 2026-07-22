# Console Autocomplete for Schedule I

MelonLoader mod that adds live autocomplete to the in-game developer console.

## Features

- Live suggestions, gray ghost text, Tab complete
- Up/Down navigates suggestions (history when the panel is closed)
- Structure helper header (`give <item> [quantity]`) + description / source line
- Rows show `suggestion — Mod Name vX.Y.Z` (or Vanilla)
- Arg providers keep suggestions relevant (`spawnvehicle` → vehicles, `give` → items, …)
- Per-save usage stats under `{save}/Modded/ConsoleAutocomplete/usage_stats.json`

## Install

```powershell
dotnet build .\Autocomplete.csproj -c Release
dotnet build .\Autocomplete.Il2Cpp.csproj -c Release
dotnet build .\loader\ConsoleAutocomplete.Loader.csproj -c Release
```

Outputs (also copied when `GameCorePath` is set):
- `ConsoleAutocomplete.dll` / `ConsoleAutocomplete.IL2CPP.dll` → `Mods/`
- `ConsoleAutocomplete.Loader.dll` → `Plugins/`

## Config

Copy `local.build.props.example` to `local.build.props` and set `GameCorePath`.

Usage stats are save-specific (written on game save, cleared when leaving a save).
