# Console Autocomplete (Schedule I)

**Author:** Hiemdallh  
**Requirements:** MelonLoader 0.7.2+ (0.7.3 recommended)  
**Supports:** IL2CPP (main) and Mono (alternate), via a small loader plugin

## Description

Adds **live autocomplete** to the Schedule I developer console.

- Suggestion list under the console bar  
- Structure helper (`give <item> [quantity]`)  
- Ghost text + Tab complete  
- Up/Down to move through suggestions  
- Labels for Vanilla vs mod-added items/commands  
- Argument helpers for common cheats (`give`, `spawnvehicle`, teleports, …)  
- Remembers frequent commands/args **per save**

## Installation

1. Install MelonLoader and run the game once.  
2. From the main file zip, copy:

```
Schedule I/
  Plugins/ConsoleAutocomplete.Loader.dll
  Mods/ConsoleAutocomplete.IL2CPP.dll
  Mods/ConsoleAutocomplete.dll          (Mono builds; optional on IL2CPP-only)
```

3. Launch the game. The loader enables the DLL that matches your backend.

## How to use

1. Open the console.  
2. Type — e.g. `give tom` or `spawnvehicle `.  
3. Use **Tab** to apply, **Up/Down** to change selection.

You should see a dark panel **below** the console strip with the structure line, a source line, and suggestions.

## Compatibility notes

- Works alongside mods that register items into the game Registry (e.g. Legal Produce).  
- Commands that only exist in S1API’s private registry (never added to native `Console.Commands`) will not appear until they are registered natively.  
- Mixed/discovered product IDs appear once the game adds them to the Registry.

## Troubleshooting

Attach `MelonLoader/Latest.log` and mention IL2CPP vs Mono.

## Changelog

See the **Changelogs** tab / GitHub release notes for this version.
