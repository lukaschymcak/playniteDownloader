# Steam Depot Downloader — Design Spec

**Date:** 2026-04-06
**Status:** Draft

## Goal

Add Steam depot downloading as a second download method alongside the existing repack-based flow. Users provide a Steam App ID or game name; the plugin fetches depot info from Steam, lets the user pick which depots to download, and downloads them using DepotDownloader.

## User Flow

1. User types a Steam App ID (e.g. `1245620`) or a game name (e.g. `Elden Ring`) into the search UI
2. `SteamSourceProvider` looks up the game:
   - If App ID: fetches depot info directly via SteamKit2
   - If name: searches Steam Store Web API to find the App ID, then fetches depot info
3. UI shows a depot picker dialog listing all depots (name, size, OS, language)
4. User selects which depots to download
5. Selected depots are enqueued as `QueueEntry` items with `steam://depot/<appId>/<depotId>/<manifestId>` URIs
6. Pipeline processes them: `SteamDepotDownloader` invokes DepotDownloader.dll, no extraction needed, files land directly in the game directory
7. `PlayniteIntegration` marks the game as installed

## Architecture

### New Directory: `Steam/`

Sits alongside the existing layers as a self-contained Steam integration module.

```
PlayniteDownloaderPlugin/
├── Steam/
│   ├── SteamApiClient.cs        — SteamKit2 anonymous login + product info
│   ├── SteamDepotDownloader.cs  — IDownloader impl, shells out to DepotDownloader.dll
│   └── SteamSourceProvider.cs   — ISourceProvider impl, searches Steam by name/App ID
├── Models/
│   ├── SteamAppInfo.cs          — depot info for a Steam app
│   ├── SteamDepotInfo.cs        — one depot with manifest, size, OS
│   ├── SteamDepotSelection.cs   — UI selection result
│   └── (existing models unchanged)
└── UI/
    └── SteamDepotPickerDialog.xaml + .cs
```

### New Components

#### `SteamAppInfo`

```
int AppId
string Name
string InstallDir
List<SteamDepotInfo> Depots
```

#### `SteamDepotInfo`

```
int DepotId
string Name
string? ManifestId
long? Size
string? OsList          — "windows", "linux", null
string? Language        — null for base depot, "english" etc for language-specific
```

#### `SteamApiClient`

Static class wrapping SteamKit2.

- `FetchAppInfoAsync(int appId, CancellationToken ct)` → `SteamAppInfo?`
  - Creates `SteamClient`, calls `AnonymousLogin()`
  - Calls `GetProductInfo(apps: [appId])`
  - Parses response: extracts depots, manifests, sizes, installdir, game name
  - Falls back to Steam Store Web API for name and header image
- `SearchByNameAsync(string name, CancellationToken ct)` → `List<(int appId, string name)>`
  - Calls `https://store.steampowered.com/api/storesearch?term=<name>&l=english&cc=US`
  - Returns top matches with App IDs

#### `SteamDepotDownloader`

Implements `IDownloader`. Wraps DepotDownloader.dll as a subprocess.

- `StartAsync(string uri, string savePath, CancellationToken ct)`
  - Parses `steam://depot/<appId>/<depotId>/<manifestId>` URI
  - Builds command: `dotnet <depsDir>/DepotDownloader.dll -app X -depot Y -manifest Z -dir <savePath> -validate`
  - Launches subprocess, reads stdout line-by-line
  - Parses progress from percentage regex `(\d{1,3}(?:\.\d{1,2})?)%` (same as ACCELA)
  - Fires `ProgressChanged` events
- `Pause()` — kills the subprocess. DepotDownloader supports resume by re-running with same args
- `ResumeAsync()` — re-launches subprocess with same args
- `Cancel(bool deleteFile)` — kills subprocess, optionally deletes downloaded files
- `GetStatus()` → `DownloadProgress`

#### `SteamSourceProvider`

Implements `ISourceProvider`.

- `Id` = `"steam"`, `Name` = `"Steam"`
- `SearchAsync(string query, CancellationToken ct)` → `List<DownloadResult>`
  - If query is all digits: treat as App ID, call `SteamApiClient.FetchAppInfoAsync()`
  - Otherwise: call `SteamApiClient.SearchByNameAsync()`, then `FetchAppInfoAsync()` for top match
  - Returns one `DownloadResult` per depot, with:
    - `Title` = depot name or game name
    - `Uris` = `[ "steam://depot/<appId>/<depotId>/<manifestId>" ]`
    - `FileSize` = formatted depot size
    - `SourceId` = `"steam"`, `SourceName` = `"Steam"`
    - `MatchScore` = 1.0

#### `SteamDepotPickerDialog`

WPF dialog window.

- DataGrid with columns: Select (checkbox), Name, Size, OS, Language
- Pre-selects Windows depots by default
- "Download Selected" button
- Returns `SteamDepotSelection` containing the selected `SteamDepotInfo` list and `SteamAppInfo`

### Pipeline Changes

#### `DownloadPipelineRunner.ProcessEntryAsync()`

Small modification to handle `steam://` URIs:

- Detect `steam://` prefix on `entry.OriginalUri`
- If steam:
  - Use `SteamDepotDownloader` instead of `HttpDownloader`
  - Skip `ExtractionService` (files are already in the correct directory)
  - Use `SteamAppInfo.InstallDir` to set the extraction path before download
  - Still call `InstallResolver.FindExecutable()` and `PlayniteIntegration.MarkInstalled()`
- If not steam: existing flow unchanged

### Existing Components — No Changes

- `DownloadQueue` — `QueueEntry` works as-is with `steam://` URIs
- `HttpDownloader` — unchanged
- `RealDebridClient` — unchanged
- `DownloaderFactory` — unchanged
- `ExtractionService` — unchanged (just not called for Steam entries)
- `PlayniteIntegration` — unchanged
- `SourceManager` — just gets `SteamSourceProvider` registered as an additional source
- `UserConfig` — no new fields in v1

## Dependencies

### New NuGet: SteamKit2

- Used by `SteamApiClient` for anonymous Steam login and product info
- Same library DepotDownloader uses internally

### Bundled Binary: DepotDownloader.dll

- Placed in plugin's `deps/` directory
- Shipped with the plugin
- Invoked via `dotnet <path>/DepotDownloader.dll` as subprocess
- Requires .NET runtime (already present for Playnite)

## URI Scheme

`steam://depot/<appId>/<depotId>/<manifestId>`

Examples:
- `steam://depot/1245620/1245621/4837298473628746321`
- `steam://depot/1245620/1245622/9876543210987654321`

This scheme is stored in `QueueEntry.OriginalUri` and parsed by `SteamDepotDownloader`.

## Error Handling

| Scenario | Handling |
|----------|----------|
| App ID not found | `SearchAsync` returns empty list; UI shows "no results" |
| Anonymous login fails | Fall back to Steam Store Web API only (limited depot info) |
| No manifest for depot | Skip that depot with warning in picker dialog |
| DepotDownloader crashes | Subprocess exit code != 0 → entry set to Error status with message |
| Steam CDN unavailable | Subprocess error → entry set to Error status |
| User cancels mid-download | Kill subprocess, optionally delete partial files, entry set to Paused |

## Out of Scope (v1)

- Authenticated Steam login (anonymous only)
- Steam Guard / 2FA
- DLC depot auto-detection and unlocking
- Download speed limiting
- Depot delta patches (full downloads only)
- Multi-language depot filtering logic (show all, let user pick)
