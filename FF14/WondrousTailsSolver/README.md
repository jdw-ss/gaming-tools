# Wondrous Tails Odds

A personal Dalamud plugin that overlays bingo-line completion
probabilities on Final Fantasy XIV's weekly **Wondrous Tails** journal
(Khloe's mini-game).

For each board, the overlay shows:

- **P(≥1 line)**, **P(≥2 lines)**, **P(≥3 lines)** — the probability of
  ending the week with at least that many completed rows / columns /
  diagonals, given the stamps already placed and assuming the remaining
  stamps land on random unstamped cells.
- **Shuffle baseline** — the corresponding probabilities you'd see if
  you used a Second Chance shuffle to redistribute every sticker from
  scratch. Only shown while a shuffle is still cheap (≤ 7 placed).
- Colour bands relative to the shuffle baseline so a glance tells you
  whether to keep going or reshuffle.

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
