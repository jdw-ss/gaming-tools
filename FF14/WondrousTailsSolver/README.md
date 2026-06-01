# Wondrous Tails Solver

A Dalamud plugin that overlays bingo-line completion probabilities on
Final Fantasy XIV's weekly **Wondrous Tails** journal (Khloe's mini-game).

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

This is a **clean-room reimplementation** of the archived
[`MidoriKami/EzWondrousTails`](https://github.com/MidoriKami/EzWondrousTails)
plugin. No code was copied; the original is unlicensed and no longer
loads against current Dalamud (API 15).

## Install

In-game: Dalamud → Settings → Experimental → **Custom Plugin
Repositories**, add:

```
https://jdw-ss.github.io/gaming-tools/ff14/wondroustailssolver/pluginmaster.json
```

Then enable **Wondrous Tails Solver** in the plugin list.

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

## Licence

MIT — see [`LICENSE`](./LICENSE).
