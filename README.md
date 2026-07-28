# Console Autocomplete for Schedule I

MelonLoader mod that adds live autocomplete to the in-game developer console.

## Features

- Live suggestions, gray ghost text, Tab complete
- Up/Down navigates suggestions (history when the panel is closed)
- Structure helper header (`give <item> [quantity]`) + description / source line
- Rows show `suggestion - Mod Name vX.Y.Z` (or Vanilla)
- Arg providers keep suggestions relevant (`spawnvehicle` → vehicles, `give` → items, …)
- Per-save usage stats under `{save}/Modded/ConsoleAutocomplete/usage_stats.json`

## Install (players)

Prefer a [GitHub Release](https://github.com/shreyas1996/Schedule_I_AutocompleteConsoleCommand/releases) zip (or Thunderstore / Nexus once published).

```
Schedule I/
  Plugins/ConsoleAutocomplete.Loader.dll
  Mods/ConsoleAutocomplete.IL2CPP.dll
  Mods/ConsoleAutocomplete.dll            # Mono branch
```

Open the console and type - a panel appears **under** the input bar.

## Dev build

```powershell
dotnet build .\Autocomplete.csproj -c Release
dotnet build .\Autocomplete.Il2Cpp.csproj -c Release
dotnet build .\loader\ConsoleAutocomplete.Loader.csproj -c Release
```

Copy `local.build.props.example` → `local.build.props` and set `GameCorePath`.

## Ship a release

```powershell
# 1) Bump ModVersion + edit CHANGELOG.md
# 2) Package (needs local game assemblies)
.\Scripts\Build-Release.ps1 -Configuration Release

# 3) Tag → draft GitHub release (Actions)
git tag v0.1.0
git push origin v0.1.0

# 4) Attach zips from releases\v0.1.0\, publish the draft
#    → optional Thunderstore / Nexus upload if secrets are set
```

Full checklist: [docs/RELEASING.md](docs/RELEASING.md). Changelog: [CHANGELOG.md](CHANGELOG.md).

## Future: S1API

Analysis of folding autocomplete into / alongside [S1API](https://github.com/ifBars/S1API) (catalog API, arg providers, optional bundled UX): [docs/S1API-INTEGRATION.md](docs/S1API-INTEGRATION.md).

## Testing mod-added commands

Sibling probe mod [`Schedule_I_CommandProbe`](../Schedule_I_CommandProbe) injects `acprobe*` into native `Console.Commands`. Type `acprobe` to verify indexing.

## Debug / IL2CPP troubleshooting

```powershell
dotnet build .\Autocomplete.Il2Cpp.csproj -c Debug
```

Look for `[ConsoleAutocomplete] [dbg]` in `MelonLoader/Latest.log`.
A `Mods/ConsoleAutocomplete.IL2CPP.debug.marker` file confirms the Debug DLL was copied.
