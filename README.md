# gaming-tools

Monorepo for personal game tooling. Currently houses Dalamud (FFXIV) plugins
under [`FF14/`](./FF14). Each plugin builds and publishes independently via
GitHub Actions; the resulting `pluginmaster.json` files are served from
GitHub Pages so they can be subscribed to from inside the game client.

## Plugins

| Plugin | Path | Subscribe URL |
| --- | --- | --- |
| [Bulk Desynth](./FF14/BulkDesynth) | `FF14/BulkDesynth` | `https://jdw-ss.github.io/gaming-tools/ff14/bulkdesynth/pluginmaster.json` |

(The subscribe URL works once the GitHub Pages site has been enabled on
this repository and the build workflow has run at least once — see
[Deployment](#deployment) below.)

## Layout

```
.
├── .github/workflows/
│   └── build-bulkdesynth.yml   CI: build + publish BulkDesynth on push
├── docs/                       Output: served by GitHub Pages
│   └── ff14/bulkdesynth/
│       ├── latest.zip
│       └── pluginmaster.json
└── FF14/
    └── BulkDesynth/            Source for the BulkDesynth plugin
```

The `docs/` folder is the GitHub Pages root. Each plugin gets a dedicated
subpath so future plugins (e.g. `FF14/SomethingElse`) drop in without
disturbing existing subscribers.

## Deployment

1. Push the repo to GitHub (`gh repo create jdw-ss/gaming-tools --public --source=. --remote=origin`).
2. On the repository's **Settings → Pages**, set:
   - Source: *Deploy from a branch*
   - Branch: `main` / folder: `/docs`
3. Push any change under `FF14/BulkDesynth/**` (or trigger the workflow
   manually). CI will build, zip, refresh `pluginmaster.json`, and commit
   the new artifacts into `docs/ff14/bulkdesynth/` on `main`.
4. In-game: Dalamud → Settings → Experimental → **Custom Plugin
   Repositories** → paste the subscribe URL from the table above → Save.

The first build can take a couple of minutes (Pages needs to publish);
subsequent updates are picked up by Dalamud the next time it refreshes
the repo (or click the refresh icon on the row).

## Per-plugin docs

See each plugin's own README for usage / commands / safety model:

- [`FF14/BulkDesynth/README.md`](./FF14/BulkDesynth/README.md)
