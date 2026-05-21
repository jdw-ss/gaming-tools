# Session Log — Gaming Tools (monorepo)

Append-only, newest entries on top. Format defined in `~/Claude Projects/docs/DOCUMENTATION_PYRAMID.md` → `<project>/SESSION_LOG.md`.

Write an entry at the end of any non-trivial session (anything that produced commits, decisions, abandoned approaches, or mid-flight work). Skip for pure read-only / Q&A / typo-fix sessions.

When adding a new plugin to the monorepo, prefer one entry covering the whole bootstrap rather than per-file entries.

---

<!-- New entries go directly below this line -->

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
