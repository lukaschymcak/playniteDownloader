# Update System Fixes — Design Spec
**Date:** 2026-04-07
**Branch:** feature/auto-update-monitoring

---

## Overview

Three related fixes to the auto-update system:

1. **ManifestChecker build pipeline** — `ManifestChecker.exe` crashes on launch because only the bootstrapper EXE is copied to `deps/`, not the managed DLL. Fix: use `dotnet publish` to produce a true single-file exe.
2. **Playnite Tags** — `UpdateChecker` has no way to surface update status on game cards. Fix: add/remove a `"Update Available"` tag on Playnite game records. Also surface check failures as a notification instead of silent log.
3. **UpdateWindow** — "Update via API/ZIP" in `UpdateGameDialog` opens the full `GameWindow`, which discards pre-parsed ZIP data and has no update-specific UX. Fix: new minimal `UpdateWindow` that knows the existing install, pre-fills the path and depot selection, and updates the `InstalledGame` record on completion.

---

## Part 1 — ManifestChecker Build Pipeline

### Root cause

`ManifestChecker.csproj` has `PublishSingleFile=true` but `dotnet build` ignores this flag — it only applies during `dotnet publish`. A regular build produces a multi-file output:

- `ManifestChecker.exe` — tiny native bootstrapper only
- `ManifestChecker.dll` — the actual managed code
- `SteamKit2.dll`, `Newtonsoft.Json.dll`, etc. — dependencies

The `CopyManifestChecker` target in `BlankPlugin.csproj` copies only the bootstrapper EXE and `runtimeconfig.json`. The bootstrapper immediately crashes when launched because it cannot find `ManifestChecker.dll` alongside it. `ManifestCheckerRunner` catches this as an exception, returns `(null, error)`, and `UpdateChecker` sets all games to `cannot_determine` with no user-visible output.

### Fix

Add a `PublishManifestChecker` target to `BlankPlugin.csproj` that runs `dotnet publish` before the copy step. The publish output (`publish/ManifestChecker.exe`) is a true single-file exe that bundles the managed DLL and works with no companion files (only requires the .NET 9 runtime on the machine, which is already a stated requirement).

Update the `CopyManifestChecker` target to copy from the publish output folder instead of the build output folder.

**Files changed:** `BlankPlugin/source/BlankPlugin.csproj` only. No changes to `ManifestCheckerRunner.cs`.

### Exact changes to `BlankPlugin.csproj`

Replace the existing `CopyManifestChecker` target with:

```xml
<!-- Publish ManifestChecker to single-file exe before copying -->
<Target Name="PublishManifestChecker" BeforeTargets="CopyManifestChecker">
  <Exec
    Command="dotnet publish &quot;$(SolutionDir)..\..\ManifestChecker\ManifestChecker.csproj&quot; -c $(Configuration) --nologo -v quiet"
    ContinueOnError="true" />
</Target>

<!-- Copy published single-file exe into deps/ -->
<Target Name="CopyManifestChecker" BeforeTargets="PackPlugin">
  <Copy
    SourceFiles="$(SolutionDir)..\..\ManifestChecker\bin\$(Configuration)\net9.0\publish\ManifestChecker.exe"
    DestinationFolder="$(OutDir)deps\"
    SkipUnchangedFiles="true"
    ContinueOnError="true" />
  <Copy
    SourceFiles="$(SolutionDir)..\..\ManifestChecker\bin\$(Configuration)\net9.0\publish\ManifestChecker.runtimeconfig.json"
    DestinationFolder="$(OutDir)deps\"
    SkipUnchangedFiles="true"
    ContinueOnError="true" />
</Target>
```

---

## Part 2 — Playnite Tags in UpdateChecker

### What gets added

Inside `UpdateChecker.RunAsync()`, after each game's status is resolved, apply Playnite tag changes:

| Status | Tag action |
|--------|-----------|
| `update_available` | Add `"Update Available"` tag to the Playnite game |
| `up_to_date` | Remove `"Update Available"` tag if present |
| `cannot_determine` | No change to tag state |

Tag is retrieved or created via `PlayniteApi.Database.Tags.Add("Update Available")`, which returns the existing tag if the name already exists. All game record changes are applied in a single `PlayniteApi.Database.BufferedUpdate()` block at the end of the run.

Lookup: use `InstalledGame.PlayniteGameId` to find the game via `PlayniteApi.Database.Games.Get(playniteGameId)`. If the game is not found (deleted from library), skip silently.

### Failure visibility

When `ManifestCheckerRunner.Run()` returns `(null, error)`, push a Playnite notification:

```
"BlankPlugin: Update check failed — {error}"
```

Currently this path only logs a warning. The user has no way to know the check ran and failed.

### Files changed

`BlankPlugin/source/Pipeline/UpdateChecker.cs` only. This is an untracked new file — safe to modify without a backup.

---

## Part 3 — New UpdateWindow

### Purpose

A minimal update-specific window that:
- Knows the existing `InstalledGame` (install path, selected depots, AppId)
- For the API path: fetches a fresh manifest from Morrenus, then shows depot selection
- For the ZIP path: uses the already-parsed `GameData` directly (no network call)
- Pre-fills install path and pre-checks the previously selected depots
- After successful download: updates the `InstalledGame` record and removes the "Update Available" tag

### New file: `UI/UpdateWindow.cs`

**Constructor:**
```csharp
public UpdateWindow(
    InstalledGame existingGame,
    BlankPluginSettings settings,
    InstalledGamesManager gamesManager,
    IPlayniteAPI api,
    GameData preloadedData = null)
```

**On load behavior:**
- If `preloadedData == null` (API path): background thread calls `MorrenusClient.DownloadManifest(existingGame.AppId)` then `ZipProcessor.Process()`. Status label shows "Fetching manifest…" until complete.
- If `preloadedData != null` (ZIP path): depot list is populated immediately. Status label shows "Ready".

**UI layout (top to bottom):**
1. Header label: `"Updating: {existingGame.GameName}"`
2. Status label: `"Fetching manifest…"` / `"Ready to update"` / `"Downloading…"`
3. Depot checkbox list — each depot from the manifest; pre-checked if depot ID is in `existingGame.SelectedDepots`
4. Install path TextBox — pre-filled with `existingGame.InstallPath`, user-editable
5. `"Update Game"` button (disabled until manifest loaded and at least one depot checked)
6. ProgressBar
7. Log TextBox (scrollable, read-only)

**Not included** (belongs to fresh install only): search panel, API status bar, library registration checkbox, DRM strip checkbox, usage label.

**On successful download:**
1. `existingGame.ManifestGIDs = new Dictionary<string, string>(freshGameData.Manifests)`
2. `existingGame.SelectedDepots = [checked depot IDs]`
3. `existingGame.SizeOnDisk` = recalculated by summing file sizes in `existingGame.InstallPath`
4. `gamesManager.Save(existingGame)`
5. Look up Playnite game via `existingGame.PlayniteGameId` → remove `"Update Available"` tag from `TagIds` → `api.Database.Games.Update(playniteGame)`

### Changes to `UpdateGameDialog.cs`

`UpdateGameDialog.cs` is an untracked new file — safe to modify.

`OnUpdateViaApi()`: close current window, open `UpdateWindow(installed, settings, gamesManager, api)`.

`OnUpdateViaZip()`: parse ZIP into `freshData` as before (keep validation), then close current window, open `UpdateWindow(installed, settings, gamesManager, api, freshData)`.

Remove the now-unused `GameWindow` import/usage from both methods.

---

## Files Changed Summary

| File | Status | Change |
|------|--------|--------|
| `BlankPlugin/source/BlankPlugin.csproj` | Modified (M) | Add `PublishManifestChecker` target; update `CopyManifestChecker` to use publish output |
| `BlankPlugin/source/Pipeline/UpdateChecker.cs` | Untracked new (??) | Add tag logic + failure notification |
| `BlankPlugin/source/UI/UpdateWindow.cs` | New file | Create update-specific window |
| `BlankPlugin/source/UI/UpdateGameDialog.cs` | Untracked new (??) | Replace `GameWindow` calls with `UpdateWindow` |

No existing committed files are modified.
