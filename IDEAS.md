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
  Both `.github/workflows/build-bulkdesynth.yml` and
  `.github/workflows/build-wondroustailssolver.yml` use
  `actions/checkout@v4` and `actions/setup-dotnet@v4`, which CI is
  warning are Node-20-based. Forced to Node 24 by default on
  **2026-06-16**, Node 20 fully removed from the runner **2026-09-16**.
  Action: check for newer major versions of both actions and bump in
  one `ci:` commit. If newer versions aren't yet out, opt in early
  with `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` per the GitHub blog
  post.

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
