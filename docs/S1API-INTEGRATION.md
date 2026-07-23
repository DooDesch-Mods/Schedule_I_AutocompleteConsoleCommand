# S1API Integration Analysis — Console Autocomplete

**Status:** Research / proposal only (no implementation in this repo yet)  
**Audience:** Future PR to [ifBars/S1API](https://github.com/ifBars/S1API) + maintainers of this mod  
**Date:** 2026-07-23  
**Related code:** this repo (`Schedule_I_Autocomplete`); S1API console module under `S1API/Console/`

---

## 1. Goal (player / modder experience)

**Desired end state (as requested):**

> Anyone who has **S1API** installed gets console autocomplete “for free,” whether or not other mods currently use S1API — i.e. shipping autocomplete *with* S1API (or as a tightly coupled default feature) so the Mods/Plugins layout of S1API enables it automatically.

**Secondary goals:**

- Autocomplete must see **both** vanilla/`Console.Commands` **and** S1API `BaseConsoleCommand` subclasses (today those are invisible to this mod).
- Mod authors should be able to declare **arg suggestions** in a managed, Mono/IL2CPP-safe way (same spirit as `BaseConsoleCommand`).
- Stay consistent with S1API’s design rules: cross-backend builds, don’t leak game/Il2Cpp types in the **public** API, PRs → `bleeding-edge`.

---

## 2. How the pieces work today

### 2.1 Console Autocomplete (this mod)

| Concern | Approach |
|--------|----------|
| Command discovery | Live index of native `ScheduleOne.Console.Commands` / Il2Cpp equivalent |
| Custom S1API commands | **Not indexed** (they never enter the native list) |
| Arg suggestions | Local `IArgProvider` + game registries (items, vehicles, …) |
| UI | Harmony on `ConsoleUI` + TMP overlay under the input bar |
| Packaging | Own MelonMod + Loader (Mono + IL2CPP DLLs) |

### 2.2 S1API console (ifBars/S1API)

| Concern | Approach |
|--------|----------|
| Custom commands | Subclass `S1API.Console.BaseConsoleCommand` (`CommandWord` / `Description` / `ExampleUsage` / `ExecuteCommand`) |
| Registration | Auto-discover subclasses on `Console.Awake` → **internal** `CustomConsoleRegistry` |
| Execution | Harmony **prefix** on `Console.SubmitCommand`: if not in native `commands` dict → run managed command |
| Help UI | `CommandListScreen.Start` postfix adds rows (TMP) for custom commands |
| Native list | Custom commands are **intentionally not** added to `Console.Commands` (avoids subclassing Il2Cpp abstracts) |
| Public surface | `BaseConsoleCommand`, `ConsoleHelper`; registry is **`internal`** |

This is deliberate and good for S1API’s abstraction goals — but it means any autocomplete that only reads `Console.Commands` will miss every S1API command (MultiDelivery, etc.).

### 2.3 Packaging reality

S1API already ships:

- `Plugins/S1APILoader*.dll`
- `Mods/S1API.*.dll` (Mono + IL2CPP, loader picks one)

Autocomplete today is a **separate** MelonMod. “Just having S1API” does **not** currently enable autocomplete.

---

## 3. Options for “default with S1API”

### Option A — Embed full autocomplete UI inside S1API

Ship ConsoleUI Harmony + overlay + suggestion engine as part of the S1API MelonMod.

| Pros | Cons |
|------|------|
| True “install S1API → autocomplete works” | Large UI PR; fights “API layer, not product UX” |
| One dependency for players who already need S1API | Couples TMP/`ConsoleUI` deeply into S1API internals forever |
| Can see managed registry natively | Harder to iterate UX without S1API releases |
| | Violates spirit of “don’t leak game UI types” even if kept `internal` |
| | Players who want autocomplete **without** S1API lose out unless feature is split again |

**Fit with S1API standards:** Poor as a first PR. Maintainers may reject scope.

### Option B — S1API exposes metadata + arg-provider API; UI stays a separate mod (recommended foundation)

S1API gains a **small public console catalog / provider API**. Autocomplete (this mod, or a thin “S1API.ConsoleUX” package) consumes it.

| Pros | Cons |
|------|------|
| Aligns with S1API’s role (abstraction + discovery) | “Having S1API alone” does **not** enable UI until something ships the UX |
| Unlocks autocomplete for all `BaseConsoleCommand`s | Two packages unless you also ship Option C |
| Easy to PR in phases | Soft dependency / version negotiation |
| Matches PhoneApp-style auto-discovery patterns | |

**Fit:** Excellent for an initial PR.

### Option C — Hybrid: API in S1API + optional bundled UX package (best match to the user’s “default feature” ask)

1. Land **Option B** APIs in S1API.  
2. Either:
   - **C1:** S1API release zip **also includes** `ConsoleAutocomplete.*.dll` (same loader story as dual backends), or  
   - **C2:** New MelonMod `S1API.ConsoleAutocomplete` living in the S1API repo / solution, enabled by the same loader, default-on.

| Pros | Cons |
|------|------|
| Matches “install S1API → autocomplete on” | Bigger release / support surface for ifBars |
| UI can still be disabled or replaced | Need clear ownership of bugs (API vs UX) |
| Autocomplete can soft-depend on S1API APIs and degrade gracefully | Versioning: UX feature vs API semver |

**Fit:** Best long-term product story; propose **after** Option B lands or as a follow-up PR series.

### Option D — Inject S1API commands into native `Console.Commands`

Register Il2Cpp/`RegisterTypeInIl2Cpp` shells (like CommandProbe) into the game list.

| Pros | Cons |
|------|------|
| Existing Autocomplete indexes them with zero S1API API change | Fragile on IL2CPP; S1API explicitly avoided this |
| | Duplicates SubmitCommand routing |
| | Unlikely to be accepted upstream |

**Fit:** Not recommended for an S1API PR.

---

## 4. Recommendation

**Phased approach:**

1. **Near-term (this mod, no S1API merge required)**  
   - Optional reflection soft-hook into `CustomConsoleRegistry` if S1API assembly is present (fragile; stopgap only).  
   - Prefer waiting for a public API.

2. **S1API PR #1 (API-only, high chance of merge)** — Option B  
   - Public read-only command catalog.  
   - Optional arg-provider / structure metadata.  
   - Zero ConsoleUI code.

3. **S1API PR #2 / release packaging (Option C)** — discuss with ifBars first  
   - Bundle or sibling-ship autocomplete UX so S1API installs enable it by default.  
   - Keep UX code in a dedicated project under the S1API solution (or this repo as a submodule / NuGet consumed by S1API packaging).

Do **not** lead with a giant ConsoleUI PR into S1API.

---

## 5. Proposed S1API public API (sketch)

Aligned with `BaseConsoleCommand` and coding standards (managed types only, XML docs, PascalCase, `#if` only inside Internal).

### 5.1 Command catalog

```csharp
namespace S1API.Console
{
    /// <summary>Snapshot of a registered console command (vanilla wrappers optional later).</summary>
    public sealed class ConsoleCommandInfo
    {
        public string CommandWord { get; }
        public string CommandDescription { get; }
        public string ExampleUsage { get; }
        public string? StructureHeader { get; }  // optional; else tooling parses ExampleUsage
        public string SourceLabel { get; }       // e.g. Melon mod name / "S1API"
    }

    /// <summary>Public read-only view over custom (and eventually unified) commands.</summary>
    public static class ConsoleCommandCatalog
    {
        public static IReadOnlyList<ConsoleCommandInfo> GetCustomCommands();
        // Future: GetAll() merging documented built-ins via ConsoleHelper metadata
    }
}
```

Implementation: thin wrapper over existing `CustomConsoleRegistry` (promote listing, keep mutate paths internal).

### 5.2 Arg providers (optional, discoverable)

```csharp
namespace S1API.Console
{
    public sealed class ConsoleArgCandidate
    {
        public string Value { get; init; }
        public string? DisplayLabel { get; init; }
        public string? SourceLabel { get; init; }
    }

    /// <summary>
    /// Implement and expose a public parameterless constructor;
    /// auto-registered similarly to BaseConsoleCommand.
    /// </summary>
    public abstract class BaseConsoleArgProvider
    {
        public abstract string CommandWord { get; }
        public abstract int ArgIndex { get; } // 0 = first arg after command word
        public abstract IEnumerable<ConsoleArgCandidate> GetCandidates(
            IReadOnlyList<string> tokensSoFar);
    }
}
```

Mirror this mod’s `IArgProvider` / `ArgCandidate` without referencing game item types in the public signature (providers that need Registry stay in the UX mod or use S1API item wrappers if those exist).

### 5.3 Optional enrichment on `BaseConsoleCommand`

Additive, non-breaking:

```csharp
public virtual string? StructureHeader => null;
// or
public virtual IReadOnlyList<BaseConsoleArgProvider>? ArgProviders => null;
```

Prefer separate `BaseConsoleArgProvider` discovery so existing commands need no changes.

### 5.4 What stays out of public S1API

- `ConsoleUI`, `TMP_InputField`, overlay GameObjects  
- Harmony patches for Tab / caret  
- Direct `ScheduleOne.*` / `Il2CppScheduleOne.*` in public method signatures  

Those remain in Internal (if ever) or in the UX assembly.

---

## 6. Improvements we should make in Console Autocomplete *before* / *for* an S1API PR

Independent of merge politics — harden this mod so an upstream handoff is cleaner:

| Area | Why |
|------|-----|
| **Split “engine” vs “UI”** | `SuggestionEngine`, command index, arg providers vs `SuggestionOverlay` / ConsoleUI patches — engine can later call S1API catalog |
| **Pluggable command sources** | `ICommandSource` (NativeCommands, S1ApiCommands, …) instead of only `CommandIndex` → game list |
| **Pluggable arg providers already exist** | Keep managed-only candidate DTOs; move game-specific providers behind adapters |
| **Soft S1API reference** | Detect `S1API` assembly; use catalog API when version ≥ X; no hard compile dependency required for players without S1API |
| **Attribution** | Label S1API commands as Melon mod assembly (same as today for ProbeCommand) |
| **Strip debug probes** | Packaging ID probes / verbose dbg for Release packaging before upstreaming UX |
| **Tests / fixtures** | Pure managed unit tests for tokenize / rank / structure normalize (no game) |
| **Docs** | Keep player-facing CHANGELOG quality; link S1API dependency matrix |

When proposing Option C, the UX project should compile against **public S1API** only (not InternalsVisibleTo), same as any other mod.

---

## 7. Alignment with S1API contribution standards

From [CONTRIBUTING.md](https://github.com/ifBars/S1API/blob/stable/CONTRIBUTING.md) / coding standards:

| Rule | How this proposal complies |
|------|----------------------------|
| PR target `bleeding-edge` | Open discussion issue + PR there first |
| Build **Il2CppMelon** + **MonoMelon** | API-only PR is easy; UX project needs both configs too |
| Don’t casually change GitHub Actions | Leave their CI alone unless asked |
| No leaking Il2Cpp/game types in public API | Catalog + arg DTOs are managed-only |
| Internals in `S1API.Internal.*` | Keep Harmony SubmitCommand / Awake patches internal; add public façade |
| XML docs on public API | Required for catalog / providers |
| Naming / folders | `S1API.Console` next to `BaseConsoleCommand` |

**Process tip:** Open a GitHub Issue / Trello note on ifBars’ board *before* a large PR: “Public ConsoleCommandCatalog + optional Console Autocomplete bundling.” Get buy-in on Option B vs C.

---

## 8. Dependency & versioning sketch

```
[Players]
  MelonLoader
   └─ S1API (loader + API)     ← almost everyone with content mods
   └─ ConsoleAutocomplete UX   ← today separate; later bundled (Option C)

[Mod authors]
  Reference S1API NuGet / DLL
   └─ BaseConsoleCommand (+ optional BaseConsoleArgProvider)
```

SemVer:

- Catalog / provider API → **minor** bump of S1API (additive).  
- Bundling UX → packaging/release notes, not necessarily API break.  
- Autocomplete soft-depends: `S1API >= 3.x` with feature detection.

Thunderstore: if bundled, declare `ifBars-S1API_Forked-x.y.z` as dependency of the UX package, or ship both in one zip with clear Mods/Plugins layout (same as S1API’s current dual DLL pattern).

---

## 9. Risks & open questions for ifBars

1. **Product scope:** Is console UX in-scope for S1API, or should it stay a sister mod with a hard recommendation?  
2. **Default-on:** If bundled, can players disable autocomplete via MelonPreferences without removing S1API?  
3. **BepInEx builds:** S1API also has BepInEx configs — does autocomplete need those targets or Melon-only?  
4. **Performance:** Catalog is tiny; arg providers that scan full item registries should stay lazy / cached (lesson from this mod).  
5. **CommandListScreen vs live console:** Catalog helps both; UI work is ConsoleUI-only.  
6. **Ownership:** Who maintains overlay bugs after merge — S1API maintainers or Autocomplete authors as CODEOWNERS of a subproject?

---

## 10. Suggested PR series (concrete)

| PR | Repo | Contents |
|----|------|----------|
| **0** | Schedule_I_Autocomplete | Refactor: `ICommandSource`, soft S1API detection stub, engine/UI split (optional prep) |
| **1** | ifBars/S1API | `ConsoleCommandCatalog.GetCustomCommands()` + docs + samples |
| **2** | ifBars/S1API | `BaseConsoleArgProvider` auto-discovery + register/list API |
| **3** | Schedule_I_Autocomplete | Consume catalog/providers; document “works best with S1API ≥ …” |
| **4** | ifBars/S1API (discussion) | Bundle or sibling-ship UX as default in S1API release zip / loader |

---

## 11. Bottom line

- **Yes, it can become a default companion to S1API**, but stuffing the full ConsoleUI overlay into S1API as the *first* PR is the wrong shape for that codebase.  
- **Best path:** standardize on S1API’s existing console pattern (managed commands + Harmony routing) by exposing a **public catalog + arg-provider API**, then either keep UX as this mod (soft-dependent) or **bundle it in S1API’s release** once maintainers agree (Option C).  
- **Critical gap today:** Autocomplete only sees native `Console.Commands`; S1API commands will never appear until catalog API (or fragile reflection) exists.  
- **Standards fit:** Option B is fully compatible with ifBars’ MIT project, dual Melon builds, and “no game types in public API” rule.

When ready to engage upstream: draft an issue on [ifBars/S1API](https://github.com/ifBars/S1API/issues) linking this doc and asking for preference between **API-only** vs **API + bundled UX**.
