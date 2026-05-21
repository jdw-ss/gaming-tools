# Gaming Tools (monorepo)

## Snapshot

Publishing-infrastructure monorepo for FFXIV Dalamud plugins. Each plugin lives in its own subfolder; CI builds the plugin and publishes the `.zip` + `pluginmaster.json` to GitHub Pages under a dedicated subpath so users can subscribe per-plugin. Currently active plugin: **BulkDesynth**. Status: **live**.

## Stack

- **Language**: C# (.NET 10.0 Windows, `EnableWindowsTargeting` so it cross-compiles on Mac/Linux)
- **Framework**: Dalamud plugin API
- **Hosting**: GitHub Pages (serves `docs/`)
- **CI**: GitHub Actions (`.github/workflows/build-bulkdesynth.yml` etc.)

## Run locally

Plugin builds run on CI, not locally. Local C# builds require:

```bash
dotnet build FF14/BulkDesynth/BulkDesynth.csproj -c Release
```

There is no local UI to preview.

## Cloud infrastructure

- **GitHub Pages** — serves `docs/`
- **GitHub Actions** — CI per plugin

## Schedules

None.

## External connections

None (build-time only).

## Deploy

- **Push to `main`** → CI builds → commits `latest.zip` + `pluginmaster.json` under `docs/<plugin-subpath>/` → GitHub Pages publishes.
- **Subscribe URL for BulkDesynth**: `https://jdw-ss.github.io/gaming-tools/ff14/bulkdesynth/pluginmaster.json`

## Companion docs

- `README.md` — monorepo overview + per-plugin subscribe URLs

## Related projects

- **`ffxiv-achievement-tracker`** — consumes the BulkDesynth plugin published from here; coordinate plugin version bumps and CI timing (consumer needs to `git pull` after plugin CI lands before `npm run build`)

See `~/Claude Projects/docs/PROJECT_INDEX.md` for the full cross-project map.

## Gotchas

### Monorepo / CI

- Each plugin needs its own subpath under `docs/` so subscribe URLs don't collide.
- Plugin version bumps live in **two places** per plugin: the `.csproj` `<AssemblyVersion>` and the JSON manifest. Both must move together or Dalamud refuses to update.
- Adding a new plugin: create `FF14/<NewPlugin>/`, add a matching `.github/workflows/build-<newplugin>.yml`, commit a placeholder `docs/<subpath>/pluginmaster.json`. Follow the BulkDesynth pattern.
- CI change-detection: `git diff --quiet <path>` returns clean for **untracked** files. Stage first, then check `git diff --cached --quiet` so the first build of a new plugin actually commits its artifacts.
- `--` (double dash) is **illegal inside XML comments**. csproj `<!-- ... -->` blocks must paraphrase any literal CLI examples (`dotnet --list-sdks`, `-p:Foo=Bar`).

### Dalamud / FFXIVClientStructs API

- The bundled ImGui binding is **`Dalamud.Bindings.ImGui.dll`** (namespace `Dalamud.Bindings.ImGui`), not `ImGui.NET.dll` / `ImGuiNET`. The DLL and namespace both changed in Dalamud 14+.
- `InventoryContainer` exposes `IsLoaded` (bool), not `Loaded` (int).
- `InventoryItem.SpiritbondOrCollectability` is the field name, not `Spiritbond`. Range is 0-100 (matches the in-game percentage display).
- `QuestManager.IsQuestComplete(ushort)` is the native function. A `uint` convenience overload masks `& 0xFFFF`, so a literal like `65688` resolves to native quest 152 — passing it via `uint` Just Works, but anyone passing it as `ushort` will hit an overflow error. Use `uint` constants for quest IDs above 65535.
- `StdVector<T>` indexer signature is `[long]`, not `[ulong]` or `[int]`. Cast or use a `long` loop variable.
- `ItemOrderModule.Instance()->InventorySorter` holds the player's customized visual inventory order. `sorter->Items[i]` is indexed by **visual** linear position; each entry's `Page` and `Slot` point at the **internal** location. Invert at runtime if you need internal → visual.

### Plugin invocation patterns

- Desynth invocation: `AgentSalvage.Instance()->SalvageItem(InventoryItem*)` followed by `agent->AgentInterface.ReceiveEvent(&retval, [Int 0, Bool 1], 2, 1)`. The `Bool=1` bypasses the SelectYesno warning dialog — pre-filter HQ / high-spiritbond items if you want the warning's safety semantics back.
- Cast pacing: gate on `ICondition[ConditionFlag.Occupied39]` (the game's busy flag during cast + animation). No need to poll addon visibility.
