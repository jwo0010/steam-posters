# Steam Posters

A small Windows desktop app (.NET 10 + Avalonia) that finds the non-Steam games in your Steam library, matches each one to the real game, and adds posters, banners, hero images, logos, icons and clean display names so they look and feel like native Steam games. See [docs/PLAN.md](docs/PLAN.md) for the full implementation plan.

**Status:** Steps 0-6 done: read-only spike, Steam layer, SteamGridDB artwork provider, matching, wizard GUI, apply into Steam with backup and restore, and packaging with auto-update.

## Install

Download `SteamPosters-win-Setup.exe` from the [latest release](https://github.com/jwo0010/steam-posters/releases/latest) and run it. It installs for your Windows user only (no admin rights) and updates itself from GitHub Releases. You need a free [SteamGridDB API key](https://www.steamgriddb.com/profile/preferences/api); the app asks for it on first run.

## Develop

- Run: `dotnet run --project steamposters`
- Test: `dotnet test steamposters/steamposters.sln` (set `STEAMGRIDDB_API_KEY` to include the live SteamGridDB test)
- Build the installer locally: `./build/Pack.ps1` (output in `artifacts/releases`)
- Release: bump `<Version>` in `steamposters/steamposters.csproj`, merge, then push a tag such as `v0.2.0`. The Release workflow builds the installer and publishes the GitHub release; installed copies update themselves.

## Layout

- `steamposters/`: Avalonia wizard app (MVVM: `ViewModels/`, `Views/`, `Services/`) and the solution (`steamposters.sln`)
- `SteamPosters.Core/`: Steam layer: VDF files, shortcuts, artwork files, safe writes, backups
- `SteamPosters.Artwork/`: SteamGridDB client, artwork ranking, image cache, encrypted API key store
- `SteamPosters.Artwork.Tests/`: xUnit tests (set `STEAMGRIDDB_API_KEY` to also run the live smoke test)
- `SteamPosters.Matching/`: guesses the real game for each shortcut (name cleanup, emulator detection, fuzzy scoring, confidence)
- `SteamPosters.Matching.Tests/`: xUnit tests
- `SteamPosters.App.Tests/`: wizard view-model tests
- `SteamPosters.Core.Tests/`: xUnit tests (`dotnet test steamposters/steamposters.sln`)
- `build/`: `Pack.ps1` builds the installer and update packages (Velopack)
- `.github/workflows/`: CI on pull requests, release on version tags
- `tools/spike/`: read-only PowerShell script that lists non-Steam games
