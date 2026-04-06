# Steam Depot Downloader — Design Spec

**Date:** 2026-04-06
**Status:** Draft

## Goal

Add Steam depot downloading as a second download method alongside the existing repack-based flow. Users provide a Steam App ID or game name; the plugin fetches depot info from Steam, lets the user pick which depots to download, and downloads them using DepotDownloader.

## User Flow

Steam downloading is a **separate entry point** from the existing search-based flow. It is not wired through `ISourceProvider` because the depot picker interaction model (multi-select from a structured list) does not fit the flat search-results pattern.

1. User clicks a "Download from Steam" button in the side panel
2. A `SteamDownloadDialog` opens: single text field for App ID or game name
3. User types a Steam App ID (e.g. `1245620`) or a game name (e.g. `Elden Ring`) and clicks Search
4. `SteamApiClient.SearchOrFetchAsync()` resolves the query:
   - If all digits: fetches depot info directly via SteamKit2
   - If name: searches Steam Store Web API to find the App ID, then fetches depot info
5. `SteamDepotPickerDialog` opens showing all depots (name, size, OS, language) with checkboxes
6. User selects depots, picks download location, clicks Download
7. For each selected depot, a `QueueEntry` is created with a `steam://depot/...` URI (see URI scheme below) and enqueued
8. Pipeline processes each entry: `SteamDepotDownloader` invokes DepotDownloader.dll, no extraction needed, files land directly in the game directory
9. `PlayniteIntegration` marks the game as installed

## Architecture

### New Files — Placed in Existing Layer Directories

Components go into the existing layer directories to maintain the established convention (Source/, Download/, Models/, UI/).

```
PlayniteDownloaderPlugin/
├── Download/
│   ├── SteamApiClient.cs          — SteamKit2 anonymous login + product info
│   └── SteamDepotDownloader.cs    — IDownloader impl, shells out to DepotDownloader.dll
├── Models/
│   ├── SteamAppInfo.cs            — depot info for a Steam app
│   ├── SteamDepotInfo.cs          — one depot with manifest, size, OS
│   └── (existing models unchanged)
└── UI/
    ├── SteamDownloadDialog.xaml + .cs        — input dialog for App ID / name
    ├── SteamDownloadDialogViewModel.cs
    ├── SteamDepotPickerDialog.xaml + .cs     — multi-select depot picker
    └── SteamDepotPickerDialogViewModel.cs
```

### New Models

#### `SteamAppInfo`

```csharp
class SteamAppInfo
{
    int AppId
    string Name
    string InstallDir
    List<SteamDepotInfo> Depots
}
```

#### `SteamDepotInfo`

```csharp
class SteamDepotInfo
{
    int DepotId
    string Name
    string ManifestId         // non-nullable; depots without manifests are excluded before reaching the picker
    long? Size
    string? OsList            // "windows", "linux", null
    string? Language          // null for base depot, "english" etc for language-specific
}
```

Depots without a `ManifestId` are filtered out in `SteamApiClient.FetchAppInfoAsync()` and never shown to the user. This means every depot in the picker can be downloaded and the URI scheme always has all three required parts.

### New Components

#### `SteamApiClient` (instance class, implements `IDisposable`)

Wraps SteamKit2 for anonymous Steam login and product info fetching. Instance class because SteamKit2's `SteamClient` has a connection lifecycle (connect, callback wait, login, callback wait, request, callback wait).

The caller creates one instance, makes calls, then disposes. The `SearchOrFetchAsync` method handles the full connect-request-disconnect cycle internally so the caller does not manage connection state.

```csharp
interface ISteamApiClient : IDisposable
{
    Task<SteamAppInfo?> FetchAppInfoAsync(int appId, CancellationToken ct);
    Task<List<SteamSearchResult>> SearchByNameAsync(string name, CancellationToken ct);
}
```

- `FetchAppInfoAsync(int appId, CancellationToken ct)` → `SteamAppInfo?`
  - Creates `SteamClient`, connects, calls `AnonymousLogin()`
  - Calls `GetProductInfo(apps: [appId])`
  - Parses response: extracts depots, manifests, sizes, installdir, game name
  - Filters out depots without manifest IDs
  - Falls back to Steam Store Web API for name when SteamKit2 returns generic "App NNNN"
  - Disconnects and returns
- `SearchByNameAsync(string name, CancellationToken ct)` → `List<SteamSearchResult>`
  - Calls `https://store.steampowered.com/api/storesearch?term=<name>&l=english&cc=US`
  - Returns top matches with App IDs and names
- `Dispose()` — disconnects SteamClient if connected

#### `SteamSearchResult`

```csharp
class SteamSearchResult
{
    int AppId
    string Name
}
```

#### `SteamDepotDownloader`

Implements `IDownloader`. Wraps DepotDownloader.dll as a subprocess.

- `StartAsync(string uri, string savePath, CancellationToken ct)`
  - Parses the URI (see URI scheme below) to extract appId, depotId, manifestId, installdir
  - Sets savePath to `<userDownloadPath>/<installdir>` so files land in the correct subdirectory
  - Builds command: `dotnet <depsDir>/DepotDownloader.dll -app X -depot Y -manifest Z -dir <savePath> -validate`
  - Launches subprocess, reads stdout line-by-line
  - Parses progress from percentage regex `(\d{1,3}(?:\.\d{1,2})?)%` (same pattern ACCELA uses)
  - Fires `ProgressChanged` events with estimated bytes/total based on percentage × depot size
- `Pause()` — kills the subprocess via `CancellationTokenSource.Cancel()`. Sets status to Paused.
- `ResumeAsync()` — re-launches the subprocess with the same args. DepotDownloader natively resumes partial downloads by checking existing files and only downloading missing chunks.
- `Cancel(bool deleteFile)` — kills subprocess, optionally deletes the downloaded directory
- `GetStatus()` → `DownloadProgress`

#### `SteamDownloadDialog`

WPF dialog window — the entry point for Steam downloads. Opened from a button in the side panel.

- Single text field: "Enter Steam App ID or game name"
- Search button
- If the query is an App ID: directly shows `SteamDepotPickerDialog` with depot info
- If the query is a name: shows a list of search results, user picks one, then shows `SteamDepotPickerDialog`
- Download path picker (same as existing search dialog)

#### `SteamDepotPickerDialog`

WPF dialog window.

- DataGrid with columns: Select (checkbox), Name, Size, OS, Language
- Pre-selects Windows depots by default (where `OsList` is null or contains "windows")
- Shows game name and App ID at the top
- "Download Selected" button
- Returns list of selected `SteamDepotInfo` items plus the `SteamAppInfo`

### Pipeline Changes

#### `DownloadPipelineRunner.ProcessEntryAsync()` — Modified

The existing method on line 74-90 directly instantiates `HttpDownloader`. It needs to branch based on URI scheme. The change is local to this method.

**Branch logic (pseudo-code):**

```
if entry.OriginalUri starts with "steam://"
    ProcessSteamEntryAsync(entry, ct)
else
    // existing flow unchanged
```

**New private method: `ProcessSteamEntryAsync(QueueEntry entry, CancellationToken ct)`**

1. Parse the `steam://` URI to extract installdir
2. Set `entry.ExtractionPath` = `<entry.DownloadPath>/<installdir>`
3. Create `SteamDepotDownloader` and subscribe to progress
4. Call `StartAsync(entry.OriginalUri, entry.DownloadPath, ct)`
5. Call `InstallResolver.FindExecutable(entry.ExtractionPath)`
6. Call `_integration.MarkInstalled(entry.GameId, entry.ExtractionPath, exePath)`
7. Set `entry.Status = DownloadStatus.Complete`

No extraction step. No URL resolution via `DownloaderFactory`. The `steam://` URI goes directly to `SteamDepotDownloader`.

### Existing Components — No Changes

- `DownloadQueue` — `QueueEntry` works as-is with `steam://` URIs
- `HttpDownloader` — unchanged
- `RealDebridClient` — unchanged
- `DownloaderFactory` — unchanged (not called for Steam entries)
- `ExtractionService` — unchanged (not called for Steam entries)
- `PlayniteIntegration` — unchanged
- `SourceManager` — unchanged (Steam is not an `ISourceProvider`)
- `UserConfig` — no new fields in v1

### Plugin Entry Point Changes

`PlayniteDownloaderPlugin.cs` needs minor additions:

- Register a "Download from Steam" button in the side panel UI
- Wire the button to open `SteamDownloadDialog`
- The dialog handles the entire Steam flow independently from the existing search flow

## URI Scheme

```
steam://depot/<appId>/<depotId>/<manifestId>?installdir=<installdir>
```

The `installdir` query parameter carries the game's install directory name (e.g. `Elden Ring`). This is needed by the pipeline to set the correct extraction path. It is URL-encoded to handle spaces and special characters.

Examples:
- `steam://depot/1245620/1245621/4837298473628746321?installdir=Elden%20Ring`
- `steam://depot/1245620/1245622/9876543210987654321?installdir=Elden%20Ring`

This URI is stored in `QueueEntry.OriginalUri` and parsed by `SteamDepotDownloader`.

## Dependencies

### New NuGet: SteamKit2

- Used by `SteamApiClient` for anonymous Steam login and product info
- Same library DepotDownloader uses internally
- Version: latest stable

### Bundled Binary: DepotDownloader.dll

- Placed in plugin's `deps/` directory (e.g. `deps/DepotDownloader.dll`)
- Shipped with the plugin as a fixed version
- Invoked via `dotnet <path>/DepotDownloader.dll` as subprocess
- Requires .NET runtime (already present for Playnite)
- If the DLL is missing at download time, the entry fails with a clear error message

### No other new dependencies

`SteamDownloadDialog` and `SteamDepotPickerDialog` use WPF (already in project). All other infrastructure (queue, progress, Playnite integration) is reused.

## Error Handling

| Scenario | Handling |
|----------|----------|
| App ID not found | `FetchAppInfoAsync` returns null; dialog shows "Game not found" |
| Name search returns no results | Dialog shows "No results" message |
| Anonymous login fails | Fall back to Steam Store Web API only (limited depot info — no manifests) |
| No depots with manifests | Dialog shows "No downloadable depots found for this game" |
| DepotDownloader.dll missing | Entry set to Error with message "DepotDownloader.dll not found in plugin directory" |
| DepotDownloader process crashes | Subprocess exit code != 0 → entry set to Error with stderr output as message |
| Steam CDN unavailable | Subprocess error → entry set to Error status |
| User cancels mid-download | Kill subprocess; entry set to Paused; DepotDownloader resumes on next start |
| installdir missing from URI | Use AppId as fallback directory name |

## Testing Strategy

- `SteamApiClient` — test with mock HTTP responses (Store API). SteamKit2 integration tests require network and are skipped in CI.
- `SteamDepotDownloader` — test URI parsing and subprocess command construction with a mock DepotDownloader that writes predictable output. Test pause/cancel state transitions.
- `SteamDepotPickerDialogViewModel` — test selection logic, Windows-default pre-selection, filtering.
- Pipeline branching — test `ProcessSteamEntryAsync` with a mock `IDownloader` to verify the steam path skips extraction and goes directly to Playnite integration.

## Out of Scope (v1)

- Authenticated Steam login (anonymous only)
- Steam Guard / 2FA
- DLC depot auto-detection and unlocking
- Download speed limiting
- Depot delta patches (full downloads only)
- Multi-language depot filtering logic (show all, let user pick)
- Parallel depot downloads (download one depot at a time, same as existing queue behavior)
- Auto-updating DepotDownloader.dll
