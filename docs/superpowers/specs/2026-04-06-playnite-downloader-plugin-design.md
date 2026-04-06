# Playnite Downloader Plugin — Design Spec

**Date:** 2026-04-06
**Type:** Playnite Generic Plugin (C# / WPF)
**Status:** Approved

---

## Overview

A Playnite Generic Plugin that allows users to search for game downloads from community repack sources, resolve links via Real-Debrid or direct HTTP, download, extract, and automatically register the game as installed in Playnite — all without leaving the app.

Games are assumed to already exist in Playnite (added as empty entries with IGDB metadata). The plugin handles everything from source search through to marking the game playable.

---

## Architecture

The plugin follows a **modular layered architecture**, where each layer has a single clear purpose, communicates through well-defined interfaces, and can be tested independently.

```
PlayniteDownloaderPlugin (GenericPlugin subclass)
├── UI Layer
│   ├── SidePanel (WPF UserControl)     — queue, progress, settings
│   └── SearchDialog (WPF Window)       — pre-filled game name, search results
│
├── Source Layer
│   ├── ISourceProvider (interface)
│   ├── JsonSourceProvider              — fetches & parses a JSON source URL
│   └── SourceManager                   — loads/manages built-in + custom sources
│
├── Download Layer
│   ├── IDownloader (interface)
│   ├── RealDebridClient                — RD API wrapper (auth, magnet, unrestrict)
│   ├── HttpDownloader                  — resumable HTTP downloader with retry
│   └── DownloaderFactory               — selects RD or direct based on URI + config
│
├── Pipeline Layer
│   ├── DownloadQueue                   — ordered queue, persisted to disk
│   ├── ExtractionService               — zip/rar/7z via SharpCompress
│   └── InstallResolver                 — finds best .exe in extracted folder
│
└── Integration Layer
    └── PlayniteIntegration             — sets IsInstalled, InstallDirectory, GameActions
```

### Playnite SDK Registration

The `PlayniteDownloaderPlugin` class extends `GenericPlugin` and overrides:

- `GetSidebarItems()` — returns a `SidebarItem` whose `Activated` callback returns the `SidePanel` `UserControl`. This is how the side panel appears in Playnite's sidebar.
- `GetGameMenuItems(GetGameMenuItemsArgs args)` — returns a `GameMenuItem` with description "Search Downloads". When clicked, opens `SearchDialog` pre-filled with `args.Games.First().Name`.

---

## Source Layer

### DownloadResult

Returned by `ISourceProvider.SearchAsync`. Carries everything needed to display a result and enqueue a download:

```csharp
public class DownloadResult
{
    public string Title { get; set; }          // matched game title from source
    public string SourceId { get; set; }       // which source this came from
    public string SourceName { get; set; }     // display name of source
    public List<string> Uris { get; set; }     // magnet / direct / hoster links
    public string FileSize { get; set; }       // display string, e.g. "20 GB"
    public string UploadDate { get; set; }     // display string
    public float MatchScore { get; set; }      // fuzzy match score (0.0–1.0)
}
```

### JSON Schema (Hydra-compatible)

Sources follow the same format as hydralinks.cloud / Hydra Launcher:

```json
{
  "name": "FitGirl Repacks",
  "downloads": [
    {
      "title": "Game Title",
      "uris": ["magnet:?xt=...", "https://direct-link"],
      "fileSize": "20 GB",
      "uploadDate": "2024-01-15"
    }
  ]
}
```

Each `downloads` entry:
- `title` — game name, fuzzy-matched against the Playnite game name
- `uris` — one or more links (magnet, direct HTTP, or hosted file hoster)
- `fileSize` — display string only
- `uploadDate` — display string only

### ISourceProvider Interface

```csharp
public interface ISourceProvider
{
    string Id { get; }
    string Name { get; }
    Task<List<DownloadResult>> SearchAsync(string gameName, CancellationToken ct);
}
```

### SourceManager

- `builtin-sources.json` ships alongside the plugin DLL in the plugin's installation folder (not in ExtensionsData). It is read-only and loaded at runtime using `Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "builtin-sources.json")`.
- Users add custom sources via the side panel by pasting any compatible JSON URL.
- All active sources (merged built-in + custom) stored in `sources.json` in the plugin data directory (`GetPluginUserDataPath()`):

```json
[
  {
    "id": "uuid",
    "name": "FitGirl Repacks",
    "url": "https://hydralinks.cloud/sources/fitgirl.json",
    "enabled": true,
    "isCustom": false,
    "addedAt": "2026-04-06T00:00:00Z"
  }
]
```

- On search: fetch each enabled source with a browser-like `User-Agent` header (required for Cloudflare-protected sources like hydralinks.cloud), fuzzy-match `title` against game name, return results ranked by `MatchScore` descending.
- Duplicate URL detection on add.

---

## Download Layer

### IDownloader Interface

```csharp
public interface IDownloader
{
    event Action<DownloadProgress> ProgressChanged;
    // savePath is a directory; the downloader derives the filename from Content-Disposition
    // or the URL and creates the file within that directory.
    Task StartAsync(string url, string savePath, CancellationToken ct);
    void Pause();
    void Resume();
    void Cancel(bool deleteFile = true);
    DownloadProgress GetStatus();
}

public class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public string FileName { get; set; }
    public DownloaderStatus Status { get; set; }
}

public enum DownloaderStatus { Active, Paused, Complete, Error }
```

### RealDebridClient

Wraps the Real-Debrid REST API v1 (`https://api.real-debrid.com/rest/1.0`). Ported from Hydra's `real-debrid.ts`.

**Key methods:**
- `Authorize(apiToken)` — initialises `HttpClient` with `Authorization: Bearer {token}`
- `GetDownloadUrl(uri, CancellationToken ct)` — unified entry point:
  - If `magnet:` → `AddMagnet` → `SelectAllFiles` → poll `GetTorrentInfo` every 5s until `status == "downloaded"` or timeout (30 minutes) or `ct` cancelled → `UnrestrictLink` all `links[]` → return `List<string>` of direct URLs (one per part)
  - Else (hosted file) → `UnrestrictLink(uri)` → return single-element `List<string>`
- `GetUser()` — validates token and premium status
- `AddMagnet(magnet)` → `POST /torrents/addMagnet`
- `GetTorrentInfo(id)` → `GET /torrents/info/{id}`
- `SelectAllFiles(id)` → `POST /torrents/selectFiles/{id}` with `files=all`
- `UnrestrictLink(link)` → `POST /unrestrict/link`

**Note on multi-file torrents:** `GetDownloadUrl` for magnets returns all links from `links[]`, not just `links[0]`. Multi-part repacks (e.g., split RAR archives) will produce multiple direct URLs. The `DownloadQueue` worker downloads each part sequentially into the same `DownloadPath` folder before triggering extraction.

**RD Torrent statuses handled:** `waiting_files_selection`, `queued`, `downloading`, `downloaded`, `error`, `dead`

**Polling:** 5-second interval, 30-minute maximum wait, honours `CancellationToken` on each poll iteration.

### HttpDownloader

Implements `IDownloader`. Ported from Hydra's `JsHttpDownloader.ts`.

**Features:**
- Resume: checks existing file size on disk, sends `Range: bytes=N-` header
- Stall detection: if no bytes received for 8s, abort and retry
- Retry with exponential backoff: up to 10 attempts, max 15s delay
- Speed tracking: bytes/elapsed per 1s window
- `Content-Disposition` filename parsing (`filename*` preferred over `filename`)
- Pause / cancel / resume support
- Fires `ProgressChanged` event on each chunk

### DownloaderFactory

```csharp
public static async Task<(List<string> urls, bool usedRealDebrid)> ResolveUrlsAsync(
    string uri, UserConfig config, CancellationToken ct)
{
    bool isMagnet = uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
    bool isHostedFile = IsKnownHoster(uri); // checks domain against known hoster list

    if (isMagnet || isHostedFile)
    {
        if (!config.RealDebridEnabled)
            throw new InvalidOperationException(
                "Real-Debrid is required for magnet links and hosted file links. " +
                "Enable Real-Debrid in settings or choose a direct HTTP link.");
        var rdClient = new RealDebridClient(config.RdApiToken);
        return (await rdClient.GetDownloadUrl(uri, ct), true);
    }

    // Plain HTTP — use as-is
    return (new List<string> { uri }, false);
}
```

`IsKnownHoster` checks the URI hostname against a known list of file hosters (e.g., 1fichier.com, rapidgator.net, mega.nz) that require RD to resolve. If RD is disabled and a hoster URI is selected, a clear error is shown to the user — not a silent fallback.

---

## Pipeline Layer

### DownloadQueue

- One active download at a time (sequential model)
- Queue persisted to `queue.json` in `GetPluginUserDataPath()` — survives Playnite restarts
- Supports: enqueue, pause, resume, cancel, reorder
- When a new download is enqueued, any currently active download is paused. On resume, `QueueEntry.CurrentUrlIndex` identifies which part to continue from; `HttpDownloader` resumes that part via `Range: bytes=N-` using the existing partial file on disk — the partial file is never discarded on pause.

**QueueEntry:**
```csharp
public class QueueEntry
{
    public string Id { get; set; }
    public string GameId { get; set; }            // Playnite Game.Id (Guid as string)
    public string GameName { get; set; }
    public string OriginalUri { get; set; }       // as selected by user
    public List<string> ResolvedUrls { get; set; } // after RD resolution (may be multiple)
    public int CurrentUrlIndex { get; set; }      // for multi-part progress
    public string DownloadPath { get; set; }      // folder where archives are saved
    public string ExtractionPath { get; set; }    // subfolder: DownloadPath\GameName\
    public DownloadStatus Status { get; set; }
    public float Progress { get; set; }           // 0.0–1.0 overall download
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public float ExtractionProgress { get; set; } // 0.0–1.0
    public DateTime AddedAt { get; set; }
}
```

**DownloadStatus:** `Waiting | Active | Paused | Error | Complete | Extracting`

**Extraction path:** archives are downloaded to `DownloadPath\{GameName}\`. Extraction output goes to the same folder (`ExtractionPath = DownloadPath\{GameName}\`). This is also the value passed to `game.InstallDirectory`.

### ExtractionService

- Uses **SharpCompress** NuGet package
- Detects extractable extensions: `.zip`, `.rar`, `.7z`, `.tar`, `.tar.gz`
- For multi-part archives (`.part1.rar`, `.r00`, etc.) — only triggers extraction on the first part; SharpCompress handles the rest automatically
- Tracks `extractionProgress` (0.0–1.0) via entry count; fires progress callback
- If no top-level archive file is found, scans `ExtractionPath` directory recursively for archives
- `ExtractAsync(string archivePath, string outputPath, Action<float> onProgress, CancellationToken ct)`

### InstallResolver

- Recursively scans `outputPath` for `.exe` files
- Scores candidates:
  - Penalise (score -= 100): filename contains `setup`, `install`, `unins`, `redist`, `vc_`, `directx`, `vcredist`, `dxsetup`
  - Reward (score += file size in MB / 10): larger executables preferred
  - Penalise (score -= depth * 5): prefer shallower paths
- Returns path of highest-scoring `.exe`, or `null` if none found

---

## UI Layer

### SidePanel (WPF UserControl)

Registered via `GetSidebarItems()` override on the plugin. Always accessible from the Playnite sidebar. Sections:

1. **Active Download** — game name, progress bar (download + extraction), speed, ETA, pause/cancel buttons
2. **Queue** — ordered list of waiting downloads; cancel individual entries
3. **Completed** — recent completed/failed entries with status and error message if applicable
4. **Settings** — RD API token input with "Verify" button, default download path picker, source list (enable/disable toggles, add by URL, remove custom sources)

### SearchDialog (WPF Window)

Triggered from `GetGameMenuItems()` override → "Search Downloads" menu item.

- Game name pre-filled from `game.Name`, editable before searching
- Search button → calls `SourceManager.SearchAsync(gameName, ct)`
- Results table columns: Title, Source, File Size, Upload Date, # Links
- Selecting a result expands a URI list below — user picks which link to use (displayed as type: Magnet / Direct / Hoster)
- Download path field (defaults to `config.DefaultDownloadPath`), Browse button
- "Download" button → calls `DownloaderFactory.ResolveUrlsAsync` → enqueues to `DownloadQueue`; dialog closes

---

## Full Data Flow

```
User right-clicks game → GetGameMenuItems() → "Search Downloads"
    → SearchDialog opens (game.Name pre-filled)
    → User edits name if needed → clicks Search
    → SourceManager.SearchAsync(gameName, ct)
        → foreach enabled source: GET source URL (spoofed User-Agent)
        → fuzzy match downloads[].title against gameName (FuzzySharp)
        → return ranked List<DownloadResult>
    → Results displayed in SearchDialog
    → User picks result, selects URI, confirms download path → clicks Download
    → DownloaderFactory.ResolveUrlsAsync(uri, config, ct)
        → if magnet/hoster + RD enabled → RealDebridClient.GetDownloadUrl(uri, ct)
            → returns List<string> of direct URLs (1 or more parts)
        → if plain HTTP → returns [uri]
        → if magnet/hoster + RD disabled → throws, shows error to user
    → DownloadQueue.Enqueue(entry) → dialog closes

DownloadQueue background Task:
    → foreach resolvedUrl in entry.ResolvedUrls:
        → HttpDownloader.StartAsync(url, entry.DownloadPath, ct)
            → ProgressChanged events → QueueEntry updated → SidePanel refreshes
    → All parts downloaded →
    → entry.Status = Extracting
    → Scan entry.DownloadPath for archive entry point:
        → *.part1.rar  — new-style multi-part RAR (SharpCompress entry point)
        → *.rar where a sibling *.r00 exists — old-style multi-part RAR (use *.rar, NOT *.r00)
        → *.zip, *.7z, *.tar — single-volume archives
        → Note: *.r00 is the second volume in old-style RAR sets, never a valid entry point
        → if none found, ExtractionService falls back to recursive scan of ExtractionPath
    → ExtractionService.ExtractAsync(archivePath, entry.ExtractionPath, onProgress, ct)
        → onProgress → QueueEntry.ExtractionProgress updated → SidePanel refreshes
    → Extraction complete →
    → InstallResolver.FindExecutable(entry.ExtractionPath) → exePath (nullable)
    → PlayniteIntegration.MarkInstalled(gameId, entry.ExtractionPath, exePath)
        → game.IsInstalled = true
        → game.InstallDirectory = entry.ExtractionPath
        → game.GameActions = exePath != null
            ? [new GameAction { Path = exePath, Type = GameActionType.File, IsPlayAction = true }]
            : []
        → playniteApi.Database.Games.Update(game)
    → entry.Status = Complete
```

---

## Configuration

Stored in `config.json` in `GetPluginUserDataPath()`:

```json
{
  "realDebridEnabled": true,
  "realDebridApiToken": "...",
  "defaultDownloadPath": "C:\\Games\\Downloads",
  "maxConcurrentDownloads": 1
}
```

---

## Persistence Files

| Location | File | Purpose |
|----------|------|---------|
| Plugin install dir (read-only) | `builtin-sources.json` | Curated hydralinks.cloud source URLs shipped with plugin |
| `GetPluginUserDataPath()` | `config.json` | User settings (RD token, paths) |
| `GetPluginUserDataPath()` | `sources.json` | Active source list (merged built-in + custom) |
| `GetPluginUserDataPath()` | `queue.json` | Persisted download queue |

---

## Dependencies (NuGet)

| Package | Purpose |
|---------|---------|
| `SharpCompress` | zip/rar/7z extraction |
| `FuzzySharp` | Fuzzy title matching for source search |
| `Newtonsoft.Json` | JSON serialisation (Playnite standard) |
| `PlayniteSDK` | Playnite plugin API |

---

## Error Handling

| Scenario | Behaviour |
|---------|-----------|
| Source fetch fails | Skip source, log warning, continue with others |
| RD token invalid / not premium | Show error in SidePanel, prompt to re-enter token |
| RD torrent status `error`/`dead` | Mark queue entry as Error, show descriptive message |
| Magnet/hoster URI selected with RD disabled | Show error dialog before enqueueing |
| RD polling timeout (30 min) | Mark as Error: "Real-Debrid took too long to process torrent" |
| Download stall | Auto-retry with exponential backoff (up to 10x) |
| Max retries exceeded | Mark as Error in queue |
| Extraction fails | Mark as Error, leave downloaded archive intact |
| No exe found after extraction | Mark as Complete with warning; open extracted folder for user |
| Playnite game not found at install time | Log warning, skip install registration |

---

## Out of Scope (v1)

- Parallel downloads (queue is sequential)
- Downloads surviving Playnite process exit (background service)
- Torbox, AllDebrid, Premiumize (RD only for v1)
- Seeding after download
- Auto-update of built-in source list from library.hydra.wiki
- GOG / Epic / other store install flows
