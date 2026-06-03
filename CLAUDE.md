# Gaming Tools (monorepo)

## Snapshot

Publishing-infrastructure monorepo for FFXIV Dalamud plugins. Each plugin lives in its own subfolder; CI builds the plugin and publishes the `.zip` + `pluginmaster.json` to GitHub Pages under a dedicated subpath so users can subscribe per-plugin. Currently active plugins: **BulkDesynth**, **WondrousTailsOdds** (source folder `WondrousTailsSolver/`), **GcSupplyHelper**. Status: **live**.

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
dotnet build FF14/GcSupplyHelper/GcSupplyHelper.csproj -c Release
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
- **Subscribe URL for WondrousTailsOdds**: `https://jdw-ss.github.io/gaming-tools/ff14/wondroustailssolver/pluginmaster.json`
- **Subscribe URL for GcSupplyHelper**: `https://jdw-ss.github.io/gaming-tools/ff14/gcsupplyhelper/pluginmaster.json`

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

### KamiToolKit (historical — removed from WondrousTailsOdds in v0.1.2)

The notes here document what we learned while integrating KamiToolKit. **WondrousTailsOdds dropped KamiToolKit in v0.1.2** in favour of a plain Dalamud ImGui `Window` anchored to the addon (cleaner, no extra DLLs to ship). Keep these notes because any future plugin that *does* need to draw native nodes into a game addon will run into the same surface.

- KamiToolKit must be consumed as a **NuGet PackageReference** (`KamiToolKit` 1.1.0+), never as a git submodule. The upstream `EzWondrousTails` broke partly because its `..\KamiToolKit\KamiToolKit.csproj` relative reference died when the submodule was dropped. NuGet sidesteps that footgun.
- Bootstrap from the host plugin with **one call**: `KamiToolKitLibrary.Initialize(PluginInterface)` in the constructor; `KamiToolKitLibrary.Dispose()` in `Dispose()`. The library's internal `Services` class is `[PluginService]`-injected from inside KamiToolKit — the host doesn't need to expose `IGameGui`, `IAddonLifecycle`, etc. just for KTK.
- `AddonController<T>` uses **init-only** property setters (`AddonName`, `OnSetup`, `OnFinalize`, `OnRefresh`, `OnUpdate`). Build with an object initialiser, then call `.Enable()` on the framework thread. `Enable()` asserts main thread.
- The KamiToolKit DLL must be **bundled in the shipped plugin zip** alongside the host DLL — Dalamud doesn't resolve KamiToolKit for you. So must `SixLabors.ImageSharp.dll`, which KamiToolKit pulls in transitively. `Microsoft.Extensions.ObjectPool.dll` IS bundled by Dalamud — don't ship a second copy or you risk version conflicts.
- `TextNode.String` is `Lumina.Text.ReadOnly.ReadOnlySeString`. Build text with `new SeStringBuilder().Append(...).ToReadOnlySeString()` (from `Lumina.Text`). Plain `string` does not implicitly convert.
- `Position`, `IsVisible`, `Size`, `TextColor`, `TextOutlineColor`, `FontSize`, `AlignmentType` live on `NodeBase` (and thus `TextNode`).

### Addon-anchored ImGui windows (the v0.1.2 WondrousTailsOdds pattern)

- **`IGameGui.GetAddonByName(name)` returns `Dalamud.Game.NativeWrapper.AtkUnitBasePtr`**, NOT a raw `AtkUnitBase*`. Cast with `(AtkUnitBase*)gameGui.GetAddonByName(name).Address`. Easy to miss because older docs / older d17 plugin code show the direct pointer return.
- **For a window that follows an addon**, extend Dalamud's `Window`, override `DrawConditions()` to check `addon != null && addon->IsVisible`, and set position from `addon->X / addon->Y` inside `PreDraw()` via `ImGui.SetNextWindowPos(...)`. This automatically tracks the addon being dragged. Use `AlwaysAutoResize` so the window snaps to its content rather than fighting your position.
- **`ImGuiCond.Always`** on `SetNextWindowSize` is what overrides ImGui's "remember last user resize" behaviour. Without it the first resize sticks.
- **The Dalamud `WindowSystem` runs on the framework thread**, so dereferencing `AtkUnitBase*` inside `Draw()` / `DrawConditions()` / `PreDraw()` is safe without `Framework.RunOnFrameworkThread` gymnastics.
- **`Window.RespectCloseHotkey = false` and `ShowCloseButton = false`** is the right config for a passive overlay (no Escape-to-close, no X button). Toggle visibility from a config flag the user's slash command flips.
- **Static-readonly precomputed targets** (`LineProbability.OptimalSevenStamp*`, v0.1.3) are initialised in a `static` constructor that enumerates ~11,440 boards × 36 continuations. Cost is ~50 ms at first access; happens once at plugin load. Acceptable, but be aware: type initialisation is lazy, so if you reference any LineProbability member during a hot path you'll pay that one-off cost there instead of at startup. If it ever matters, move the precomputation into `Plugin()` so it runs before `WindowSystem.Draw` ticks.

### Plugin naming and InternalName collisions

- **Check the d17 stable channel for InternalName collisions BEFORE shipping** any plugin whose subject overlaps an existing community plugin. Dalamud always prefers d17 over custom repos for the same `InternalName`, so a custom-repo plugin with a colliding name is invisible in the installer. Check by fetching `https://raw.githubusercontent.com/goatcorp/DalamudPluginsD17/main/stable/<CandidateInternalName>/manifest.toml` — if it returns 200, you have a collision and must pick a different name.
- **An archived repo doesn't mean a dead plugin.** `MidoriKami/EzWondrousTails` was archived but the plugin itself lives on at `MidoriKami/WondrousTailsSolver` under InternalName `WondrousTailsSolver` and is actively maintained in d17 stable. WondrousTailsOdds (this monorepo's second plugin) collided on the original name and had to be renamed in v0.1.1.
- **Renaming after first ship is cheap but not free.** AssemblyName + InternalName + manifest filename must all change together. The csproj `<None Update="*.json">` block, the `MANIFEST_PATH` and `MANIFEST=` cat in the CI workflow, and the zip step's file list all need updating. RootNamespace, source folder, csproj filename, and GH Pages subpath can stay (and did, for WondrousTailsOdds — the disconnect is purely cosmetic and documented in the relevant READMEs).
- **`Plugin.Name` is user-facing**, distinct from `InternalName`. Keep them consistent in spirit so users searching the installer find what they expect, but they don't have to be byte-identical.

### Plugin → web URL handoff (GcSupplyHelper v0.1.2)

- **Use `Dalamud.Utility.Util.OpenLink(url)`** to open a URL in the user's default browser from a plugin button. It's the bundled wrapper around the platform's `Process.Start` shell-execute path; works on Wine and Windows-native installs without extra fuss.
- **URL contract is versioned at the JSON level** (`{"v": 1, ...}`) and base64-url-safe-encoded into the URL **hash fragment** (`#r=<b64url>`), not a query string. Two small wins: hash fragments aren't sent to the server in HTTP requests, and they don't appear in Firebase Hosting access logs. Functionally equivalent for plugin → web read-only handoffs.
- **`System.Text.Json` serialises `byte[]` as a base64 string**, not a JSON array of numbers. If the receiving end expects `number[]` (TypeScript), the C# wire-format field must be `int[]` (or `uint[]`), not `byte[]`. Caught by the v0.1.2 round-trip scratch test.
- **The achievement-tracker URL is hardcoded** in `Plugin.cs` as `WebsiteBaseUrl`. If the tracker ever moves to a custom domain or different Firebase project, the constant needs bumping with the next plugin release. Filed as a v0.1.3 idea (Configuration override) in `IDEAS.md`.
- **Web-side decoder lives at `ffxiv-achievement-tracker/app/gc-supply-route/lib.ts`**. The C# encoder + TS decoder share the same versioned contract (`v: 1`); the round-trip scratch test (`/tmp` test we ran during v0.1.2) verifies they agree on every field.
- **See workspace ADR-0002** for the in-game-vs-web venue decision. The page is deliberately unauthenticated, breaking the achievement tracker's `<AuthGate>`-wraps-everything convention.

### ImGui table with footer button (GcSupplyHelper v0.1.3 fix)

- **`ImGuiTableFlags.ScrollY` with no explicit `outer_size` consumes all remaining vertical space of the parent window.** Anything you intend to render *below* `ImGui.EndTable()` (a button, a separator, a status line) gets pushed off-screen and silently disappears. The v0.1.2 "Plan route on web" button was invisible for exactly this reason.
- **Fix**: pass `new Vector2(0f, -footerHeight)` as `BeginTable`'s `outer_size` argument, with `footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2f + N` for whatever sits underneath. Negative Y in ImGui sizing means "leave this many pixels for the parent below me".
- Diagnostic signal: the table fills the tab and runs straight to the bottom edge with no gap. If there's no footer gap, your footer is hiding under the scroll region.

### Grand Company supply data surface (GcSupplyHelper v0.1)

- **Authoritative read path**: `FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentGrandCompanySupply.Instance()->SupplyProvisioningData`. The pointer is null until the player has opened the **Grand Company Personnel Officer's Supply / Provisioning list** at least once in the session. Once non-null it stays valid until logout, even after the window is closed. **The Timers panel does NOT populate this agent** — empirically verified in v0.1.1 in-game testing. Timers renders the missions from `UIState.GCSupply` (the persistent buffer at offset `0x10D28`) without going through the agent at all.
- **Addon-listener pattern**: v0.1.0 tried to enumerate known-good addon names (`GrandCompanySupplyList` + three Timers candidates). v0.1.1 simplified to a single catch-all `IAddonLifecycle.RegisterListener(AddonEvent.PostSetup, handler)`. `SupplyMissionReader.TryRefresh`'s null-pointer fast path makes the per-event cost negligible (one comparison) and the pattern is future-proof against unknown addon names.
- **Array layout**: `SupplyProvisioningData->SupplyData` (length 8) holds the eight crafter Supply Missions in ClassJob order CRP=8, BSM=9, ARM=10, GSM=11, LTW=12, WVR=13, ALC=14, CUL=15. `ProvisioningData` (length 3) holds the three gatherer Provisioning Missions in order MIN=16, BTN=17, FSH=18. Each `SupplyProvisioningItem` exposes `ItemId` (uint), `NumRequested` (byte), and `ItemName` (Utf8String, currently unused — we resolve names from the Lumina Item sheet for stability across future game-text changes).
- **Timers candidates pruned in v0.1.1**: the v0.1.0 guess list (`ContentsInfo`, `ContentsInfoDetail`, `ContentsTimerSetting`) was based on the speculation that Timers would side-effect-populate the agent. In-game testing falsified that — Timers reads from `UIState.GCSupply` directly. The catch-all listener obviates the guess list entirely; if a future patch *does* start populating the agent from some new entry point, the catch-all will pick it up without code changes.
- **Class-job index mapping**: the array index → ClassJob id table is hand-coded in `Services/SupplyMissionReader.cs:21`. If a future patch reshuffles the array order (unlikely but possible), users will see e.g. "Carpenter mission asks for leather", which is the diagnostic signal to update the mapping table. Don't trust the array index alone — the same item shouldn't be in two different mission slots on the same day, but if it is, treat the first occurrence as canonical.
- **Lumina `Recipe` lookup is keyed by `RecipeId`, not `ItemId`.** `recipeSheet.TryGetRow(itemId)` does NOT find the recipe for an item — Recipe is indexed by its own row id. Either use `RecipeLookup` (keyed by ItemId, returns class-keyed recipe references), or — what GcSupplyHelper does — eagerly walk the Recipe sheet once at plugin load and build a `Dictionary<itemResultId, RecipeData>`. ~2,700 rows; trivially cheap.
- **Leaf classification**: `Item.GatheringItem.RowId != 0` is the canonical "this item is harvestable by BTN/MIN/FSH" check (see `Services/LuminaRecipeDataSource.BuildGatheredSet()`). Items with neither a recipe nor a `GatheringItem` reference fall back to `ItemSearchCategory.RowId != 0` as a weak "is a vendor item" signal — better than nothing for the source-label colour, but not authoritative.
- **`UIState.GCSupply` at offset `0x10D28`** (size `0x2C28`) is the persistent backing buffer the game populates at login and reads from to render the Timers panel. Its internal layout is **not** documented in FFXIVClientStructs as of API 15. v0.1.1's in-game testing confirmed this is the only way to drop the "visit Personnel Officer once" prerequisite — there's no agent-populating side-effect we can hook. v0.2 reverse-engineers the 11 item-ID offsets in this buffer so the plugin works without any in-game UI interaction. Filed as the lead v0.2 item in `IDEAS.md`.
