# AGENTS.md - LuDownloader.Electron

Guidance for humans and coding agents working only in `LuDownloader.Electron`.
This file is intentionally scoped to the Electron app and does not define rules for other projects in this repository.

---

## 1. Project context

`LuDownloader.Electron` is a standalone Windows desktop app built with Electron + React that mirrors LuDownloader workflows outside Playnite.

Primary goals:

- Search Steam titles and DLC using Morrenus/Steam store data.
- Download and process manifest ZIP files.
- Run `DepotDownloader` to install selected depots.
- Track installed and saved library games in local JSON storage.
- Integrate installs back into Steam (`.acf` + Lua copy).
- Check update drift through `ManifestChecker.exe`.
- Run helper tools like Steamless, and optional cloud task sync.

Shared runtime data root:

- `%APPDATA%\LuDownloader`
- `settings.json`
- `data/installed_games.json`
- `data/library_games.json`
- `manifest_cache/*.zip`
- `ludownloader.log`

---

## 2. Tech stack and runtime

| Area | Details |
|------|---------|
| Runtime | Electron `34.x` |
| UI | React `18.x` + TypeScript |
| Build | `electron-vite` + Vite + TS (`strict: true`) |
| Packaging | `electron-builder` (portable + NSIS targets) |
| Platform-specific deps | `winreg`, external helper binaries under `resources/` |
| Process model | Main process + Preload bridge + Renderer |

Key configs:

- `package.json` scripts: `dev`, `build`, `package`, `dist`
- `electron.vite.config.ts`: explicit main/preload/renderer entry wiring
- `electron-builder.yml`: `extraResources` from `resources/`, `asarUnpack` for `*.exe/*.dll/*.json`

---

## 3. Architecture and boundaries

### Main process (`src/main`)

`src/main/index.ts` owns app window creation, IPC registration, and event fan-out (`library:changed`, `updates:changed`, error forwarding).

Functional modules under `src/main/ipc/`:

- `settings.ts`: settings load/save with backward-compatible key mapping (camelCase + PascalCase).
- `paths.ts`: data/resource paths and `dotnet` discovery.
- `morrenus.ts`: health/stats/search/manifest download.
- `zip.ts`: parse manifest ZIP and expose `GameData`.
- `depot.ts`: run/cancel DepotDownloader and Steam auth flow.
- `steam.ts`: Steam path/library parsing, Lua copy, `.acf` creation, Add to Steam.
- `manifest.ts`: cache management, ManifestChecker integration, update status logic.
- `games.ts`: installed/saved records, reconciliation, safe uninstall path checks.
- `goldberg.ts`: Goldberg emulator setup, config patching, and DLL replacement.
- `steamless.ts`: Steamless tool execution.
- `igdb.ts`: optional metadata fetch using stored credentials.
- `logger.ts`, `jsonStore.ts`: shared file/log helpers.

### Achievement Discovery (`src/main/achievement`)

- `discoveryService.ts`: scans local files and registry for achievement data across multiple sources (Goldberg, CODEX, Empress, etc.).
- `registryAdapter.ts`: abstraction for Windows registry access.

### Cloud Sync (`src/main/sync`)

- `cloudSync.ts`: optionally pushes library state and polls for remote tasks (download, update checks, etc.) if configured.

### Preload (`src/preload`)

- `contextIsolation: true`, `nodeIntegration: false`.
- Exposes a typed `window.api` surface that mirrors IPC contract in `src/shared/ipc.ts`.
- Renderer must call only through this bridge; no direct Node/Electron calls in React components.

### Renderer (`src/renderer`)

- App shell in `App.tsx` with five views:
  - `LibraryView`
  - `SearchView`
  - `ManifestsView`
  - `DownloadView`
  - `SettingsModal`
- UI listens to main-process push events (`library:changed`, `updates:changed`) and refreshes derived rows via `library.rows()`.

### Shared contracts (`src/shared`)

- `ipc.ts`: canonical IPC channel names.
- `types.ts`: domain types (`InstalledGame`, `GameData`, `LibraryRow`, update status, etc.).

---

## 4. Functionality map (what does what)

### Search flow

1. Renderer `SearchView` calls `window.api.morrenus.search(query, mode)`.
2. Main `morrenus.ts` queries Steam store APIs for metadata/ranking and Morrenus auth headers where needed.
3. User can add result to saved library (`library.add`) or jump to download flow.

### Download flow

1. `DownloadView` fetches manifest from cache or Morrenus (`manifest.cachePath` + `morrenus.downloadManifest`).
2. ZIP is parsed (`zip.process`) into `GameData`.
3. User selects depots + target Steam library + runtime settings.
4. `download.start` executes `dotnet DepotDownloader.dll` per depot, streams logs/progress back over channel id.
5. Completion persists an `InstalledGame` record and refreshes library rows.

### Library management

- Installed and saved entries are kept separate and merged into `LibraryRow` for UI.
- Library actions include:
  - refresh/check updates
  - run/select executable
  - add to Steam
  - remove/uninstall
  - link an existing folder and reconcile metadata

### Steam integration

- Steam root from registry (`HKCU\Software\Valve\Steam`).
- Libraries parsed from `libraryfolders.vdf`.
- Add-to-Steam flow:
  - resolve manifest zip
  - ensure manifest GIDs
  - copy Lua file into Steam config
  - write `appmanifest_<appid>.acf`
  - persist updated install metadata

### Update checking

- `ManifestChecker.exe` invoked with AppIDs.
- Status computed per app by comparing saved depot manifest GIDs vs checker output.
- Unknown/error states become `cannot_determine` with message.

### Goldberg Emulator Setup

- `goldberg.run` patches `configs.user.ini` with user account/SteamID.
- Runs `generate_emu_config.exe` to produce emulator files.
- Backs up original Steam DLLs and replaces them with Goldberg versions.
- Optionally copies `GSE Saves` folder to `%APPDATA%`.

### Achievement Discovery

- Scans multiple "sources" (emulators/cracks) for `achievements.json`, `achievements.ini`, etc.
- Resolves AppIDs from folder structures.
- Supports GreenLuma registry scanning.
- Used to identify which games in the library have local achievement data.

### Cloud sync agent

- `sync/cloudSync.ts` optionally activates if `cloudServerUrl` and `cloudApiKey` are configured.
- Pushes local library snapshot and polls remote pending tasks every 60s.
- Supports remote task types like `download`, `add_to_library`, `add_to_steam`, `steamless`, `check_updates`.

---

## 5. Styling system and UI structure

### Token-first styling

- Core design tokens are in `src/renderer/src/styles/tokens.css`.
- Use existing CSS variables (`--accent`, `--bg-*`, `--fg-*`, `--good`, `--warn`, `--danger`, etc.) before adding new colors.
- Keep visual consistency by reusing radius and border token patterns (`--radius`, `--radius-sm`, `--line`, `--line-soft`).

### Global styles and layout

- Main stylesheet: `src/renderer/src/styles/app.css`.
- App layout model:
  - Custom draggable titlebar (`-webkit-app-region: drag`)
  - Left sidebar navigation
  - Right content area with per-view layout blocks
- Existing style language is dark, high-contrast, card-based UI with teal accent.

### Responsiveness

- Multiple breakpoints are already implemented (`1120`, `900`, `820`, `700`, `560`).
- When adding UI:
  - Update relevant breakpoint blocks.
  - Avoid fixed widths that break card/list behavior on narrow screens.
  - Preserve touch targets and button readability in compressed layouts.

### Component structure conventions

- Keep view-specific structure in `src/renderer/src/views/*`.
- Keep presentational icons in `icons.tsx`; do not inline SVG paths repeatedly in views.
- Reuse existing utility class families (`btn`, `badge`, `panel`, `empty`, etc.) where practical.

---

## 6. Coding conventions for this app

- Match existing TS/React style in edited file.
- Preserve strict typing; do not weaken types with broad `any`.
- Keep renderer-side logic UI-focused; route file system, subprocess, and OS operations through IPC.
- Add new features by extending:
  1. `src/shared/types.ts` (types)
  2. `src/shared/ipc.ts` (channel)
  3. `src/main/index.ts` + `src/main/ipc/*` (handler)
  4. `src/preload/index.ts` (bridge)
  5. renderer usage
- Keep logs actionable; include operation context.

---

## 7. Strict prohibitions

- NEVER call Node/Electron APIs directly from renderer components; use `window.api` only.
- NEVER bypass `contextBridge` with insecure patterns (`nodeIntegration: true`, disabling isolation, exposing raw `ipcRenderer`).
- NEVER run destructive file deletions without path safety checks (see `games.ts`/`manifest.ts` examples).
- NEVER assume helper binaries are bundled if packaging rules are changed; verify `resources/` inclusion and unpack rules.
- NEVER store secrets in source control (`apiKey`, cloud keys, Steam secrets, IGDB credentials).
- NEVER break compatibility of persisted data keys without migration logic.

---

## 8. Build, package, verify

From `LuDownloader.Electron/`:

```powershell
npm install
npm run dev
npm run build
npm run package
```

Expected outputs:

- Dev/build artifacts under `out/`
- Packaged app under `dist/` (portable executable and/or NSIS installer)
- Runtime helper binaries available under packaged `resources/`

Manual smoke checks (recommended):

1. App launches with custom titlebar and navigation.
2. Settings load/save persists to `%APPDATA%\LuDownloader\settings.json`.
3. Search works and can add entries to Library.
4. Manifest fetch + ZIP parse works.
5. Download progress/log channel updates in UI.
6. Library refresh and update checks return sensible statuses.
7. Add-to-Steam writes manifest/Lua for a valid install.

---

## 9. Quick change checklist

- [ ] Change is contained to `LuDownloader.Electron` scope.
- [ ] IPC contract kept in sync across shared/main/preload/renderer.
- [ ] Styling uses token system and existing responsive structure.
- [ ] No insecure renderer-to-node shortcuts introduced.
- [ ] Build succeeds (`npm run build`).
- [ ] Critical flow manually smoke-tested for touched area.

