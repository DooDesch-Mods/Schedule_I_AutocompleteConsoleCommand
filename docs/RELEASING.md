# Releasing Console Autocomplete

This repo follows the same *shape* as Legal Produce’s local packaging (`Scripts/Build-Release.ps1` → `releases/v<version>/`), plus GitHub Actions for release notes and optional store uploads.

Legal Produce does **not** have Thunderstore/Nexus CI yet — treat this Autocomplete pipeline as the template to copy later.

## One-time setup

### GitHub

1. Push this repo (already: `Schedule_I_AutocompleteConsoleCommand`).
2. Optional repository **secrets** (Settings → Secrets and variables → Actions):

| Secret | Purpose |
|--------|---------|
| `THUNDERSTORE_TOKEN` | Thunderstore [service account](https://thunderstore.io/) token |
| `THUNDERSTORE_NAMESPACE` | Team name on Thunderstore (e.g. `Hiemdallh`) |
| `NEXUSMODS_API_KEY` | [Nexus API key](https://www.nexusmods.com/settings/api-keys) |
| `NEXUSMODS_FILE_ID` | File id from an existing Nexus main file (after first manual upload) |

Optional **variables**:

| Variable | Example |
|----------|---------|
| `THUNDERSTORE_COMMUNITY` | `schedule-i` |
| `NEXUSMODS_GAME_DOMAIN` | `schedule1` |

> **First Nexus upload** must be done once by hand in the Nexus UI (create mod page + first main file). After that, Actions can push new versions to the same `NEXUSMODS_FILE_ID`.

### Local

- Schedule I install with MelonLoader (for IL2CPP refs under `MelonLoader/Il2CppAssemblies`)
- For dual-branch zips: also have Mono `Schedule I_Data/Managed` available once
- `dotnet` SDK, and `gh` CLI if you want `-CreateGitHubRelease`

## Cut a release (happy path)

1. **Update version** in `src/Constants/ModInfo.cs` (`ModVersion`).
2. **Write notes** in `CHANGELOG.md` under `## [X.Y.Z] - YYYY-MM-DD`  
   (what’s new, fixed, how to install/use, what players should expect).
3. **Build packages locally** (CI cannot see your game assemblies):

```powershell
cd d:\github_projects\Schedule_I_Autocomplete
.\Scripts\Build-Release.ps1 -Configuration Release
# IL2CPP-only machine:
# .\Scripts\Build-Release.ps1 -SkipMono
```

Outputs under `releases/vX.Y.Z/`:

| File | Use |
|------|-----|
| `ConsoleAutocomplete - vX.Y.Z.zip` | Nexus / manual drop-in (`Mods/` + `Plugins/`) |
| `Hiemdallh-ConsoleAutocomplete-X.Y.Z.zip` | Thunderstore |
| `README.txt` / `CHANGELOG.txt` | Player-facing copy inside the zip |
| `NEXUS_DESCRIPTION.md` | Paste into Nexus description if needed |

4. **Commit** changelog + version bump (do **not** commit `Mods/*.dll` or `*.zip` — gitignored).
5. **Tag and push**:

```powershell
git tag vX.Y.Z
git push origin master
git push origin vX.Y.Z
```

6. GitHub Actions **Release** workflow:
   - Validates tag ↔ `ModInfo`
   - Opens a **draft** GitHub Release with the CHANGELOG section as the body
7. **Attach zips** to that draft (UI, or):

```powershell
gh release upload vX.Y.Z `
  ".\releases\vX.Y.Z\ConsoleAutocomplete - vX.Y.Z.zip" `
  ".\releases\vX.Y.Z\Hiemdallh-ConsoleAutocomplete-X.Y.Z.zip" `
  --clobber

# Or rebuild with:
# .\Scripts\Build-Release.ps1 -CreateGitHubRelease
```

8. **Publish** the GitHub release (undraft).  
   The **Publish stores** workflow then runs:
   - Thunderstore upload if `THUNDERSTORE_TOKEN` is set
   - Nexus upload if `NEXUSMODS_API_KEY` + `NEXUSMODS_FILE_ID` are set  
   Missing secrets → step skipped (release on GitHub still succeeds).

## Why builds are local

IL2CPP/Mono projects reference `Assembly-CSharp` / MelonLoader Il2Cpp interop from your game install. Those binaries must not be committed to a public repo. Legal Produce uses the same local `Build-Release.ps1` approach for that reason.

## Checklist (player-facing notes)

Each CHANGELOG section should answer:

- [ ] What changed / what was fixed (plain language)
- [ ] How to install / update
- [ ] How to use the feature in-game
- [ ] What “good” looks like (panel under console, Tab, labels, …)
- [ ] Known limits (e.g. S1API-only commands)

## For Legal Produce later

Copy:

- `Scripts/Build-Release.ps1` pattern (already exists there)
- `CHANGELOG.md` discipline
- `.github/workflows/release.yml` + `publish-stores.yml`
- `packaging/thunderstore/` + `packaging/nexus/`

Wire the same secrets on the Legal Produce repo when ready.
