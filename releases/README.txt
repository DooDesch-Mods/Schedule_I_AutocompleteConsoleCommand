Console Autocomplete {{VERSION}}
================================
MelonLoader mod for Schedule I
Author: Hiemdallh

Live autocomplete for the in-game developer console: suggestions, Tab complete,
structure helper, and mod/vanilla source labels.


REQUIREMENTS
------------
- Schedule I with MelonLoader 0.7.2 or newer (0.7.3 recommended)
- Works on IL2CPP (main Steam branch) and Mono (alternate branch), when both
  mod DLLs are present


INSTALLATION
------------
1. Install MelonLoader and launch the game once.
2. Copy files from this zip into your Schedule I folder:

     Schedule I/
       Plugins/
         ConsoleAutocomplete.Loader.dll
       Mods/
         ConsoleAutocomplete.IL2CPP.dll    (IL2CPP build)
         ConsoleAutocomplete.dll           (Mono build, if included)

3. Launch the game.
   The loader detects IL2CPP vs Mono, enables the matching mod DLL, and
   renames the other to *.dll.disabled so MelonLoader does not load both.

Leave both mod DLLs in Mods when possible and let the loader handle it.


HOW TO USE
----------
1. Open the developer console (same key you normally use).
2. Start typing a command (example: give, spawnvehicle, packageproduct).
3. A panel appears under the console bar showing:
     - Structure hint, e.g. give <item> [quantity]
     - Description / source line
     - Up to 8 suggestions (Tab to apply, Up/Down to move)
4. Gray ghost text shows the completion for the highlighted row.


WHAT TO EXPECT
--------------
- Vanilla items and commands are labeled "Vanilla"
- Mod-registered items (e.g. Legal Produce tomato_seed) show the mod name
- Newly mixed / discovered products appear after the game adds them to the
  Registry (live list — not a fixed dump)
- Usage ranking remembers what you run often (per save)


UPDATING
--------
Replace the DLLs in Mods/ and Plugins/ with the ones from the new zip.
Usage stats in your save folder are kept.


TROUBLESHOOTING
---------------
- No panel under the console: confirm ConsoleAutocomplete.*.dll is enabled
  (not .disabled) for your backend, and check MelonLoader Latest.log
- Suggestions work but look empty: try toggling the console once after load
- Send MelonLoader/Latest.log and say whether you are on IL2CPP or Mono


LINKS
-----
GitHub: https://github.com/shreyas1996/Schedule_I_AutocompleteConsoleCommand
