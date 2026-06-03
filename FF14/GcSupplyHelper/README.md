# GC Supply Helper

A personal Dalamud plugin that turns the 11 daily Grand Company Supply
and Provisioning missions in Final Fantasy XIV into a single
raw-material shopping list — so you can gather everything in one pass
instead of chasing each mission's crafting tree individually.

For each game day it shows:

- **Per Mission tab** — every enabled class's turn-in, with the raw
  materials needed to craft (or gather) the required item.
- **Aggregate tab** — one flat table summing materials across all
  enabled missions. Items needed by multiple classes are listed once,
  with a "Needed by" badge naming each contributing class.
- **Settings tab** — class-job checkboxes so you can hide classes you
  don't level, plus a manual "Refresh now" button.

## Populating today's missions

The plugin reads from the game's `AgentGrandCompanySupply` agent. That
agent is null at login and becomes available the moment you open the
**Grand Company Personnel Officer's Supply / Provisioning list** once
per session. After that single visit, the plugin caches today's
missions in memory and the data stays available for the rest of the
session.

The in-game **Timers** panel (System → Online → Timers → Next Mission
Allowance) also shows the same missions, but it reads them from a
different memory buffer (`UIState.GCSupply`) that the agent doesn't
mirror — so opening Timers doesn't populate the plugin. A v0.2
ambition is to read `UIState.GCSupply` directly and drop the
Personnel-Officer prerequisite entirely.

## Install

In-game: Dalamud → Settings → Experimental → **Custom Plugin
Repositories**, add:

```
https://jdw-ss.github.io/gaming-tools/ff14/gcsupplyhelper/pluginmaster.json
```

Then click the refresh icon on the custom-repo row, open the plugin
installer, and search for **GC Supply Helper** (or filter by author
"John Wilson (jdw-ss)").

## Commands

| Command | Effect |
| --- | --- |
| `/gcs` | Open or close the main window |

## Build locally

See the monorepo [`README.md`](../../README.md) for the workspace
conventions and [`CLAUDE.md`](../../CLAUDE.md) for the cross-platform
build instructions. The short version:

```bash
# Drop the Dalamud reference DLLs (CI does the same):
mkdir -p .refs
curl -L -o /tmp/dalamud.zip https://goatcorp.github.io/dalamud-distrib/latest.zip
unzip -q /tmp/dalamud.zip -d .refs

dotnet build GcSupplyHelper.csproj -c Release
```

## Licence

MIT — see [`LICENSE`](./LICENSE).
