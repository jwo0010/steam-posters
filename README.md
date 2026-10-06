# Steam Posters

A small Windows desktop app (.NET 10 + Avalonia) that finds the non-Steam games in your Steam library, matches each one to the real game, and adds posters, banners, hero images, logos, icons and clean display names so they look and feel like native Steam games. See [docs/PLAN.md](docs/PLAN.md) for the full implementation plan.

**Status:** Steps 0-4 done (read-only spike, Steam layer, SteamGridDB artwork provider, matching, wizard GUI); step 5 (apply into Steam) next. Run the app with `dotnet run --project steamposters` (it writes nothing to Steam yet).

## Layout

- `steamposters/`: Avalonia wizard app (MVVM: `ViewModels/`, `Views/`, `Services/`) and the solution (`steamposters.sln`)
- `SteamPosters.Core/`: Steam layer: VDF files, shortcuts, artwork files, safe writes, backups
- `SteamPosters.Artwork/`: SteamGridDB client, artwork ranking, image cache, encrypted API key store
- `SteamPosters.Artwork.Tests/`: xUnit tests (set `STEAMGRIDDB_API_KEY` to also run the live smoke test)
- `SteamPosters.Matching/`: guesses the real game for each shortcut (name cleanup, emulator detection, fuzzy scoring, confidence)
- `SteamPosters.Matching.Tests/`: xUnit tests
- `SteamPosters.App.Tests/`: wizard view-model tests
- `SteamPosters.Core.Tests/`: xUnit tests (`dotnet test steamposters/steamposters.sln`)
- `tools/spike/`: read-only PowerShell script that lists non-Steam games
