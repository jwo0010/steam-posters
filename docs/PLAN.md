# Steam Posters: implementation plan

Status: **decisions made** (section 8). Building step by step, checking in after each step.
GUI mockups: see the Steam Posters project thread (mockups.html).

---

## 1. What we are building

A small Windows desktop app that:

1. Finds your Steam install and the Steam account(s) on this PC.
2. Lists every **non-Steam game** you already added to that account.
3. Guesses the real game for each one (e.g. `Cyberpunk2077.exe` becomes "Cyberpunk 2077"), downloads matching artwork, and offers a clean display name.
4. Lets you review and swap anything you don't like, then writes it all into Steam in one click.
5. Restarts Steam so the changes show up in the desktop library and Big Picture.

## 2. How Steam stores this (the facts the design rests on)

| Thing | Where it lives | Notes |
|---|---|---|
| Steam install | Registry `HKCU\Software\Valve\Steam\SteamPath` | Fallback: `C:\Program Files (x86)\Steam` |
| Accounts on this PC | `{Steam}\config\loginusers.vdf` and folders in `{Steam}\userdata\{id}` | Each Steam account has its own non-Steam game list |
| Non-Steam games | `{Steam}\userdata\{id}\config\shortcuts.vdf` | **Binary** VDF file. Fields per game: `appid`, `AppName`, `Exe`, `StartDir`, `icon`, `LaunchOptions`, `IsHidden`, `AllowOverlay`, `tags`, `LastPlayTime`, and a few more |
| Artwork | `{Steam}\userdata\{id}\config\grid\` | Files named by the game's shortcut id (see below) |

Artwork files Steam reads for a non-Steam game with id `N`:

| File | What it is | Where you see it | Recommended size |
|---|---|---|---|
| `Np.png` | Poster (portrait "capsule") | Library grid, Big Picture library | 600 x 900 |
| `N.png` | Wide banner (horizontal capsule) | "Recent games" shelf, some Big Picture rows | 920 x 430 |
| `N_hero.png` | Big background banner | Top of the game's page | 3840 x 1240 |
| `N_logo.png` | Transparent game logo | Drawn on top of the hero | ~1280 wide, transparent |
| `N.json` | Logo position on the hero | Game page | Written by Steam when you drag the logo; we can write it too |
| icon | Small icon, path stored in `shortcuts.vdf` `icon` field | Library sidebar list, taskbar | 256 x 256 `.png`/`.ico` |

`.jpg` works too. We'll save `.png` for anything with transparency and keep what the source gives otherwise.

**The shortcut id.** Steam identifies a non-Steam game by a number computed from its exe path and name (`crc32(exe + name) | 0x80000000`). Recent Steam versions also store that number in `shortcuts.vdf` as `appid`. This matters because:
- If we rename a game and Steam *recomputes* the id, all its artwork (and Steam Input config, playtime) silently detach. We always read the stored `appid` and write it back unchanged, and only compute it ourselves for very old entries that lack one.
- Renaming is therefore safe in our app, and renaming in Steam's own UI afterwards stays safe too as long as the stored `appid` is present.

## 3. Limitations you should know up front

1. **Steam shows very little "metadata" for non-Steam games.** It displays the name, the four artwork pieces, and the icon. It does **not** show a description, developer, release date, genre or review score for non-Steam games, no matter what we write. So "metadata" in practice means: name + poster + banner + hero + logo + icon. Collections (Steam's folders/categories) are the one other thing, see point 5.
2. **Steam must be closed while we edit `shortcuts.vdf`.** Steam keeps the file in memory and overwrites it when it exits, so edits made while it runs get lost. Artwork-only changes can be written while Steam runs, but Steam often doesn't redraw them until a restart.
3. **Remote streaming: not a concern for you.** Custom artwork lives on the PC it was set on and is **not** synced by Steam Cloud. You stream with Moonlight/Sunshine, which shows the host PC's own Steam (with its artwork), so nothing extra is needed on client devices. A "Remote Play mode" (placing art on Steam Remote Play clients) stays a possible later feature for other users.
4. **Matching is a guess.** Exe names like `Launcher.exe`, `game.exe`, `start_protected_game.exe` (Epic/EAC games) say nothing. The app will use the folder name too and show a confidence level, but you'll sometimes need to pick the right game from a search box. Nothing gets written without you seeing it first.
5. **Collections are fragile.** Newer Steam stores collections in a cloud-synced JSON/database blob that Valve changes without notice. Collections are out of scope for version 1.
6. **Artwork source terms.** SteamGridDB (community uploaded, made exactly for this) needs a free API key per user. Art is community made, so occasionally you'll see fan art; the app will prefer "official" and highest-voted images.
7. **Big Picture caching.** Big Picture sometimes keeps old art until Steam restarts. The restart covers this.
8. **One account at a time.** If several Steam accounts have logged in on this PC, you pick which one to edit (defaults to the last logged in).
9. **Windows first.** Steam on Linux / Steam Deck uses the same files in different paths, so a later port is realistic, but version 1 targets Windows.

## 4. Discovery

Read Steam's own list (`shortcuts.vdf`). It already knows every non-Steam game you added, wherever it's installed, so the "games spread across many folders" limitation goes away, and there's no guessing which exes are games versus uninstallers or crash reporters. Folder scanning would only matter for *adding* games Steam doesn't know about yet; that's a possible later feature.

## 5. Architecture

```
+-----------------------------------------------------------+
|  UI (MVVM views, Avalonia)                                |
|  Welcome / Scan / Review / Art picker / Apply / Settings  |
+---------------------------+-------------------------------+
|  App services                                             |
|  ScanService  MatchService  ApplyService  BackupService   |
|  SteamProcessService (detect / close / restart Steam)     |
+---------------------------+-------------------------------+
|  Steam layer              |  Providers                    |
|  SteamLocator             |  IArtworkProvider             |
|  ShortcutsVdf (read/write)|   - SteamGridDbProvider       |
|  GridFolder (art files)   |   - SteamStoreProvider (later)|
|  ShortcutId (crc calc)    |  ImageCache (on disk)         |
+---------------------------+-------------------------------+
|  Storage: %APPDATA%\SteamPosters\ (settings, backups, log)|
|  and %LOCALAPPDATA%\SteamPosters\cache (downloaded images) |
+-----------------------------------------------------------+
```

Key design rules:
- **Steam layer has no UI and no network.** Pure file handling with unit tests against sample `shortcuts.vdf` files, so the risky part (writing Steam's files) is the best tested part.
- **Providers are pluggable.** SteamGridDB first; others slot in behind the same interface.
- **Every apply is backed up first.** `shortcuts.vdf` and any art we replace are copied to `%APPDATA%\SteamPosters\backups\{timestamp}\`. A "Restore" button rolls back.
- **Write safely.** Write to a temp file, verify it parses, then swap it in. Never leave a half-written `shortcuts.vdf`.
- **We only touch what you approved.** Fields we don't understand in `shortcuts.vdf` are preserved byte for byte.

Packaging: **Velopack** builds a one-click `Setup.exe` with built-in auto-update, published from GitHub Releases.

## 6. The user flow (wizard)

1. **First run:** app finds Steam, picks the last-logged-in account, asks for a SteamGridDB key (with a "Get a free key" button that opens the page, and a paste box). Key is stored with Windows DPAPI encryption, not plain text.
2. **Scan:** reads the non-Steam list, shows each game with its current art (or a blank).
3. **Auto-match:** for each game, searches by cleaned-up name and folder name, picks best match + best art. Shows a confidence badge: Matched / Check this / Not found.
4. **Review:** fix the "Check this" ones, swap any art you dislike, untick games you want left alone (tools, emulators).
5. **Apply:** "Steam needs to close to save these. Close and apply?" Yes: backup, write, reopen Steam. No: the app waits until you close Steam yourself, then applies.
6. **Done:** summary, plus a "Restore previous" button.

## 7. Build plan (steps, in order)

**Step 0: Spike on your machine. Done (2026-10-06).**
- Read-only script `tools/spike/List-NonSteamGames.ps1` read the real `shortcuts.vdf`: 7 non-Steam games, none with artwork or icons yet.
- Finding: **no stored `appid` matches `crc32(exe + name) | 0x80000000`**, and two entries with identical Exe + AppName have different stored ids. Current Steam assigns its own ids, so the stored `appid` is the only reliable key for artwork; the computed id is a fallback for entries that lack one. Duplicate Exe + AppName entries must be handled.
- Test poster and Remote Play client checks skipped by decision: the user streams with Moonlight/Sunshine (host-side Steam), so client-side art is not needed.

**Step 1: Steam layer. Done (2026-10-06).** Locator, binary VDF reader/writer, shortcut id calc, grid folder writer, backups. Unit tests with sample files (never your live files).
- Code: `SteamPosters.Core` (no UI, no network) and `SteamPosters.Core.Tests` (xUnit, 31 tests, synthetic files only), both in `steamposters/steamposters.sln`.
- Binary VDF keeps raw bytes for keys and strings, keeps other known types raw, and refuses unknown types rather than guessing their size.
- Default account: the MostRecent one in `loginusers.vdf`, else the first with non-Steam games, else the first. (On this PC no account is flagged MostRecent.)
- Read-only check on the real `shortcuts.vdf`: parsed and re-serialized in memory, byte-identical (2693 bytes); the file was not modified.

**Step 2: SteamGridDB provider + image cache. Done (2026-10-06).** Search, fetch grids/heroes/logos/icons, filter by size and style, cache to disk, respect rate limits.
- Code: `SteamPosters.Artwork` (references Core; Core stays network-free) and `SteamPosters.Artwork.Tests` (fake HTTP handler, no real network; one live smoke test runs only when `STEAMGRIDDB_API_KEY` is set).
- Ranking: drop NSFW / humor / epilepsy-warning images, prefer Steam's recommended size, then style ("official" logos and icons, "alternate" posters and banners), then score, then upvotes.
- Limits: at most 4 API calls at once, 30 s timeout per call, HTTP 429 retried up to 3 times honouring Retry-After (capped at 30 s).
- API key: stored with Windows DPAPI (current user) in `%APPDATA%\SteamPosters\settings`, never in plain text.
- Image cache: `%LOCALAPPDATA%\SteamPosters\cache` (local, so images don't roam), 20 MB cap per image, PNG / JPEG / WebP / ICO accepted by magic number. **WebP caveat:** Steam's grid folder only reads `.png` and `.jpg`, so WebP images are flagged `NeedsConversion`; **Decided (2026-10-06): convert WebP to PNG in step 5 using SkiaSharp**, which the app already gets through Avalonia (no new dependency). Art requests don't filter WebP out.

**Step 3: Matching. Done (2026-10-06).** Name cleanup rules (strip `.exe`, split CamelCase, drop "Launcher", "Shipping", "Win64", etc.), use parent folder names, fuzzy scoring, confidence levels.
- Code: `SteamPosters.Matching` (references Core and Artwork, no UI) and `SteamPosters.Matching.Tests` (fake provider, made-up paths modelled on a real library).
- `NameCleaner`: one list of noise words and one conservative list of store/repack tags stripped from dash-joined folder names ("Some Game-FitGirl" -> "Some Game"; unknown suffixes are kept).
- `CandidateExtractor`: search terms in order: AppName (skipped when it is just the exe name or noise), exe name, up to 3 meaningful parent folders.
- Emulators and tools (yuzu, Ryujinx, Cemu, RetroArch, Dolphin, PCSX2, RPCS3, Xenia, PPSSPP, DuckStation, Citra, Steam ROM Manager) are recognised by exe name. A game file in LaunchOptions becomes the only search term; without one the result is Not found ("emulator, no game in launch options") so the wizard can untick it.
- `NameSimilarity`: ignores case, accents, punctuation, a leading "The", "&" vs "and", roman vs arabic numerals; expands acronyms found in the candidate ("TotK" -> "Tears of the Kingdom", "AC" -> "Assassin's Creed"); best of word overlap and character bigram overlap. Small bonus for verified games.
- `MatchService`: at most 4 searches per game, stops early on a strong hit; Matched (score >= 0.85 and clear of a differently named runner-up by 0.05), Check this (>= 0.5 or several close results), Not found. Returns the match, top 5 alternatives, the term that matched and the suggested display name; `MatchAllAsync` yields one result per game for progress.
- Offline dry run on the real library: both yuzu entries and Ryujinx are emulators with no game in their launch options; "Launcher" searches its folder ("The Legend of Zelda TotK"); Starfield, ACBlackFlag and Avowed get sensible terms. A live run needs a SteamGridDB key.
- Live run (2026-10-06, with the user's key): SteamGridDB search returns at most 10 results and ignores words it doesn't know, so "The Legend of Zelda TotK" first matched the 1986 game. Fixed: a short editable list of well-known abbreviations (TotK, BotW, AC, GTA, CoD, ...) is spelled out into an extra search term, unknown abbreviations get a search-only term without them, and names missing words of the search term score lower. Result on the real library: Tears of the Kingdom, Starfield and Avowed Matched; ACBlackFlag Check this (SteamGridDB has a "Black Flag Resynced" entry next to AC IV); yuzu x2 and Ryujinx Not found (emulators).

**Step 4: GUI. Done (2026-10-06).** The wizard, built MVVM so the logic stays testable.
- Avalonia + CommunityToolkit.Mvvm, in the `steamposters` app project. Pages: **Set up** (Steam path, account picker, SteamGridDB key checked with one search then saved encrypted), **Find games** (reads the non-Steam list, matches each game with progress and a Stop button, shows posters and confidence badges; emulators are labelled "Emulator"), **Review** (tick/untick, edit the Steam name, swap poster / wide banner / hero / logo / icon from up to 12 ranked options each, or keep what Steam has), **Apply** (summary of the approved changes; writing comes in step 5).
- **Manual fix match** (requested 2026-10-06): on Review, "Wrong game? Search for the right one" searches SteamGridDB for any text and picks the result; it starts with the automatic match's alternatives and opens by itself for games that need checking. A hand-picked game counts as confirmed ("Picked by you"), gets ticked, takes the game's name, and reloads its art; the previous match stays in the list as an alternative.
- The approved changes are collected in an `ApplyPlan` (per game: shortcut index, appid, new name if changed, chosen image per art piece), which step 5 writes into Steam.
- Tests: `SteamPosters.App.Tests` drives the view models end to end with a fake SteamGridDB, a fake key store and a made-up Steam folder.

**Step 5: Apply flow. Done (2026-10-06).** Steam close/restart (or wait for the user to do it), safe write, restore.
- Order: download every chosen image at full resolution and convert it (WebP and ICO become PNG with SkiaSharp; icons are always PNG), **then** close Steam, so Steam is only down for the quick file writes.
- Closing Steam: if it is running, the app asks: "Close Steam and apply" (sends `steam.exe -shutdown`, waits up to 60 s, then reopens Steam if "Reopen Steam when done" is ticked), "I'll close it myself" (waits until Steam has exited; also the fallback when Steam ignores the shutdown), or "Cancel" (nothing changed). Back is disabled while this runs.
- Writing: backup first (`shortcuts.vdf` and every art file that will be replaced or created, into `%APPDATA%\SteamPosters\backups\{timestamp}\`), then art files into `grid\`, then `shortcuts.vdf` is read fresh (Steam rewrites it on exit) and saved safely with the new names and icon paths. Shortcuts are found by index and appid, or by appid if Steam reordered them. Old entries without a stored appid get their current id pinned before a rename, so their art stays attached.
- Any error after the backup restores it automatically ("everything was put back as it was"). After a successful apply, "Restore previous" puts back every file from that backup (closing Steam the same way) and removes files the apply created.
- Tests: `ApplyTests` covers Steam closed / running / ignoring shutdown / closed by the user / cancel, restore, automatic rollback, WebP conversion and appid pinning, all on a made-up Steam folder with a pretend Steam client. The real `SteamProcess` (steam.exe -shutdown and restart) is only exercised on a real apply.

**Step 6: Packaging.** Installer, app icon, GitHub Releases, auto-update.

**Step 7 (later, optional):** remember manual match picks so a rescan reuses them, Remote Play mode, collections, "add games from other launchers", Linux/Deck build, tray icon that watches for newly added games.

Each step ends with something you can try, and Claude checks in before starting the next.

## 8. Decisions (made 2026-10-06)

1. **Tech stack:** C# / .NET 10 + Avalonia UI
2. **Platform for v1:** Windows only
3. **Discovery:** Steam's own non-Steam list (`shortcuts.vdf`) only, no folder scanning
4. **Art source:** SteamGridDB, each user supplies their own free API key (more providers possible later)
5. **Steam restart:** the app asks, then closes and reopens Steam itself; the user can decline and close/reopen Steam themselves
6. **GUI:** the step-by-step wizard
7. **Source control:** Claude commits locally on the user's PC; the user reviews and pushes
