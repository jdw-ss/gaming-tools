# Session Log — Gaming Tools (monorepo)

Append-only, newest entries on top. Format defined in `~/Claude Projects/docs/DOCUMENTATION_PYRAMID.md` → `<project>/SESSION_LOG.md`.

Write an entry at the end of any non-trivial session (anything that produced commits, decisions, abandoned approaches, or mid-flight work). Skip for pure read-only / Q&A / typo-fix sessions.

When adding a new plugin to the monorepo, prefer one entry covering the whole bootstrap rather than per-file entries.

---

<!-- New entries go directly below this line -->

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
