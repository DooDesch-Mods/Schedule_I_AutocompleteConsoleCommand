# Console Autocomplete

Live **developer console autocomplete** for [Schedule I](https://store.steampowered.com/app/3164500/Schedule_I/).

## Features

- Suggestions while you type (commands + args)
- Tab complete, Up/Down navigation, ghost text
- Structure helper (`give <item> [quantity]`)
- Source labels (`Vanilla`, other Melon mods)
- Smart arg lists (`give` items, `spawnvehicle`, weather, …)
- Per-save usage ranking

## Install (Thunderstore / r2modman / Gale)

1. Install this package (pulls in MelonLoader if needed)
2. Launch Schedule I
3. Open the console and type — a panel appears **under** the input bar

Manual layout inside the package:

- `Mods/ConsoleAutocomplete.IL2CPP.dll`
- `Mods/ConsoleAutocomplete.dll` (Mono, when present)
- `Plugins/ConsoleAutocomplete.Loader.dll`

The loader enables the correct DLL for IL2CPP vs Mono.

## How to play / what to expect

| You type | You should see |
|----------|----------------|
| `gi` | `give` (and similar) with structure `give <item> [quantity]` |
| `give tom` | Items like `tomato` / `tomato_seed` — mod crops show their mod name |
| `spawnvehicle ` | Vehicle ids |
| Tab | Completes the highlighted suggestion |

Newly mixed product strains show up after the game registers them (not a static list).

## Troubleshooting

Check `MelonLoader/Latest.log` for `[Console Autocomplete]`. Say whether you are on **IL2CPP** or **Mono** when reporting issues.

## Links

- Source: https://github.com/shreyas1996/Schedule_I_AutocompleteConsoleCommand
- Changelog: see GitHub Releases
