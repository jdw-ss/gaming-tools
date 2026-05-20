# Bulk Desynth (Dalamud plugin)

Queue and run **bulk desynthesis** jobs without clicking each item by hand.
Tick which bags (and optionally the armoury chest) to scan, optionally
narrow the result with filters (item name contains, ilvl range, HQ, max
spiritbond, or an exact Lumina row id), then preview and confirm. Every
job is **dry-run first**: the plugin builds a preview list, you click the
red Desynth button, then it walks the list one item at a time on the
framework tick.

## Status

`v0.2.0` — tested end-to-end in a live client. Targeting tab now uses
multi-select bag checkboxes plus inline filter parameters (replacing the
v0.1.0 scope-radio UX). Name-contains filter added so you don't need to
know item row IDs.

## Design at a glance

1. **`InventoryScanner`** reads `InventoryManager` on the framework thread
   and turns matching slots into `DesynthCandidate` records (one per slot).
   Items are skipped when:
   - `Lumina.Item.Desynth == 0` (item is fundamentally not desynthable)
   - `IsSymbolic` is true (crystal/currency pseudo-slots)
   - The player's level in the item's `ClassJobRepair` class is below 30
     (the global desynth unlock threshold), if `RespectSkillCap` is on
   - The unlock quest (id 65688) is not complete
2. **`MainWindow`** (ImGui) - single Targeting tab with bag/armoury
   checkboxes at the top, filter parameters (name-contains, item id,
   ilvl range, HQ toggle, spiritbond cap) below them, then **Build
   preview** -> table -> red **Desynth N items** button. There is no
   auto-run path - every job requires explicit click.
3. **`DesynthExecutor`** ticks on `IFramework.Update` and walks the queue:
   ```
   FireNext -> WaitForBusy -> WaitForBusyToClear -> PostCastCooldown -> ...
   ```
   No `Thread.Sleep`, every wait is a deadline checked on the next tick.

### Desynth invocation

Matches the canonical pattern used by AutoRetainer / SomethingNeedDoing /
ffxiv-bundleoftweaks:

```csharp
AgentSalvage.Instance()->SalvageItem(itemPtr);                 // open dialog
var retval = new AtkValue();
var args = stackalloc AtkValue[2];
args[0] = new AtkValue { Type = AtkValueType.Int,  Int  = 0 };
args[1] = new AtkValue { Type = AtkValueType.Bool, Byte = 1 };
agent->AgentInterface.ReceiveEvent(&retval, args, 2, 1);       // fire Begin
```

The `Bool=1` second argument bypasses the SelectYesno confirmation popup
that the game shows for HQ / spiritbonded items. **That's why the scanner
defaults to excluding HQ items** — without the popup we lose the only
remaining player-facing safety net for those slots. You can opt in via the
HQ checkbox + spiritbond slider on the Filter tab, but the dry-run preview
is then the sole defense.

Pacing is gated by `ICondition[ConditionFlag.Occupied39]` (the game's own
busy flag during the desynth cast + animation) plus a configurable
post-cast cooldown.

## Build (Windows, requires XIVLauncher + .NET 10 SDK)

```powershell
cd "Gaming Tools/FF14/BulkDesynth"
dotnet build -c Release
```

The csproj's `DalamudLibPath` auto-detection picks (in order):
1. A local `.refs/` folder in the project root.
2. `$AppData\XIVLauncher\addon\Hooks\dev\` (Windows + XIVLauncher).
3. Whatever you pass on `-p:DalamudLibPath=...`.

If you build on macOS / Linux (or in CI), copy `.refs/` from your existing
tracker plugin:

```bash
cp -R "../../../ffxiv-achievement-tracker/dalamud-plugin/.refs" ./
```

## Install (end-user)

The plugin is published via GitHub Pages out of this monorepo. To
subscribe from inside the game:

1. `/xlsettings` -> **Experimental** tab -> **Custom Plugin Repositories**.
2. Paste:
   ```
   https://jdw-ss.github.io/gaming-tools/ff14/bulkdesynth/pluginmaster.json
   ```
3. Click **Save and Close**, then open `/xlplugins` -> **All Plugins** and
   search for **Bulk Desynth**. Click **Install**.
4. `/bds` to open the window.

(Replace `jdw-ss/gaming-tools` in the URL above with whatever GitHub
owner / repo this monorepo lives under. The build workflow constructs
the URLs from `github.repository` automatically, so the pluginmaster
points at the right place.)

## Install for local development

If you want to hack on the source and load it as a dev plugin instead of
subscribing:

1. Build (above). Produces
   `bin\x64\Release\net10.0-windows\BulkDesynth.dll` plus
   `BulkDesynth.json`.
2. Copy that folder into
   `%AppData%\XIVLauncher\devPlugins\BulkDesynth\`.
3. In-game, `/xlplugins` -> **Dev Plugins** tab -> load **Bulk Desynth**.
4. Run `/bds` to open the window.

## Commands

| Command          | What it does                                                                   |
| ---------------- | ------------------------------------------------------------------------------ |
| `/bds`           | Open the main window.                                                          |
| `/bds bag <1-4>` | Build a preview for the given bag and open the window for confirmation.        |
| `/bds item <id>` | Build a preview that targets every copy of item ID *id* across the main bags. |
| `/bds stop`      | Cancel a run that is currently in progress.                                    |

## Safety model

- **Dry-run by default.** Building a preview never touches the game. Only
  the explicit red Desynth button starts the executor.
- **Per-run hard cap.** Configurable in Settings (defaults to 50) — even a
  bad filter cannot nuke an entire bag in one click.
- **Unlock check.** If the desynthesis unlock quest is not complete, the
  scanner returns an empty list and logs a warning. Nothing fires.
- **Skill cap check.** Items whose required DoH class is below level 30
  are filtered out, so the executor never fires on something that would
  fail validation anyway.
- **HQ excluded by default.** The `Bool=1` Begin parameter bypasses the
  game's HQ warning popup, so we exclude HQ items by default. Opt in via
  the Filter tab if you really want to.
- **Slot reverification.** Between dequeueing a candidate and firing the
  desynth, the executor re-reads the slot and aborts if the item ID has
  changed (i.e. you moved stuff between preview and run).
- **Stop is one click.** `/bds stop` or the Stop button on the Run tab
  drains the queue immediately. The currently-casting desynth still
  completes in-game (you cannot abort a cast remotely) but nothing
  further gets queued.

## Known gaps / TODO

- Block / allow list editing is not in the UI yet — `Configuration.cs`
  exposes the fields but you can only edit them by hand-editing the
  serialised config JSON.
- "Skip if already in armoury" cross-check (i.e. don't desynth the same
  ilvl gear that's currently equipped) isn't implemented.
- No throttle / behavioural test suite. First real test is in-game.

## Notes

- The executor never amends items, never sends packets directly, and never
  performs any action a player cannot perform manually. It only invokes
  the same `SalvageItem` + Begin event the client itself runs when you
  click Begin in the SalvageDialog.
- Inventory reads use `InventoryManager` and are read-only.
