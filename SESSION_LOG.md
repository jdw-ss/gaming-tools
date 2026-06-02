# Session Log — Gaming Tools (monorepo)

Append-only, newest entries on top. Format defined in `~/Claude Projects/docs/DOCUMENTATION_PYRAMID.md` → `<project>/SESSION_LOG.md`.

Write an entry at the end of any non-trivial session (anything that produced commits, decisions, abandoned approaches, or mid-flight work). Skip for pure read-only / Q&A / typo-fix sessions.

When adding a new plugin to the monorepo, prefer one entry covering the whole bootstrap rather than per-file entries.

---

<!-- New entries go directly below this line -->

## 2026-06-02 — Bootstrap GcSupplyHelper plugin (v0.1.0)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: pending (this session)

### Changed

- Added a **third plugin** to the monorepo: `FF14/GcSupplyHelper/`. Reads today's 11 Grand Company Supply / Provisioning missions from `AgentGrandCompanySupply` and presents per-mission + aggregate raw-material shopping lists in a three-tab ImGui window.
- New files:
  - `FF14/GcSupplyHelper/GcSupplyHelper.csproj` — mirrors the WondrousTailsOdds v0.1.2+ csproj shape; no third-party PackageReferences.
  - `FF14/GcSupplyHelper/GcSupplyHelper.json` — manifest, `DalamudApiLevel: 15`, `AssemblyVersion: 0.1.0.0`.
  - `Plugin.cs`, `Configuration.cs` (HashSet of enabled ClassJob ids, AutoRefreshOnLogin flag).
  - `Models/DailyMission.cs`, `Models/MaterialRequirement.cs`.
  - `Services/IRecipeDataSource.cs` (abstraction), `Services/RecipeWalker.cs` (pure recursive solver with cycle guard and ceil-quantity math), `Services/LuminaRecipeDataSource.cs` (production impl), `Services/SupplyMissionReader.cs` (unsafe `AgentGrandCompanySupply` reader).
  - `Windows/MainWindow.cs` — Per Mission / Aggregate / Settings tabs with item icons, source labels, and "Needed by" class chips.
  - `LICENSE` (MIT), per-plugin `README.md`.
  - `.github/workflows/build-gcsupplyhelper.yml` — clone of `build-wondroustailssolver.yml` with name and path-filter substitutions.
  - `docs/ff14/gcsupplyhelper/pluginmaster.json` — placeholder `[]`.
- Edited:
  - `Gaming Tools/CLAUDE.md` — snapshot lists three plugins; new "Grand Company supply data surface" gotcha section.
  - `Gaming Tools/README.md` — third row in Plugins table, updated layout diagram, per-plugin docs link.
  - `Gaming Tools/IDEAS.md` — new backlog item: reverse-engineer `UIState.GCSupply` layout for zero-UI-trigger population.
  - `~/Claude Projects/docs/PROJECT_INDEX.md` — `gaming-tools` one-liner now lists all three plugins.

### Decisions

- **Multi-addon refresh hooks instead of just Personnel Officer**: the user pointed out the in-game Timers panel also shows the day's missions. The data source agent populates the same way regardless of which UI triggers it, so the plugin registers `PostSetup` listeners against `GrandCompanySupplyList` (Personnel Officer, confirmed) plus `ContentsInfo` / `ContentsInfoDetail` / `ContentsTimerSetting` (Timers candidates). First in-game test will identify the real Timers addon name and prune the dead candidates.
- **`UIState.GCSupply` reverse-engineering deferred to v0.2.** The 11,304-byte buffer at offset `0x10D28` almost certainly contains today's missions persistently from login onwards, but FFXIVClientStructs hasn't annotated its layout. Diffing the buffer at known trigger points to find the 11 item-ID offsets is real work; the multi-addon hook approach satisfies the user's "I shouldn't have to specifically visit the officer" intent without it.
- **`IRecipeDataSource` abstraction over Lumina sheets.** Production implementation walks Lumina once at construction; test implementation injects hand-crafted recipes. Lets `RecipeWalker` be exercised off-tree with no Dalamud / Lumina dependency. 13 scratch assertions pass: ceil-quantity math, cycle termination + warning, direct gather leaf, cross-class aggregation with NeededByClassJobs accumulation.
- **Class-job index → array slot mapping hardcoded.** `_supplyData[0..7]` = ClassJobs 8..15 (CRP..CUL), `_provisioningData[0..2]` = ClassJobs 16..18 (MIN..FSH). The mapping is hand-coded in `SupplyMissionReader.cs:21`; if SE reorders the arrays in a future patch the symptom is "Carpenter mission asks for leather" and the fix is a one-line edit. Documented as a gotcha.
- **Recipe lookup builds a single eager `Dictionary<itemId, RecipeData>` at construction.** Avoids the `RecipeLookup`-vs-`Recipe.TryGetRow(itemId)` API ambiguity entirely (the Phase-1 research's `TryGetRow` example was wrong — Recipe is keyed by RecipeId, not ItemId). 2,700-row scan is trivially cheap; first-write-wins resolves the rare case of multiple recipes producing the same item.

### Tried and abandoned

- **`Action<string>` from `IPluginLog.Warning` method group** — failed compilation because `Warning` has many overloads. Wrapped in a `msg => Log.Warning(msg)` lambda. Tiny, but worth noting because it's the same shape as the first-build issues on the previous two plugins.
- **Top-level statements before class declaration in the scratch test program** — C# requires the class declarations after top-level statements. One-shot fix.

### Gotchas (added to `Gaming Tools/CLAUDE.md`)

- `AgentGrandCompanySupply` agent pointer is null until Personnel Officer OR Timers panel is opened; `UIState.GCSupply` is the persistent backing buffer but its layout isn't documented yet.
- `Recipe` is keyed by `RecipeId`, NOT `ItemId`. Use `RecipeLookup` or scan once into a map.
- `Item.GatheringItem.RowId != 0` is the leaf "is this gathered" check.
- Multi-addon `PostSetup` hooks against name candidates is the right pattern when FFXIVClientStructs doesn't annotate an addon's struct.

### Open threads

- **Timers addon name TBD** — in-game test should add a temporary log line in `Plugin.cs` to capture every `PostSetup` event, identify which candidate is the real Timers addon, and prune the dead ones in a v0.1.1 cleanup commit.
- **Vendor classification is weak** — currently uses `Item.ItemSearchCategory.RowId != 0` as the "is a vendor item" signal, which catches most cases but misses GC-seal-shop-only items. v0.2 idea: build a vendor-availability map.
- **v0.2 ambition**: reverse-engineer `UIState.GCSupply` (offset `0x10D28`, size `0x2C28`) so the plugin works without any in-game UI interaction.
- **In-game smoke test pending** — same constraint as every plugin in this monorepo. Build verification done on Mac (zero warnings, zero errors, 13/13 scratch checks pass).

---

## 2026-06-01 — WondrousTailsOdds: Best column = optimal 7-stamp targets (v0.1.3)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: pending — follow-on to e097af9 (v0.1.2)

### Changed

- **User feedback after v0.1.2 in-game test**: the binary `(100%) / (0%)` Best column was correct but coarse. The actionable mental model is "what's the highest P(≥N) any 7-stamp board could give me?", because stamp 7 is the last point at which Second Chance reshuffles still help — past 7, the remaining 2 stamps land uniformly at random and the player has no steering left. The aspirational target is therefore the **max over all 7-stamp configurations of P(≥N lines | board + 2 random stamps)**, computed once and shown as a fixed target.
- **Solver: new static-readonly constants**
  `LineProbability.OptimalSevenStampOneLine / TwoLines / ThreeLines`, computed in a `static` constructor that enumerates every 16-bit value with popcount 7 (C(16, 7) = 11,440 boards) and runs the 2-stamp continuation enumeration for each. Tracks the max of `AtLeast(1)`, `AtLeast(2)`, `AtLeast(3)` across all boards. Single pass. Takes ~50 ms on a Mac at first access; happens once at plugin load.
- **Solver math (now verified end-to-end via scratch test)**: Best P(≥1) = 100%, Best P(≥2) = 100%, Best P(≥3) = exactly 1/12 ≈ 8.3333%. The 1/12 falls out of the **row + diagonal** pattern (e.g. row 0 ∪ main diag = {0, 1, 2, 3, 5, 10, 15}), which puts 2 lines on the board and leaves three distinct 2-cell pairs ({9,13}, {6,14}, {7,11}) that each complete a third line — 3 disjoint events over C(9, 2) = 36 total 2-stamp draws.
- **Result struct trimmed**: dropped `BestOneLine` / `BestTwoLines` / `BestThreeLines` (the binary reachability indicators from v0.1.2). The information they carried collapses cleanly into `MaxLineCount`, which is still surfaced in the overlay footer as "Max achievable: N line(s)".
- **Overlay column semantics**:
  - **Best** column now reads the static targets directly (`LineProbability.OptimalSevenStampOneLine` etc), not per-board values. Rendered in dimmed text since the targets are fixed.
  - Removed the `(100%)` parentheses styling — column separation makes them redundant and the targets aren't binary any more.
  - **Current** colour now compares to Best instead of to the shuffle baseline. Matching-or-exceeding = green, within ~80% = yellow, below = red, zero = dark red. A target of 0 collapses to neutral white.
  - Footer gained a third explainer line: `Best = optimal 7-stamp target`.

### Decisions

- **Static constructor over hardcoded constants.** The three values *could* be baked in as numeric literals — but doing the computation explicitly in code keeps the derivation reviewable and immune to silent drift if `BingoBoard.MaxStamps` or the line set ever changed. 50 ms at startup is a fine price for that clarity.
- **Best column dimmed, Current coloured.** Best is a static reference; Current is where you are. The colour budget is best spent on the dynamic column. Avoids the "two equally-shouting columns" visual problem.
- **Current ratio vs Best, not vs ShuffleBaseline.** v0.1.2 coloured Current by ratio to ShuffleBaseline ("am I above the long-run average?"). User's mental model in v0.1.3 is "am I on track to hit the optimal 7-stamp number?" — same comparator should drive the colour. ShuffleBaseline is still shown below the table for the supplementary "luck" question.
- **`MaxLineCount` stays in the footer.** It answers a different question ("is N still mathematically reachable from here?") and complements the Best target nicely. Best tells you what an optimal player would have; Max tells you what you yourself can still get.

### Tried and abandoned

- **Keeping the binary Best alongside the new target Best.** Considered surfacing both ("(reachable) (target)") but it'd double the cognitive load of the column for marginal value. `MaxLineCount` in the footer covers reachability already.

### Gotchas (added to `Gaming Tools/CLAUDE.md`)

- Static-readonly precomputed targets initialised in a `static` constructor are lazy — first access pays the cost. ~50 ms here, fine; if it ever bites a hot path, move the precomputation into `Plugin()` so it happens before `WindowSystem.Draw` ticks.

### Open threads

- **In-game eyeball check on v0.1.3 pending.** Logic-side everything's verified, but the Current-vs-Best colour banding wants seeing on a few real boards to confirm the green/yellow/red breakpoints match intuition.
- Possibility for v0.1.4: surface the canonical "row + diagonal" shape diagram in the overlay so the player can *see* what they're aiming for, not just the number. Trade-off is overlay real-estate.

---

## 2026-06-01 — WondrousTailsOdds: floating window overlay + best-achievable column (v0.1.2)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: pending (this session) — follow-on to 98f54ab (v0.1.1)

### Changed

- **User feedback after v0.1.1 in-game install**: the native TextNode rendered at the very top of the WeeklyBingo addon (`Position = (20, 12)`) clipped the addon's existing title bar — unreadable. User asked for a separate floating window anchored to the addon's left edge, plus a "best possible" column next to the current probabilities. Both shipped in v0.1.2.
- **Dropped KamiToolKit entirely.** With the rendering moving to ImGui, we no longer need the native-node toolkit:
  - Removed `KamiToolKit` and `Microsoft.Extensions.ObjectPool` `PackageReference`s from the csproj.
  - Removed `KamiToolKit.dll` and `SixLabors.ImageSharp.dll` from the CI zip step (Dalamud bundles ObjectPool itself, so it never needed shipping; the other two were only present because KTK pulled them).
  - Removed the `KamiToolKitLibrary.Initialize(PluginInterface)` / `Dispose()` bootstrap from `Plugin.cs`.
- **`UI/AddonWeeklyBingoOverlay.cs` rewritten end-to-end.** Now extends Dalamud's `Window` rather than wrapping a KamiToolKit `AddonController`. `DrawConditions()` checks `IGameGui.GetAddonByName("WeeklyBingo")` for liveness; `PreDraw()` reads `addon->X / addon->Y` and calls `ImGui.SetNextWindowPos` so the overlay follows the journal when the user drags it. `IsOpen = true` and `RespectCloseHotkey = false` keep the window passively visible whenever the addon is.
- **Solver gained `MaxLineCount`.** `LineProbability.Result` now exposes the maximum line count achievable across every enumerated future-board, plus three convenience properties `BestOneLine` / `BestTwoLines` / `BestThreeLines` that collapse it to per-threshold 0/1 probabilities. The "Current/Best" table renders Best as `(100%)` / `(0%)` in green/dark-red.
- **`Plugin.cs` swapped to the standard Dalamud `WindowSystem` pattern**: `windows.AddWindow(overlay)` + `PluginInterface.UiBuilder.Draw += windows.Draw`. Dropped `IFramework` (no longer needed — the WindowSystem callback already runs on the framework thread). Added `IGameGui`. `/wts` no longer needs `ForceRefresh` because `DrawConditions()` is consulted every frame.
- **Manifest bumped to 0.1.2.0**, csproj `<Version>` to 0.1.2.
- **CI workflow zip step trimmed** to just the four plugin files (`WondrousTailsOdds.{dll,json,deps.json,pdb}`). The shipped zip dropped from ~954KB to roughly the plugin's own ~30KB.

### Decisions

- **ImGui Window over a redesigned native node.** Even if we tuned the KamiToolKit text node's `Position` to clear the addon's title bar, the readability would still be cramped against the journal art. A floating window with a proper background, table layout, and follow-the-addon positioning is a strict UX win and a strict dependency win.
- **`MaxLineCount` exposed as a single integer** plus three derived boolean-style properties. The integer is the more honest representation (it carries information the booleans don't — "max is 3" tells you 3 lines are achievable; "BestThreeLines = 1.0" doesn't tell you 4 is impossible because 4 is *always* impossible at 9 stamps). Surface both: the integer in the window footer ("Max achievable: 3 line(s)"), the booleans next to the per-threshold rows.
- **No new InternalName change.** v0.1.1's `WondrousTailsOdds` rename remains correct; v0.1.2 is just an internals refactor + feature add. Subscribers update in place.
- **`Window.AlwaysAutoResize` + `ImGuiCond.Always` on SetNextWindowSize** to fight ImGui's "remember last user resize" — the overlay is positioned every frame, so we want size + position to be fully ours, every frame.
- **`Window.IsOpen = true` permanently**, gating visibility via `DrawConditions()`. The alternative — flipping `IsOpen` from `IAddonLifecycle` events — would have worked but adds an extra service dependency and a redundant state machine.

### Tried and abandoned

- **First-pass cast `(AtkUnitBase*)gameGui.GetAddonByName(...)`** failed compilation because modern Dalamud wraps the return in `AtkUnitBasePtr`. Fixed to `(AtkUnitBase*)gameGui.GetAddonByName(...).Address`. Now documented in CLAUDE.md gotchas — the older d17 plugin examples online still show the direct cast.

### Gotchas (added to `Gaming Tools/CLAUDE.md`)

- `IGameGui.GetAddonByName` returns `AtkUnitBasePtr` in modern Dalamud, not the raw pointer. Use `.Address` and cast.
- Anchoring an ImGui window to a game addon: extend `Window`, check `addon != null && addon->IsVisible` in `DrawConditions`, `ImGui.SetNextWindowPos(addon->X, addon->Y)` in `PreDraw`. `ImGuiCond.Always` on `SetNextWindowSize` is required to override the user-resize cache.
- The Dalamud WindowSystem callback runs on the framework thread — dereferencing FFXIVClientStructs pointers from `Draw`/`PreDraw`/`DrawConditions` is safe without `Framework.RunOnFrameworkThread`.
- The KamiToolKit notes in CLAUDE.md are now **historical** but kept verbatim — any future plugin that genuinely needs native-node rendering will hit the same surface.

### Open threads

- **No in-game v0.1.2 test yet from this session** — user will refresh the custom-repo row and re-verify. The native-node clipping is fixed by construction (we don't render a native node any more); the floating-window positioning needs eyeballing for the right `MarginFromAddon` and `WindowWidth`.
- Window position will be off-screen if the WeeklyBingo addon is dragged hard against the left edge of the viewport — `MathF.Max(0, ...)` clamps to x=0 but the overlay would then overlap the addon. Acceptable for v0.1.2; a "snap to right side instead if no room on left" branch is a v0.2 idea if anyone hits it.
- Solver scratch test (`MaxLineCount`) now covers 11 known boards; ran clean off-tree in /tmp.

---

## 2026-06-01 — WondrousTailsSolver -> WondrousTailsOdds rename (v0.1.1)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: pending (this session) — follow-on to 4be2cfa (v0.1.0)

### Changed

- **In-game install failed.** User subscribed to the v0.1.0 repo and could only see the d17-stable Wondrous Tails Solver, not ours. User correctly diagnosed it as a name/tag collision.
- **Root cause investigation revealed v0.1.0's whole premise was partly wrong.** `MidoriKami/EzWondrousTails` is archived, but the plugin itself was never housed there — it lives at `MidoriKami/WondrousTailsSolver` and is **actively maintained in the official d17 stable channel** under InternalName `WondrousTailsSolver`, owners daemitus / MidoriKami / nathanctech, version 3.2.2.6 as of this session. Confirmed by fetching `https://raw.githubusercontent.com/goatcorp/DalamudPluginsD17/main/stable/WondrousTailsSolver/manifest.toml`. Dalamud always prefers d17 over custom repos for the same InternalName, so our v0.1.0 was invisible.
- **User chose to keep ours as a personal alternative** rather than revert. Renamed to avoid the collision:
  - `WondrousTailsSolver.json` → `WondrousTailsOdds.json` (git mv, history preserved).
  - Manifest `Name`: "Wondrous Tails Solver" → "Wondrous Tails Odds". `InternalName`: "WondrousTailsSolver" → "WondrousTailsOdds". `AssemblyVersion`: 0.1.0.0 → 0.1.1.0. `Description` and `Punchline` updated to mention this is a personal alternative to the d17 plugin. `Tags` lost "utility", gained "odds" for distinctiveness in installer search.
  - `WondrousTailsSolver.csproj` `<AssemblyName>` → `WondrousTailsOdds`, `<Version>` → 0.1.1, `<None Update="*">` path updated. `<RootNamespace>` kept as `WondrousTailsSolver` (purely internal, no value in renaming).
  - `Plugin.cs:23` `Name` property → "Wondrous Tails Odds".
  - `.github/workflows/build-wondroustailssolver.yml`: workflow display name, `MANIFEST_PATH`, `MANIFEST=`, zip step file list, commit message all switched to WondrousTailsOdds. Path filter and GH Pages subpath kept as `wondroustailssolver` so existing subscribers don't see a broken URL.
  - `FF14/WondrousTailsSolver/README.md` rewritten with the new name, a section explaining the relationship to the d17 plugin, and a note about the deliberate naming disconnect between repo paths and shipping artefacts.
- `Gaming Tools/CLAUDE.md` gained a new "Plugin naming and InternalName collisions" gotcha section under the KamiToolKit one, with the d17 check command for future plugins.
- Workspace ADR-0001 (`docs/adr/0001-dalamud-plugin-distribution-pattern.md`) gained an addendum recording the lesson.

### Decisions

- **Keep RootNamespace and source-folder name unchanged.** Renaming `RootNamespace WondrousTailsSolver` would require a sweep across every `.cs` file with no user-facing benefit; the namespace is purely internal. Same for the csproj filename, the source folder, and the GH Pages subpath — all of those are dev / URL artefacts that nobody but the maintainer reads.
- **Keep subscribe URL path as `ff14/wondroustailssolver/`** so the URL we already gave the user remains valid. The path is just a GH Pages folder; the InternalName mismatch is harmless.
- **Picked `WondrousTailsOdds` over alternatives** (WondrousTailsHelper, WondrousTailsAssistant, WTSolverV2, KhloeAssistant). Short, accurate ("odds" is exactly what the plugin computes), no whitespace/casing surprises, clearly distinct from the d17 entry in search.
- **Did not revert v0.1.0.** User explicitly chose to keep this as a personal alternative — useful as a sandbox for future iteration without coordinating with the upstream's release cadence.

### Tried and abandoned

- **Considered renaming the source folder + csproj filename + RootNamespace** for full consistency. Rejected: zero user-facing benefit, large diff, breaks `git log --follow` continuity for every source file. The naming disconnect is documented instead.

### Gotchas (added to `Gaming Tools/CLAUDE.md`)

- Check the d17 stable channel for InternalName collisions before shipping any plugin: `curl -fI https://raw.githubusercontent.com/goatcorp/DalamudPluginsD17/main/stable/<Name>/manifest.toml` — 200 means collision, 404 means clear.
- An archived repo doesn't mean a dead plugin — the canonical home may be elsewhere.
- AssemblyName + InternalName + manifest filename all rename together. RootNamespace, source folder, csproj filename, GH Pages subpath can stay.
- `Plugin.Name` is user-facing and distinct from `InternalName`.

### Open threads

- **User needs to refresh the custom-repo row in Dalamud** to pick up the new pluginmaster.json (with the WondrousTailsOdds InternalName) after CI ships v0.1.1. The previous v0.1.0 entry will simply disappear since nothing was ever installed.
- v0.1.0 in-game test never happened (the collision prevented install). v0.1.1 in-game test is the real first contact.

---

## 2026-06-01 — Bootstrap WondrousTailsSolver plugin (v0.1.0)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: pending (user will commit + push after local build verification)

### Changed

- Added a second plugin to the monorepo: `FF14/WondrousTailsSolver/`. Clean-room reimplementation of the archived `MidoriKami/EzWondrousTails`. Overlays row/column/diagonal completion probabilities onto the in-game Wondrous Tails journal.
- New files:
  - `FF14/WondrousTailsSolver/WondrousTailsSolver.csproj` — mirrors BulkDesynth's TFM + ref pattern; adds `KamiToolKit` 1.1.0 NuGet PackageReference.
  - `FF14/WondrousTailsSolver/WondrousTailsSolver.json` — manifest, `DalamudApiLevel: 15`, `AssemblyVersion: 0.1.0.0`.
  - `Plugin.cs`, `Configuration.cs` (one toggle: `ShowOverlay`).
  - `Solver/BingoBoard.cs` — pure 4x4 + 10 lines bitmask model.
  - `Solver/LineProbability.cs` — exact enumeration of every k-subset (≤ C(16,9) = 11,440 boards). Current + shuffle-baseline probabilities for P(≥1), P(≥2), P(≥3) lines. No Monte Carlo — exact enumeration is faster and deterministic.
  - `Services/BingoStateReader.cs` — wraps `PlayerState.IsWeeklyBingoStickerPlaced(int)` and `HasWeeklyBingoJournal` to produce a 16-bit mask or null.
  - `UI/AddonWeeklyBingoOverlay.cs` — `AddonController<AddonWeeklyBingo>` from KamiToolKit; attaches a `TextNode` to the addon on PostSetup, refreshes on PostRefresh + PostUpdate, detaches on PreFinalize.
  - `LICENSE` (MIT), per-plugin `README.md`.
  - `.github/workflows/build-wondroustailssolver.yml` — clone of `build-bulkdesynth.yml` with path filter on this plugin's subtree. Also lists `KamiToolKit.dll` in the zip step since it isn't bundled by Dalamud.
  - `docs/ff14/wondroustailssolver/pluginmaster.json` — placeholder `[]` so CI's first-build change-detection sees a tracked file.
- Edited:
  - `Gaming Tools/CLAUDE.md` — snapshot lists both plugins; new Subscribe URL; new "KamiToolKit" gotcha section under Plugin invocation patterns; updated local build snippet.
  - `Gaming Tools/README.md` — second row in Plugins table; updated layout diagram; per-plugin docs link.
  - `~/Claude Projects/docs/PROJECT_INDEX.md` — `gaming-tools` one-liner now mentions both plugins.
  - `~/Claude Projects/docs/ADR_INDEX.md` — entry for new workspace-level ADR.
  - New `~/Claude Projects/docs/adr/0001-dalamud-plugin-distribution.md` — workspace-level ADR documenting the `gaming-tools` monorepo + GitHub Pages + per-plugin subpath pattern. First Dalamud-specific ADR in the portfolio.

### Decisions

- **Clean-room over fork**: the upstream `MidoriKami/EzWondrousTails` has no `LICENSE` file. Reimplementing from public game-data documentation under MIT removes licence ambiguity and gives us a SPDX header for the Dalamud d17 registry if we ever publish there.
- **Exact enumeration over Monte Carlo**: the upstream plugin used 500-iteration Monte Carlo for the shuffle baseline. Exact enumeration of C(16, 9) = 11,440 boards runs in microseconds and is deterministic. Dropped the `MonteCarloIterations` config field that the plan originally proposed — YAGNI.
- **KamiToolKit via NuGet, not submodule**: archived upstream broke partly because its `..\KamiToolKit\KamiToolKit.csproj` relative project reference died when the submodule was removed. Pinning to NuGet 1.1.0 sidesteps that footgun.
- **Single overall TextColor instead of per-substring colouring**: original plugin coloured each probability cell independently (bright/green/yellow/red/dark-red). v0.1 picks one colour for the whole overlay based on the most generous threshold's ratio-to-shuffle-baseline. Simpler ReadOnlySeString construction; can be upgraded to per-substring colour payloads in v0.2 if it's missed.
- **Hide via `IsVisible`, don't detach** when the user toggles the overlay off. Detaching mid-addon-lifetime would defeat the AddonController's setup/finalize symmetry and risk leaking the node.

### Tried and abandoned

- **Reusing the existing EzWondrousTails source under fair use / no-licence-implied-permission** — rejected because it's both legally murky and unnecessary (codebase is small).
- **Per-probability colour payloads in v0.1** — postponed. Lumina's `SeStringBuilder` + Dalamud's `Edge` text flag give a perfectly readable headline; per-substring colour adds SeString payload plumbing we don't need for parity.

### Gotchas (now in `Gaming Tools/CLAUDE.md`)

- KamiToolKit bootstrap is one call: `KamiToolKitLibrary.Initialize(PluginInterface)`. Internal `Services` class auto-injects.
- `AddonController<T>` properties (`AddonName`, `OnSetup`, ...) are **init-only**. Use an object initialiser, not assignments.
- `AddonController<T>.Enable()` asserts main thread. Wrap in `Framework.RunOnFrameworkThread(...)` if you construct from anywhere other than the plugin constructor.
- `KamiToolKit.dll` must be **bundled in the shipped plugin zip** — Dalamud doesn't resolve it for you.
- `TextNode.String` is `Lumina.Text.ReadOnly.ReadOnlySeString`. Use `new SeStringBuilder().Append(...).ToReadOnlySeString()`. Plain `string` does **not** implicitly convert.
- Per-cell visibility: `NodeBase.IsVisible` (and `Position`, `Size`) live on the base class, not on `TextNode` directly. Documented inheritance trips you up otherwise.

### Open threads

- **In-game verification pending**: the `WeeklyBingo` addon's node tree may need a more specific parent than the root for the overlay to sit neatly below the bingo grid. Current code attaches `AsLastChild` of the root addon at `Position = (20, 12)`. If the overlay collides with existing in-game elements, we'll need to walk to a more stable inner node by `NodeType` rather than hard-coded `NodeID`.
- **No `/wts settings` UI**: only the `/wts` on/off toggle exists. If we want to tweak Monte Carlo iteration count, font size, colour thresholds, etc., we'll need an ImGui settings window. None of that is needed for v0.1 parity.
- **Local Mac build verification**: `dotnet 10.0.203` SDK is present at `~/.dotnet/dotnet`. Build runs after committing — CI is the source of truth either way.

---

## 2026-05-20 — BulkDesynth iteration: v0.3.0 → v0.5.0 (UI polish, live preview, visible bag positions)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: 37018cd, 6ee816e, eea362d, 6f5b20d, 46022ce, df600cd, d68414d, 6c7910a

### Changed

- **v0.3.0** (37018cd) — UI polish on top of v0.2.0:
  - Filter rows compressed to three horizontal pairs (name+id, min+max ilvl as typed `InputInt`, spiritbond+HQ).
  - Spiritbond cap corrected from 0-1000 to 0-100 (matches the in-game percentage display; the previous range was based on a stale field-unit assumption).
  - Killed the dedicated "Run" tab; status (`Processed: N | Remaining: N | [Stop]`) and current-item line fold into the Targeting tab below the filter rows.
  - `DesynthExecutor` switched from `Queue<DesynthCandidate>` to `List<DesynthCandidate>` + `IReadOnlyList<DesynthCandidate> RemainingItems`. While a Bulk Desynth is running, `MainWindow` renders that list so each successful desynth visibly removes a row.
- **v0.4.0** (eea362d) — Preview "Container" + "Slot" columns collapsed into one "Location" column rendered as `Bag {N} ({row},{col})` (5 cols × 7 rows per page). Internal → visual map built once per scan from `ItemOrderModule.InventorySorter`. Armoury items fall back to `{Container} slot {N}` since the armoury isn't in the InventorySorter.
- **v0.4.1** (df600cd) — After a Bulk Desynth ended, the table re-displayed the pre-run snapshot (executor copies the preview, the UI was still holding its own reference). Now we drop the preview reference at `executor.Start()` time so the table is correctly empty post-run.
- **v0.5.0** (6c7910a) — Label casing + wording cleanup. "Armoury Chest", "All Bags", "No Bags", "Bags + Armoury", "Max Spiritbond %", "Clear Preview", "Build preview" → "Desynth Preview". "Min ilvl"/"Max ilvl" kept lowercase per community notation. All "run" terminology replaced with "Bulk Desynth" in user-facing strings ("Stop Bulk Desynth", "Per-Bulk-Desynth Hard Cap", etc.). New post-completion summary line ("Last Bulk Desynth: N item(s) processed.") edge-detected on `wasRunning` → `!IsRunning` transition.

### Decisions

- **InventorySorter inverse-map** built once per scan rather than looked up per-candidate or per-frame. The map is a `Dictionary<(InventoryType, short), (byte bag, byte slot)>`. Cached on the `DesynthCandidate` itself so the UI doesn't need a sorter pointer at draw time. Cross-referenced `SimpleTweaksPlugin/EquipFromHotbar.cs` to confirm the lookup direction.
- **Post-run summary via edge detection** (`bool wasRunning` field, populate `lastRunSummary` when previous tick was running and current isn't). Alternative was an executor event/callback; edge detection in the UI is simpler since there's only one consumer.
- **Drop preview on `Start`** rather than diffing two snapshots later. Executor has already copied the list; holding the original UI-side just causes the post-run flashback bug.

### Tried and abandoned

- None this session. v0.4.1's "blank preview after run" bug was a straight fix; no rejected alternative.

### Gotchas (now in CLAUDE.md)

- `StdVector<T>` indexer takes **`long`**, not `ulong` or `int`. Caught by CI on the first v0.4.0 push; trivial fix but easy to repeat.
- `ItemOrderModule.InventorySorter->Items[i]` is indexed by **visual** position; each entry holds the **internal** Page+Slot. Invert if you need internal → visual (which is the common direction for "where is this item visually?").
- `InventoryItem.SpiritbondOrCollectability` range is 0-100. Initial 0-1000 ceiling was a guess that survived until user testing flagged it.

### Open threads (unchanged from bootstrap)

- Block / allow list editing has no UI surface yet. Fields exist in `Configuration.cs` but can only be edited by hand-editing the serialised config JSON.
- "Skip if equipped" / "skip if armoury duplicate" filters not implemented.
- `ImRaii` helper in `Windows/MainWindow.cs` is still rolled by hand. If `Dalamud.Interface.Utility.Raii.ImRaii` is stable on API 15+, we can delete the local one.
- `AddonWaitTimeoutMs` default is 3000ms. May want to raise to 5000ms if anyone hits "item never went busy, skipping" warnings under network lag — Settings slider already exists.

### Resolved threads

- Armoury chest path is now **confirmed end-to-end** on live client (was the open thread in the bootstrap entry).

---

## 2026-05-20 — Bootstrap monorepo + BulkDesynth plugin (v0.1.0 → v0.2.0)

**Agent**: claude-opus-4-7
**Branch**: main | **Commits**: 02d2173, 247e36f, eae43df, 0b8eb6e, d0bb3db, ce00acf, 3997db7, acf4f7d

### Changed
- Created `gaming-tools` monorepo: root `README.md`, `.gitignore`, `.github/workflows/build-bulkdesynth.yml`.
- Wrote BulkDesynth from scratch under `FF14/BulkDesynth/`: scanner, executor (state machine over `IFramework.Update`), ImGui window, configuration, manifest, csproj.
- Pushed to `https://github.com/jdw-ss/gaming-tools`, enabled GitHub Pages from `/docs` on `main` via the GitHub API.
- v0.2.0: collapsed v0.1.0's scope-radio UX into a single Targeting tab with multi-select bag checkboxes + always-visible filter parameters. Added `DesynthFilter.NameContains` (case-insensitive substring against `Lumina.Item.Name.ExtractText()`) so users don't have to know item row IDs.

### Decisions
- **Desynth invocation**: `AgentSalvage.Instance()->SalvageItem(InventoryItem*)` followed by `agent->AgentInterface.ReceiveEvent(&retval, [Int 0, Bool 1], 2, 1)`. Pattern verified against three independent reference plugins (AutoRetainer's `TaskDesynthItems`, SomethingNeedDoing's `InventoryModule`, ffxiv-bundleoftweaks). The `Bool=1` skips SelectYesno.
- **Pacing**: gate on `ICondition[ConditionFlag.Occupied39]` (game's own busy flag during cast + animation) plus a configurable post-cast cooldown. No addon polling.
- **HQ pre-filter as safety**: because `Bool=1` bypasses the game's HQ/spiritbond warning popup, the scanner defaults to `ExcludeHq = true` and exposes a `MaxSpiritbond` slider. Dry-run preview remains the universal safety net.
- **Distribution**: dedicated GitHub Pages repo per the user's preference (rejected option to share Firebase hosting with the tracker plugin). Subscribe URL: `https://jdw-ss.github.io/gaming-tools/ff14/bulkdesynth/pluginmaster.json`. Workflow builds the URL from `github.repository` so a repo rename / transfer Just Works.
- **`<AssemblyVersion>` and `Version`** are kept in sync between `BulkDesynth.json` and `BulkDesynth.csproj`. CI reads from the JSON manifest.

### Tried and abandoned
- **Elaborate addon-callback state machine** (FireBegin → WaitYesnoOrResult → FireYes → WaitResult → CloseResult). Replaced with the canonical `SalvageItem + ReceiveEvent` pattern after verifying via the three reference plugins. Addon callback indices weren't in any XML doc and would have been brittle across game patches.
- Hosting BulkDesynth alongside the tracker plugin under Firebase. Decided against: independent release cycles, Firebase project ownership is achievement-tracker-scoped.

### Gotchas (added to CLAUDE.md)
- Quest IDs above 65535 must be `uint` literals in C# — `QuestManager.IsQuestComplete(ushort)` has a `uint` overload that masks `& 0xFFFF` (so `65688` → native quest 152, correctly).
- `InventoryContainer` bool field is `IsLoaded`, not `Loaded`.
- ImGui binding in modern Dalamud is `Dalamud.Bindings.ImGui.dll`, not `ImGui.NET.dll`. The namespace and DLL name both changed.
- `InventoryItem.SpiritbondOrCollectability` (not `Spiritbond`).
- **`--` inside XML comments is illegal**. csproj `<!-- ... -->` blocks must paraphrase any literal CLI examples (`dotnet --list-sdks`, `-p:Foo=Bar`).
- CI gotcha: `git diff --quiet <path>` returns clean for **untracked** files. Stage first, then `git diff --cached --quiet` to detect changes including brand-new paths.

### Open threads
- Armoury chest desynth path is wired in the scanner + UI but **not tested end-to-end in-game** (user has run main-bag desynth successfully). Next session: try `Bags + armoury` quick button on a sacrificial gear set.
- Block / allow list editing is exposed in `Configuration.cs` but has no UI surface yet; only hand-editing the serialised config works.
- "Skip if equipped" / "skip if in armoury duplicate" filters not implemented.
- The `ImRaii` helper in `Windows/MainWindow.cs` is rolled by hand. If Dalamud's bundled `Dalamud.Interface.Utility.Raii.ImRaii` is stable enough on API 15+, we could delete this helper class.
