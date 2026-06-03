# Ideas — Gaming Tools (monorepo)

Running backlog. Reverse-chronological under each section. New
follow-ups land in `## Backlog`; shipped items get struck through and
moved to `## Shipped` with a `2026-MM-DD` date stamp.

For session-by-session context, the canonical history is `SESSION_LOG.md`.
This file is just "what might we do next, and when does it become urgent".

---

## Backlog

### Cross-plugin / CI

- **Bump GitHub Actions to Node-24-compatible versions before 2026-06-16.**
  All three workflows (`build-bulkdesynth.yml`,
  `build-wondroustailssolver.yml`, `build-gcsupplyhelper.yml`) use
  `actions/checkout@v4` and `actions/setup-dotnet@v4`, which CI is
  warning are Node-20-based. Forced to Node 24 by default on
  **2026-06-16**, Node 20 fully removed from the runner **2026-09-16**.
  Action: check for newer major versions of both actions and bump in
  one `ci:` commit. If newer versions aren't yet out, opt in early
  with `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` per the GitHub blog
  post.

### GcSupplyHelper

- ~~**v0.1.1: identify the Timers panel's addon name and prune dead
  candidates.**~~ — **shipped 2026-06-03 as v0.1.1**, but the resolution
  wasn't what we expected: in-game testing showed the Timers panel
  doesn't populate `AgentGrandCompanySupply` at all (it renders from
  `UIState.GCSupply` directly), so the Timers addon's *name* was
  irrelevant. v0.1.1 replaced the four-name guess list with a single
  catch-all `PostSetup` listener and corrected the empty-state copy.
- **v0.2 lead item: read `UIState.GCSupply` directly so the plugin
  works without any UI interaction.** The buffer at offset `0x10D28`
  (size `0x2C28`) is the persistent backing store that the Timers
  panel itself reads from; FFXIVClientStructs hasn't annotated its
  layout. v0.1.1's in-game evidence promoted this from "nice to have"
  to "the only way to drop the Personnel-Officer prerequisite".
  Approach: log the buffer pre-/post- known triggers (login,
  Personnel Officer open), diff for stable item-ID offsets that match
  the agent's view, document the layout in `Gaming Tools/CLAUDE.md`.
  The 11 item IDs should sit at a fixed offset in the buffer;
  identifying them requires one Personnel-Officer-populated session to
  get the ground-truth item IDs to search for.
- **Better vendor / source classification.** Currently
  `MaterialSource.Vendor` is a weak heuristic
  (`ItemSearchCategory.RowId != 0`) AND it collapses three distinct
  cases into one label:
  - **Drop OR Bicolor** — most Dawntrail "vendor" items
    (Gargantua Hide, Rroneek Fleece, etc.) are primarily mob drops
    in their respective zones, with the Bicolor Gemstone trader as a
    convenience shortcut. The vendor label is misleading because
    retainer ventures, MB, and direct hunting are all viable.
  - **Drop OR scrip exchange** — aethersands and similar high-end
    refined materials. Scrip is convenience; primary source is
    reduction of collectables.
  - **Vendor only** — genuinely no other source (rare; usually
    quest-locked vendor items).
  Build an explicit per-leaf model with primary + secondary sources:
  ```
  MaterialSource Primary   // Gathered / Drop / Vendor / Scrip / Other
  List<MaterialSource> SecondaryOptions  // e.g. [Vendor (Bicolor)]
  string? PrimaryDetail    // mob name + zone, or vendor + currency
  ```
  Drop data lives in `MobDrops` Lumina sheets (or equivalent — verify
  at build time). Scrip exchanges live in `GilShopItem` /
  `SpecialShop` joined on shop owner.
- **Surface intermediate crafts in the Per Mission tab.** v0.1
  flattens straight to leaves; the user mentioned wanting to gather
  raw materials, but seeing "you need 3 Bronze Ingots" between the
  turn-in and its leaf ingredients could help users who craft along
  the way. Could be a per-tab toggle.
- **Inventory comparison.** "You have 5 / need 14 Copper Ore" by
  cross-referencing `InventoryManager` (the same surface BulkDesynth
  uses). Likely the highest-value v0.2 add.
- **v0.3+ Gathering route planner.** Surface a "Route" tab that turns
  the aggregate shopping list into an ordered, zone-clustered,
  ET-window-aware step sequence with "Drop map flag" / `/tp <nearest
  aetheryte>` buttons per step. Building blocks:
  - **Per-leaf metadata join**: `GatheringPoint` + `GatheringPointBase`
    + `TerritoryType` for coords/zone/class/level; `GatheringPointTransient`
    (or sibling) for unspoiled ET windows. Eager-built dictionary
    keyed by leaf item id, same shape as the recipe table.
  - **Zone clustering + intra-zone TSP**: group leaves by
    territory + nearest aetheryte; within a zone of ≤8 leaves an
    exhaustive shortest-path order is trivial (8! = 40,320), larger
    zones get nearest-neighbour. Class-switches grouped so all
    BTN-class leaves run consecutively before MIN, etc.
  - **ET clock awareness**: compute Eorzea Time from
    `Framework.GetServerTime()` (70 RL minutes = 1 ET day). For
    unspoiled nodes, schedule entry by next-open-window OR display a
    countdown so the player can fit standard nodes between windows.
  - **Map flag + teleport**: Dalamud's map-flag API drops a marker at
    coords with one call; teleport is `/tp <aetheryte>` via the
    Teleporter plugin's chat command, not a direct reimplementation.
  - **Display**: a Route tab with a checklist of steps; "currently
    open / opens in N min" badges; per-step Map / Teleport buttons.
    Real UI work; the algorithm itself is small.
  - **Reference plugin**: GatherBuddy already solves the general case
    (arbitrary item list → routes). Our value-add is the
    GC-supply-context wrapping + cross-mission aggregation. Could
    consider sharing data if GatherBuddy exposes IPC.

### WondrousTailsOdds

- **v0.1.4 candidate: visualise the row+diagonal pattern.** The Best
  P(≥3) target is exactly 1/12 because the row+diagonal 7-stamp
  pattern simultaneously maximises all three thresholds. Show a tiny
  4×4 grid in the overlay with that pattern highlighted, so the
  player has a concrete shape to steer reshuffles toward, not just a
  number. Trade-off: overlay real-estate.
- **v0.1.5 candidate: "snap right" fallback.** If the WeeklyBingo
  addon is dragged hard against the left edge of the viewport, the
  overlay clamps to `x=0` and overlaps the journal. Add a branch in
  `PreDraw()` that snaps the window to the addon's right edge instead
  when there isn't room on the left.
- **Tune the Current colour banding.** v0.1.3 picked
  green/yellow/red breakpoints from intuition (matching / ≥80% of
  Best / below). May want adjusting after a few weeks of in-game use.
- **Decide whether `Max achievable: N line(s)` in the footer is
  useful or noise next to the Best target.** If it's noise, drop it.
  If it's the most-glanced number, promote it.

### BulkDesynth

(Carried from `SESSION_LOG.md` open threads — not yet ADR-worthy or
session-urgent.)

- **Block / allow list UI.** `Configuration.cs` has `ItemAllowList`
  and `ItemBlockList` `HashSet<uint>` fields with no editor surface.
  Currently only editable by hand-modifying the serialised JSON.
- **"Skip if equipped" / "skip if armoury duplicate" filters.**
  Logical extension of the existing safety filters.
- **Replace the local `ImRaii` helper in `Windows/MainWindow.cs`
  with Dalamud's bundled `Dalamud.Interface.Utility.Raii.ImRaii`** if
  that's stable on API 15+.
- **Raise `AddonWaitTimeoutMs` default from 3000 ms to 5000 ms** if
  anyone hits "item never went busy, skipping" warnings under network
  lag. The slider for it already exists in Settings.

## Shipped

(Nothing struck through yet — this file was created 2026-06-01 as part
of the v0.1.3 documentation pyramid audit. Items added before this
date live in `SESSION_LOG.md` entries.)
