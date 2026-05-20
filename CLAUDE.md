# Gaming Tools (monorepo)

## Snapshot

Publishing-infrastructure monorepo for FFXIV Dalamud plugins. Each plugin lives in its own subfolder; CI builds the plugin and publishes the `.zip` + `pluginmaster.json` to GitHub Pages under a dedicated subpath so users can subscribe per-plugin. Currently active plugin: **BulkDesynth**. Status: **live**.

## Stack

- **Language**: C# (.NET 10.0 Windows, `EnableWindowsTargeting` so it cross-compiles on Mac/Linux)
- **Framework**: Dalamud plugin API
- **Hosting**: GitHub Pages (serves `docs/`)
- **CI**: GitHub Actions (`.github/workflows/build-bulkdesynth.yml` etc.)

## Run locally

Plugin builds run on CI, not locally. Local C# builds require:

```bash
dotnet build FF14/BulkDesynth/BulkDesynth.csproj -c Release
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

## Companion docs

- `README.md` — monorepo overview + per-plugin subscribe URLs

## Related projects

- **`ffxiv-achievement-tracker`** — consumes the BulkDesynth plugin published from here; coordinate plugin version bumps and CI timing (consumer needs to `git pull` after plugin CI lands before `npm run build`)

See `~/Claude Projects/docs/PROJECT_INDEX.md` for the full cross-project map.

## Gotchas

- Each plugin needs its own subpath under `docs/` so subscribe URLs don't collide.
- Plugin version bumps live in **two places** per plugin: the `.csproj` `<AssemblyVersion>` and the JSON manifest. Both must move together or Dalamud refuses to update.
- Adding a new plugin: create `FF14/<NewPlugin>/`, add a matching `.github/workflows/build-<newplugin>.yml`, commit a placeholder `docs/<subpath>/pluginmaster.json`. Follow the BulkDesynth pattern.
