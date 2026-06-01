# Wondrous Tails Odds

A personal Dalamud plugin that overlays bingo-line completion
probabilities on Final Fantasy XIV's weekly **Wondrous Tails** journal
(Khloe's mini-game).

The overlay is a small floating window that opens to the left of the
Wondrous Tails journal whenever you open Khloe's book in-game, and
hides itself again when you close the journal. For each board it
shows:

- A **Current / Best** table, one row per threshold:
  - **P(≥1 line)** — probability of ending the week with at least one
    completed row / column / diagonal, given the stamps already placed
    and assuming the remaining stamps land on random unstamped cells.
  - **P(≥2 lines)** and **P(≥3 lines)** likewise.
  - The **Best** column is your aspirational target: the highest
    P(≥N) achievable by *any* 7-stamp configuration if the final two
    stamps land uniformly at random. By the time you reach 7 stamps —
    the last point at which Second Chance reshuffles are still useful —
    your Current row should match Best if you've reshuffled optimally.
    Best is a fixed constant per threshold (currently 100% / 100% /
    8.33%; the 8.33% comes out to exactly 1/12 from the row+diagonal
    pattern that simultaneously maximises all three thresholds).
  - Current is colour-coded by how close it is to Best: green when
    matching or exceeding, yellow when within ~80%, red when far below.
- **Shuffle baseline** — the corresponding probabilities you'd see on
  a fresh board with 9 random stamps. Only shown while a shuffle is
  still cheap (≤ 7 placed). Use this to answer "is my current board
  above or below the long-run average?"
- **Max achievable: N line(s)** in the footer — the hard combinatorial
  ceiling for the current board.
- Colour bands on the Current column relative to the shuffle baseline
  so a glance tells you whether to keep going or reshuffle.

## Relationship to the d17 "Wondrous Tails Solver" plugin

The official Dalamud store ships a plugin called **Wondrous Tails
Solver** at `MidoriKami/WondrousTailsSolver`, currently maintained by
daemitus / MidoriKami / nathanctech, on the same API level. That
plugin and this one solve the same problem. This repository is a
personal alternative — independent, clean-room, MIT licensed — kept
around so the author can experiment with features and UI without
touching the upstream's release cadence.

The two plugins ship under different `InternalName`s
(`WondrousTailsSolver` vs `WondrousTailsOdds`), so installing both
side-by-side is fine; Dalamud lists them as separate entries.

## Install

In-game: Dalamud → Settings → Experimental → **Custom Plugin
Repositories**, add:

```
https://jdw-ss.github.io/gaming-tools/ff14/wondroustailssolver/pluginmaster.json
```

(The URL path is `wondroustailssolver` for historical reasons — the
plugin was originally drafted under that name before the InternalName
collision with d17 was caught. The shipping plugin is "Wondrous Tails
Odds".)

After saving, click the refresh icon on the custom-repo row, open the
plugin installer, and search for **Wondrous Tails Odds** (or filter by
author "John Wilson (jdw-ss)").

## Commands

| Command | Effect |
| --- | --- |
| `/wts` | Toggle the in-journal overlay on or off |

## Build locally

See the monorepo [`README.md`](../../README.md) for the workspace
conventions and [`CLAUDE.md`](../../CLAUDE.md) for the cross-platform
build instructions. The short version:

```bash
# Drop the Dalamud reference DLLs (CI does the same):
mkdir -p .refs
curl -L -o /tmp/dalamud.zip https://goatcorp.github.io/dalamud-distrib/latest.zip
unzip -q /tmp/dalamud.zip -d .refs

dotnet build WondrousTailsSolver.csproj -c Release
```

The build output DLL is `WondrousTailsOdds.dll` even though the csproj
file is still `WondrousTailsSolver.csproj` — the disconnect is
deliberate (see `CLAUDE.md` "Plugin naming" gotcha for the rationale).

## Licence

MIT — see [`LICENSE`](./LICENSE).
