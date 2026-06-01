# Gaming Tools (monorepo)

## Snapshot

Publishing-infrastructure monorepo for FFXIV Dalamud plugins. Each plugin lives in its own subfolder; CI builds the plugin and publishes the `.zip` + `pluginmaster.json` to GitHub Pages under a dedicated subpath so users can subscribe per-plugin. Currently active plugins: **BulkDesynth**, **WondrousTailsSolver**. Status: **live**.

## Stack

- **Language**: C# (.NET 10.0 Windows, `EnableWindowsTargeting` so it cross-compiles on Mac/Linux)
- **Framework**: Dalamud plugin API
- **Hosting**: GitHub Pages (serves `docs/`)
- **CI**: GitHub Actions (`.github/workflows/build-bulkdesynth.yml` etc.)

## Run locally

Plugin builds run on CI, not locally. Local C# builds require:

```bash
dotnet build FF14/BulkDesynth/BulkDesynth.csproj -c Release
dotnet build FF14/WondrousTailsSolver/WondrousTailsSolver.csproj -c Release
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
- **Subscribe URL for WondrousTailsSolver**: `https://jdw-ss.github.io/gaming-tools/ff14/wondroustailssolver/pluginmaster.json`

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

### KamiToolKit (Wondrous Tails native overlay)

- KamiToolKit is consumed as a **NuGet PackageReference** (`KamiToolKit` 1.1.0+), never as a git submodule. The archived upstream `EzWondrousTails` broke partly because its `..\KamiToolKit\KamiToolKit.csproj` relative reference died when the submodule was dropped. Pinning to NuGet sidesteps that footgun entirely.
- Bootstrap from the host plugin with **one call**: `KamiToolKitLibrary.Initialize(PluginInterface)` in the plugin constructor; `KamiToolKitLibrary.Dispose()` in `Dispose()`. The library's internal `Services` class is `[PluginService]`-injected from inside KamiToolKit — the host doesn't need to expose `IGameGui`, `IAddonLifecycle`, etc. just for KTK.
- `AddonController<T>` uses **init-only** property setters (`AddonName`, `OnSetup`, `OnFinalize`, `OnRefresh`, `OnUpdate`). Build with an object initialiser, then call `.Enable()` on the framework thread. `Enable()` asserts main thread and will throw if called from a background thread.
- The KamiToolKit DLL must be **bundled in the shipped plugin zip** alongside the host DLL — Dalamud doesn't resolve KamiToolKit for you. So must `SixLabors.ImageSharp.dll`, which KamiToolKit pulls in transitively and which Dalamud does NOT bundle. `Microsoft.Extensions.ObjectPool.dll` IS bundled by Dalamud — don't ship a second copy or you risk version conflicts. To check what's bundled: extract `goatcorp/dalamud-distrib/latest.zip` and look in the resulting folder. `build-wondroustailssolver.yml` lists the must-ship DLLs explicitly in the zip step.
- `TextNode.String` is `Lumina.Text.ReadOnly.ReadOnlySeString`. Build text with `new SeStringBuilder().Append(...).ToReadOnlySeString()` (from `Lumina.Text`). Plain `string` does not implicitly convert.
- `Position`, `IsVisible`, `Size`, `TextColor`, `TextOutlineColor`, `FontSize`, `AlignmentType` live on `NodeBase` (and thus `TextNode`). Use `IsVisible` to hide rather than detach when the user toggles the overlay off — detaching mid-addon-lifetime defeats the controller's lifecycle assumptions.

### Plugin naming and InternalName collisions

- **Check the d17 stable channel for InternalName collisions BEFORE shipping** any plugin whose subject overlaps an existing community plugin. Dalamud always prefers d17 over custom repos for the same `InternalName`, so a custom-repo plugin with a colliding name is invisible in the installer. Check by fetching `https://raw.githubusercontent.com/goatcorp/DalamudPluginsD17/main/stable/<CandidateInternalName>/manifest.toml` — if it returns 200, you have a collision and must pick a different name.
- **An archived repo doesn't mean a dead plugin.** `MidoriKami/EzWondrousTails` was archived but the plugin itself lives on at `MidoriKami/WondrousTailsSolver` under InternalName `WondrousTailsSolver` and is actively maintained in d17 stable. WondrousTailsOdds (this monorepo's second plugin) collided on the original name and had to be renamed in v0.1.1.
- **Renaming after first ship is cheap but not free.** AssemblyName + InternalName + manifest filename must all change together. The csproj `<None Update="*.json">` block, the `MANIFEST_PATH` and `MANIFEST=` cat in the CI workflow, and the zip step's file list all need updating. RootNamespace, source folder, csproj filename, and GH Pages subpath can stay (and did, for WondrousTailsOdds — the disconnect is purely cosmetic and documented in the relevant READMEs).
- **`Plugin.Name` is user-facing**, distinct from `InternalName`. Keep them consistent in spirit so users searching the installer find what they expect, but they don't have to be byte-identical.
