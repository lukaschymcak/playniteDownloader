# Auto Update Monitoring — Design Spec
**Date:** 2026-04-07  
**Branch:** feature/steam-library  

---

## Overview

Automatically detect when a Steam game installed through BlankPlugin has a newer version available, without consuming Morrenus API quota. Notify the user via Playnite toast and a visual badge in the InstalledGamesPanel. Let the user trigger an update through a right-click context menu offering two paths: re-download via Morrenus API or supply a local manifest ZIP.

---

## Architecture

### New components

```
ManifestChecker/               ← new .NET 9 project in the solution
  ManifestChecker.csproj
  Program.cs                   ← entry point: accepts AppIDs as args, prints JSON to stdout

deps/
  ManifestChecker.exe          ← built output, copied to plugin deps
  ManifestChecker.runtimeconfig.json

BlankPlugin/source/Pipeline/
  ManifestCheckerRunner.cs     ← shells out to ManifestChecker.exe, parses stdout JSON
  UpdateChecker.cs             ← orchestrates background + on-demand checks, owns results cache

BlankPlugin/source/UI/
  UpdateGameDialog.cs          ← "Update via API" / "Update via ZIP" choice dialog
```

### Modified components

- `InstalledGamesPanel.cs` — add per-card update badge, subscribe to UpdateChecker events
- `BlankPlugin.cs` — instantiate UpdateChecker, wire OnApplicationStarted, OnLibraryUpdated, GetGameMenuItems

---

## ManifestChecker.exe

A minimal .NET 9 console app. Accepts AppIDs as space-separated command-line arguments:

```
ManifestChecker.exe 730 570 1091500
```

**Behavior:**
1. Anonymous login to Steam via SteamKit2
2. Calls `GetProductInfo(apps)` in batches of 20 with a 300ms delay between batches
3. For each app, extracts all public depot manifest GIDs from the response
4. Prints a JSON array to stdout and exits 0:

```json
[
  {"appId": "730", "depotId": "731", "manifestGid": "1234567890123456789"},
  {"appId": "730", "depotId": "232", "manifestGid": "9876543210987654321"}
]
```

**On failure:** prints `{"error": "reason"}` to stderr, exits non-zero.

---

## ManifestCheckerRunner.cs

Mirrors the pattern of `DepotDownloaderRunner`. Shells out to `ManifestChecker.exe` via `Process`, captures stdout, deserializes the JSON array into a `List<ManifestCheckResult>`.

```csharp
public class ManifestCheckResult
{
    public string AppId { get; set; }
    public string DepotId { get; set; }
    public string ManifestGid { get; set; }
}
```

Returns `(List<ManifestCheckResult> results, string error)`. If the process exits non-zero or stdout is unparseable, returns `(null, errorMessage)`.

---

## UpdateChecker.cs

Single instance owned by `BlankPlugin`. Exposes:

```csharp
public event Action<string, string> GameUpdateStatusChanged; // (appId, status)
Task RunAsync(IEnumerable<InstalledGame> games, CancellationToken ct);
```

**Logic:**
1. Filter out games with empty `ManifestGIDs` (installed before this feature) — skip silently.
2. Pass all remaining AppIDs to `ManifestCheckerRunner`.
3. For each returned `ManifestCheckResult`, compare `manifestGid` against the matching entry in `InstalledGame.ManifestGIDs`.
4. Write the result string (`"up_to_date"` | `"update_available"` | `"cannot_determine"`) into a local in-memory dictionary keyed by AppId.
5. Fire `GameUpdateStatusChanged` for each game.
6. For any games with `update_available`, push a single grouped Playnite notification listing all their names.

**Concurrency:** Uses a `SemaphoreSlim(1)` — if a check is already running when a second trigger arrives, the second is dropped.

**Cancellation:** Honors the `CancellationToken` between batches; kills the `ManifestChecker.exe` process if cancelled mid-run.

---

## Data Flow

### Background check (startup + OnLibraryUpdated)

```
BlankPlugin.OnApplicationStarted()
BlankPlugin.OnLibraryUpdated()
    └─► UpdateChecker.RunAsync(installedGames, ct)
            ├─ Filter games with ManifestGIDs
            ├─ ManifestCheckerRunner → ManifestChecker.exe → Steam anonymous API
            ├─ Compare manifests → update in-memory status dict
            ├─ Persist status to InstalledGame (InstalledGamesManager.Save())
            ├─ Toast notification (grouped, one per check run)
            └─ Fire GameUpdateStatusChanged → InstalledGamesPanel.RefreshList()
```

### On-demand update (right-click menu)

```
GetGameMenuItems → "Update Game"
    └─► UpdateGameDialog
            ├─ "Update via API"  → GameWindow (existing pipeline, AppId pre-filled)
            └─ "Update via ZIP"  → OpenFileDialog → ZipProcessor → download changed depots
```

---

## UI

### InstalledGamesPanel badge

Each game card shows a small inline label after the game name:
- **`[Update Available]`** — orange/amber color — when status is `update_available`
- **`[Checking...]`** — gray — while UpdateChecker.RunAsync is in progress
- Nothing extra — when `up_to_date` or `cannot_determine`

`InstalledGamesPanel` subscribes to `UpdateChecker.GameUpdateStatusChanged` and calls `RefreshList()` when it fires.

### Playnite toast notification

One notification per check run (not per game). If 1 game: `"Update available for Half-Life 2"`. If multiple: `"Updates available for 3 games: Half-Life 2, Portal 2, CS2"`. Uses `PlayniteApi.Notifications.Add(new NotificationMessage(...))`.

### Right-click context menu

Added to `GetGameMenuItems` under `MenuSection = "BlankPlugin"`:
- `"Update Game"` — shown for all plugin-installed games

`UpdateGameDialog` is a simple `PlayniteApi.Dialogs.CreateWindow()` with:
- Status line: last-checked manifest GIDs and current status
- `"Update via API"` button → opens `GameWindow`
- `"Update via ZIP"` button → file picker, then re-runs ZipProcessor + DepotDownloaderRunner for changed depots only

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Steam offline / login fails | `ManifestChecker.exe` exits non-zero; games marked `cannot_determine`; no notification; silent |
| Game has no `ManifestGIDs` | Skipped entirely; no badge shown |
| `ManifestChecker.exe` missing | Log warning; "Check for Updates" button shows error dialog |
| Two triggers overlap | Second run dropped via semaphore |
| Plugin unloaded mid-check | CancellationToken cancels; process killed |

---

## Out of Scope

- Automatic re-download without user confirmation
- Morrenus API used for update checking (intentionally avoided — 25/day limit)
- Per-depot granularity in the UI (update dialog shows game-level, not depot-level)
