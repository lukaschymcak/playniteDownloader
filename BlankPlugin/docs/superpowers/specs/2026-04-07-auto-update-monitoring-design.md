# Auto Update Monitoring — Design Spec
**Date:** 2026-04-07  
**Branch:** feature/steam-library

---

## Overview

Automatically detect when a Steam game installed through BlankPlugin has a newer version available, without consuming Morrenus API quota (limit: 25/day). Uses the Steam internal API (anonymous SteamKit2 login) to compare manifest GIDs. Notifies the user via Playnite toast and a visual badge in `InstalledGamesPanel`. A right-click "Update Game" menu item opens a dialog letting the user re-download via Morrenus API or supply a local manifest ZIP.

---

## Solution Structure Changes

The solution file is at `BlankPlugin/source/BlankPlugin.sln`. A **second project** must be added to it:

```
BlankPlugin/
  source/
    BlankPlugin.sln                          ← add ManifestChecker project reference here
    BlankPlugin.csproj                       ← unchanged
    ...existing files...
    Pipeline/
      ManifestCheckerRunner.cs               ← NEW
      UpdateChecker.cs                       ← NEW
    UI/
      UpdateGameDialog.cs                    ← NEW

ManifestChecker/                             ← NEW project folder (sibling to BlankPlugin/source/)
  ManifestChecker.csproj                     ← NEW
  Program.cs                                 ← NEW
```

The `ManifestChecker.exe` output must be copied into the plugin's `deps/` folder at build time so it is included in the `.pext` package.

---

## Part 1: ManifestChecker Project

### 1.1 — Create `ManifestChecker/ManifestChecker.csproj`

This is a standalone .NET 9 console application. It has no reference to Playnite or BlankPlugin.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>ManifestChecker</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SteamKit2" Version="3.*" />
    <PackageReference Include="Newtonsoft.Json" Version="13.*" />
  </ItemGroup>
</Project>
```

> **Note:** Use `SteamKit2` version 3.x from NuGet. Do NOT reference the `SteamKit2.dll` already in `BlankPlugin/source/deps/` — that one was compiled for DepotDownloader and may be a different version.

### 1.2 — Create `ManifestChecker/Program.cs`

**Purpose:** Accept AppIDs as command-line arguments. For each AppID, query Steam anonymously to get the current public manifest GID per depot. Print a JSON array to stdout. Exit 0 on success, non-zero on failure.

**Command-line usage:**
```
ManifestChecker.exe 730 570 1091500
```

**Expected stdout on success:**
```json
[
  {"appId":"730","depotId":"731","manifestGid":"1234567890123456789"},
  {"appId":"730","depotId":"232","manifestGid":"9876543210987654321"},
  {"appId":"570","depotId":"571","manifestGid":"1111111111111111111"}
]
```

**Expected stderr on failure:**
```json
{"error":"Failed to login to Steam anonymously"}
```

**Full implementation logic:**

```csharp
using SteamKit2;
using Newtonsoft.Json;

// 1. Parse command-line args — each arg is a string AppID like "730"
// 2. Validate: if no args, print {"error":"No AppIDs provided"} to stderr, exit 1
// 3. Convert args to List<uint> (integer AppIDs for SteamKit2)
// 4. Create a SteamClient and connect:
//      var steamClient = new SteamClient();
//      var manager = new CallbackManager(steamClient);
//      var steamUser = steamClient.GetHandler<SteamUser>();
//      var steamApps = steamClient.GetHandler<SteamApps>();
//      steamClient.Connect();
// 5. Use a blocking callback loop with a timeout (30 seconds total)
//    Register callbacks:
//      - ConnectedCallback: call steamUser.LogOnAnonymous()
//      - DisconnectedCallback: set a "failed" flag, exit loop
//      - LoggedOnCallback: if Result != EResult.OK, set "failed", exit loop
//                          if Result == EResult.OK, proceed to fetch
// 6. After LoggedOnCallback succeeds, call steamApps.PICSGetProductInfo:
//      var request = new SteamApps.PICSRequest(appId);   // one per AppID
//      // Call PICSGetProductInfo with all AppIDs at once (or in batches of 20)
//      var job = await steamApps.PICSGetProductInfo(appIds.Select(id => new SteamApps.PICSRequest(id)), Enumerable.Empty<SteamApps.PICSRequest>());
// 7. Wait for PICSProductInfoCallback. For each app in the result:
//      - Access app.KeyValues["depots"] section
//      - For each depot key (numeric string like "731"):
//          - Get the "manifests" subsection
//          - Get the "public" subsection inside "manifests"
//          - Read the "gid" value — this is the current manifest GID string
//          - Add {"appId": appIdStr, "depotId": depotKey, "manifestGid": gidStr} to results list
// 8. Call steamUser.LogOff(), wait for DisconnectedCallback
// 9. Serialize results list to JSON and write to Console.Out
// 10. Exit 0
```

**Important SteamKit2 KeyValues navigation for step 7:**

```csharp
// kv is the KeyValue for a single app from PICSProductInfoCallback
var depots = kv["depots"];
foreach (var depot in depots.Children)
{
    // Skip non-numeric depot keys (e.g. "branches", "overrideversionid")
    if (!uint.TryParse(depot.Name, out _)) continue;

    var gid = depot["manifests"]["public"]["gid"].Value;
    if (!string.IsNullOrEmpty(gid) && gid != "0")
    {
        results.Add(new { appId = appIdStr, depotId = depot.Name, manifestGid = gid });
    }
}
```

**Timeout handling:** The entire operation must complete within 30 seconds. Use a `CancellationTokenSource` with a 30-second timeout. If it times out, print `{"error":"Timed out waiting for Steam response"}` to stderr and exit 1.

**Batching:** Process AppIDs in batches of 20 if more than 20 are provided. Call `PICSGetProductInfo` once per batch. Add a 300ms `Thread.Sleep` between batches to avoid rate limiting.

### 1.3 — Copy output to deps/

In `BlankPlugin/source/BlankPlugin.csproj`, add a post-build step that copies `ManifestChecker.exe` to the plugin's `deps/` folder:

```xml
<Target Name="CopyManifestChecker" AfterTargets="Build">
  <Copy
    SourceFiles="$(SolutionDir)..\..\ManifestChecker\bin\$(Configuration)\net9.0\ManifestChecker.exe"
    DestinationFolder="$(OutDir)deps\"
    SkipUnchangedFiles="true"
    ContinueOnError="true" />
  <Copy
    SourceFiles="$(SolutionDir)..\..\ManifestChecker\bin\$(Configuration)\net9.0\ManifestChecker.runtimeconfig.json"
    DestinationFolder="$(OutDir)deps\"
    SkipUnchangedFiles="true"
    ContinueOnError="true" />
</Target>
```

---

## Part 2: ManifestCheckerRunner.cs

**File:** `BlankPlugin/source/Pipeline/ManifestCheckerRunner.cs`  
**Namespace:** `BlankPlugin`  
**Pattern to follow:** Mirrors `DepotDownloaderRunner.cs` — same process-launching approach, same `FindDotnet()` / `GetPluginDir()` helper pattern.

### 2.1 — Data model

```csharp
public class ManifestCheckResult
{
    public string AppId { get; set; }
    public string DepotId { get; set; }
    public string ManifestGid { get; set; }
}
```

### 2.2 — Class structure

```csharp
public class ManifestCheckerRunner
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private readonly string _dotnetPath;       // path to dotnet.exe, found via FindDotnet()
    private readonly string _checkerExe;       // path to deps/ManifestChecker.exe

    public bool IsReady => !string.IsNullOrEmpty(_dotnetPath) && File.Exists(_checkerExe);

    public ManifestCheckerRunner()
    {
        _dotnetPath = FindDotnet();   // copy the FindDotnet() method from DepotDownloaderRunner.cs exactly
        _checkerExe = Path.Combine(GetPluginDir(), "deps", "ManifestChecker.exe");
    }

    /// <summary>
    /// Queries Steam for current manifest GIDs for the given AppIDs.
    /// Returns (results, null) on success or (null, errorMessage) on failure.
    /// </summary>
    public (List<ManifestCheckResult> results, string error) Run(IEnumerable<string> appIds)
    { ... }

    private static string GetPluginDir() => ...  // copy from DepotDownloaderRunner.cs
    private static string FindDotnet() => ...    // copy from DepotDownloaderRunner.cs
}
```

### 2.3 — Run() method implementation

```csharp
public (List<ManifestCheckResult> results, string error) Run(IEnumerable<string> appIds)
{
    if (!IsReady)
    {
        var msg = File.Exists(_checkerExe)
            ? "dotnet runtime not found on PATH."
            : "ManifestChecker.exe not found in plugin deps folder.";
        return (null, msg);
    }

    // Build args string: each AppID space-separated
    var args = string.Join(" ", appIds);
    if (string.IsNullOrWhiteSpace(args))
        return (null, "No AppIDs provided.");

    // Launch: dotnet ManifestChecker.exe <appid1> <appid2> ...
    // BUT ManifestChecker.exe is a self-contained native executable — do NOT prefix with dotnet
    // Just run ManifestChecker.exe directly:
    var psi = new ProcessStartInfo(_checkerExe, args)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    try
    {
        using (var proc = new Process { StartInfo = psi })
        {
            proc.Start();

            // Read stdout and stderr in parallel to avoid deadlocks
            var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

            bool exited = proc.WaitForExit(60_000); // 60 second timeout
            if (!exited)
            {
                try { proc.Kill(); } catch { }
                return (null, "ManifestChecker.exe timed out after 60 seconds.");
            }

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            if (proc.ExitCode != 0)
            {
                // Try to parse error from stderr JSON
                try
                {
                    var errObj = JsonConvert.DeserializeObject<Dictionary<string, string>>(stderr);
                    if (errObj != null && errObj.TryGetValue("error", out var errMsg))
                        return (null, errMsg);
                }
                catch { }
                return (null, "ManifestChecker.exe failed (exit code " + proc.ExitCode + "): " + stderr);
            }

            // Parse the JSON array from stdout
            var results = JsonConvert.DeserializeObject<List<ManifestCheckResult>>(stdout);
            return (results ?? new List<ManifestCheckResult>(), null);
        }
    }
    catch (Exception ex)
    {
        logger.Error("ManifestCheckerRunner failed: " + ex.Message);
        return (null, ex.Message);
    }
}
```

> **Note on execution:** `ManifestChecker.exe` is published as a self-contained .NET 9 executable (not a DLL). It does NOT need `dotnet` to prefix it — run it directly. This differs from `DepotDownloaderRunner` which runs `dotnet DepotDownloader.dll`.

---

## Part 3: UpdateChecker.cs

**File:** `BlankPlugin/source/Pipeline/UpdateChecker.cs`  
**Namespace:** `BlankPlugin`

### 3.1 — Full class signature

```csharp
public class UpdateChecker
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private readonly ManifestCheckerRunner _runner;
    private readonly InstalledGamesManager _gamesManager;
    private readonly IPlayniteAPI _playniteApi;

    // In-memory cache: AppId -> status string
    // Status values: "up_to_date" | "update_available" | "cannot_determine" | "checking"
    private readonly Dictionary<string, string> _statusCache
        = new Dictionary<string, string>();

    private readonly SemaphoreSlim _runLock = new SemaphoreSlim(1, 1);
    private CancellationTokenSource _cts;

    // Fires on the UI thread after each game's status is determined.
    // Args: (appId, status)
    public event Action<string, string> GameUpdateStatusChanged;

    public UpdateChecker(
        ManifestCheckerRunner runner,
        InstalledGamesManager gamesManager,
        IPlayniteAPI playniteApi)
    { ... }

    public string GetStatus(string appId) { ... }

    public Task RunAsync() { ... }

    public void Cancel() { ... }
}
```

### 3.2 — GetStatus() method

```csharp
public string GetStatus(string appId)
{
    return _statusCache.TryGetValue(appId, out var status) ? status : null;
}
```

### 3.3 — RunAsync() method

```csharp
public Task RunAsync()
{
    // If already running, drop this request silently
    if (!_runLock.Wait(0))
    {
        logger.Info("UpdateChecker.RunAsync: check already in progress, skipping.");
        return Task.CompletedTask;
    }

    _cts = new CancellationTokenSource();
    var token = _cts.Token;

    return Task.Run(() =>
    {
        try
        {
            var games = _gamesManager.GetAll();

            // Only check games that have saved manifest GIDs (i.e. were installed with this plugin version)
            var checkable = games
                .Where(g => g.ManifestGIDs != null && g.ManifestGIDs.Count > 0)
                .ToList();

            if (checkable.Count == 0)
            {
                logger.Info("UpdateChecker: no games with saved manifest GIDs to check.");
                return;
            }

            logger.Info("UpdateChecker: checking " + checkable.Count + " game(s).");

            // Mark all as "checking" so the UI can show the badge immediately
            foreach (var game in checkable)
            {
                _statusCache[game.AppId] = "checking";
                FireStatusChanged(game.AppId, "checking");
            }

            if (token.IsCancellationRequested) return;

            // Run ManifestChecker.exe with all AppIDs
            var appIds = checkable.Select(g => g.AppId).ToList();
            var (results, error) = _runner.Run(appIds);

            if (token.IsCancellationRequested) return;

            if (results == null)
            {
                logger.Warn("UpdateChecker: ManifestCheckerRunner failed: " + error);
                // Mark all as cannot_determine
                foreach (var game in checkable)
                {
                    _statusCache[game.AppId] = "cannot_determine";
                    FireStatusChanged(game.AppId, "cannot_determine");
                }
                return;
            }

            // Group results by AppId for easy lookup: appId -> list of (depotId, manifestGid)
            var resultsByApp = results
                .GroupBy(r => r.AppId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var updatesAvailable = new List<string>(); // game names with updates

            foreach (var game in checkable)
            {
                if (token.IsCancellationRequested) break;

                string status;

                if (!resultsByApp.TryGetValue(game.AppId, out var steamDepots) || steamDepots.Count == 0)
                {
                    // Steam returned no data for this app
                    status = "cannot_determine";
                }
                else
                {
                    // Compare each saved depot manifest GID against the Steam result
                    bool anyChanged = false;
                    bool anyFound = false;

                    foreach (var kv in game.ManifestGIDs)
                    {
                        var savedDepotId = kv.Key;
                        var savedGid = kv.Value;

                        var steamDepot = steamDepots.FirstOrDefault(d => d.DepotId == savedDepotId);
                        if (steamDepot == null) continue; // depot not in Steam response, skip

                        anyFound = true;
                        if (steamDepot.ManifestGid != savedGid)
                        {
                            anyChanged = true;
                            logger.Info(string.Format(
                                "Update detected for {0} depot {1}: saved={2} steam={3}",
                                game.GameName, savedDepotId, savedGid, steamDepot.ManifestGid));
                            break;
                        }
                    }

                    if (!anyFound)
                        status = "cannot_determine";
                    else if (anyChanged)
                        status = "update_available";
                    else
                        status = "up_to_date";
                }

                _statusCache[game.AppId] = status;
                FireStatusChanged(game.AppId, status);

                if (status == "update_available")
                    updatesAvailable.Add(game.GameName);
            }

            // Push a single grouped Playnite notification if any updates were found
            if (updatesAvailable.Count > 0 && !token.IsCancellationRequested)
            {
                string message;
                if (updatesAvailable.Count == 1)
                    message = "Update available for " + updatesAvailable[0];
                else
                    message = "Updates available for " + updatesAvailable.Count + " games: "
                              + string.Join(", ", updatesAvailable);

                _playniteApi.Notifications.Add(new NotificationMessage(
                    "blankplugin_updates_" + DateTime.Now.Ticks,
                    message,
                    NotificationType.Info));
            }
        }
        catch (Exception ex)
        {
            logger.Error("UpdateChecker.RunAsync failed: " + ex.Message);
        }
        finally
        {
            _runLock.Release();
        }
    }, token);
}
```

### 3.4 — FireStatusChanged() helper

```csharp
private void FireStatusChanged(string appId, string status)
{
    // Must be called on UI thread for WPF bindings to work
    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
        new Action(() => GameUpdateStatusChanged?.Invoke(appId, status)));
}
```

### 3.5 — Cancel() method

```csharp
public void Cancel()
{
    try { _cts?.Cancel(); } catch { }
}
```

---

## Part 4: Changes to InstalledGamesPanel.cs

**File:** `BlankPlugin/source/UI/InstalledGamesPanel.cs`

The panel needs to:
1. Accept an `UpdateChecker` parameter in its constructor
2. Subscribe to `UpdateChecker.GameUpdateStatusChanged`
3. Show a colored badge per game card in `CreateGameEntry()`

### 4.1 — Constructor changes

The current constructor signature is:
```csharp
public InstalledGamesPanel(InstalledGamesManager manager)
```

Change it to:
```csharp
public InstalledGamesPanel(InstalledGamesManager manager, UpdateChecker updateChecker)
```

Store the `updateChecker` in a private field:
```csharp
private readonly UpdateChecker _updateChecker;
```

In the constructor body, after storing the field, subscribe to status changes:
```csharp
_updateChecker = updateChecker;
if (_updateChecker != null)
    _updateChecker.GameUpdateStatusChanged += OnGameStatusChanged;
```

### 4.2 — OnGameStatusChanged handler

```csharp
private void OnGameStatusChanged(string appId, string status)
{
    // This is already called on the UI thread via Dispatcher.BeginInvoke in UpdateChecker
    RefreshList();
}
```

### 4.3 — Badge in CreateGameEntry()

Inside `CreateGameEntry(InstalledGame game)`, after the `nameText` TextBlock is created and added to `infoPanel` (around line 174 in the current file), add a badge:

```csharp
// Add update status badge if available
if (_updateChecker != null)
{
    var status = _updateChecker.GetStatus(game.AppId);
    if (status == "update_available")
    {
        var badge = new TextBlock
        {
            Text = "  [Update Available]",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)), // orange
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Add it inline after the game name in a horizontal WrapPanel
        // Replace the standalone nameText with a StackPanel that contains both:
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(nameText);
        nameRow.Children.Add(badge);
        infoPanel.Children.Add(nameRow);
    }
    else if (status == "checking")
    {
        var badge = new TextBlock
        {
            Text = "  [Checking...]",
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(nameText);
        nameRow.Children.Add(badge);
        infoPanel.Children.Add(nameRow);
    }
    else
    {
        // No badge: just add the plain nameText
        infoPanel.Children.Add(nameText);
    }
}
else
{
    infoPanel.Children.Add(nameText);
}
```

> **Important:** Remove the original `infoPanel.Children.Add(nameText)` line that was at line 174. The badge logic above replaces it.

---

## Part 5: UpdateGameDialog.cs

**File:** `BlankPlugin/source/UI/UpdateGameDialog.cs`  
**Namespace:** `BlankPlugin`

This is a simple UserControl (not a Window — the Window wrapper is created with `PlayniteApi.Dialogs.CreateWindow()` in BlankPlugin.cs).

### 5.1 — Constructor

```csharp
public class UpdateGameDialog : UserControl
{
    private readonly InstalledGame _game;
    private readonly BlankPluginSettings _settings;
    private readonly InstalledGamesManager _gamesManager;
    private readonly IPlayniteAPI _api;
    private readonly UpdateChecker _updateChecker;

    public UpdateGameDialog(
        InstalledGame game,
        BlankPluginSettings settings,
        InstalledGamesManager gamesManager,
        IPlayniteAPI api,
        UpdateChecker updateChecker)
    { ... }
}
```

### 5.2 — UI layout

Build the UI entirely in code (no XAML), following the same pattern as other UI files in the project.

The layout from top to bottom:
1. **Title TextBlock**: `"Update: " + game.GameName`, white, FontSize 14, Bold
2. **Status section**: A TextBlock showing the current update status:
   - If `updateChecker.GetStatus(game.AppId) == "update_available"`: `"Status: Update available"` in orange
   - If `"up_to_date"`: `"Status: Up to date"` in light green
   - Otherwise: `"Status: Unknown (run a check first)"` in gray
3. **Saved manifests section**: A TextBlock listing each depot and its saved GID:
   ```
   Saved manifests:
     Depot 731: 1234567890123456789
     Depot 732: 9876543210987654321
   ```
4. **Separator** (a 1px horizontal Border)
5. **Button row** (horizontal StackPanel):
   - `"Update via API"` button — calls `OnUpdateViaApi()`
   - `"Update via ZIP"` button — calls `OnUpdateViaZip()`
   - `"Close"` button — closes the parent window

### 5.3 — Button handlers

```csharp
private void OnUpdateViaApi()
{
    // Close this dialog window first
    Window.GetWindow(this)?.Close();

    // Open GameWindow pre-filled with this game's AppId
    // GameWindow is the existing download window in UI/GameWindow.cs
    // It already accepts a Game parameter — pass null and let the user search,
    // OR add an overload that accepts a pre-filled appId string.
    // For now: open GameWindow normally (the user knows the AppId)
    var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = true,
        ShowCloseButton = true
    });
    window.Title = "BlankPlugin — Update " + _game.GameName;
    window.Width = 700;
    window.Height = 600;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner = _api.Dialogs.GetCurrentAppWindow();
    window.Content = new GameWindow(null, _settings, _gamesManager, _api);
    window.ShowDialog();
}

private void OnUpdateViaZip()
{
    // Open a file picker for a .zip file
    var zipPath = _api.Dialogs.SelectFile("Manifest ZIP|*.zip");
    if (string.IsNullOrEmpty(zipPath)) return;

    // Process the ZIP using the existing ZipProcessor
    GameData freshData;
    try
    {
        freshData = new ZipProcessor().Process(zipPath);
    }
    catch (Exception ex)
    {
        MessageBox.Show("Failed to read ZIP: " + ex.Message, "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        return;
    }

    // Verify the ZIP is for the correct game
    if (freshData.AppId != _game.AppId)
    {
        MessageBox.Show(
            "This ZIP is for AppID " + freshData.AppId + " but this game is AppID " + _game.AppId + ".",
            "Wrong Game", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // Close the dialog and open GameWindow with the fresh data pre-loaded
    Window.GetWindow(this)?.Close();
    var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = true,
        ShowCloseButton = true
    });
    window.Title = "BlankPlugin — Update " + _game.GameName;
    window.Width = 700;
    window.Height = 600;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner = _api.Dialogs.GetCurrentAppWindow();
    window.Content = new GameWindow(null, _settings, _gamesManager, _api);
    window.ShowDialog();
}
```

---

## Part 6: Changes to BlankPlugin.cs

**File:** `BlankPlugin/source/BlankPlugin.cs`

### 6.1 — New fields to add to the `BlankPlugin` class

```csharp
private UpdateChecker _updateChecker;
private CancellationTokenSource _pluginCts;
```

### 6.2 — Changes to OnApplicationStarted()

The current `OnApplicationStarted` (line 37–41) only creates `InstalledGames`. Expand it:

```csharp
public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
{
    logger.Info("BlankPlugin started.");
    InstalledGames = new InstalledGamesManager(GetPluginUserDataPath());

    // Initialize update checker
    var runner = new ManifestCheckerRunner();
    _updateChecker = new UpdateChecker(runner, InstalledGames, PlayniteApi);
    _pluginCts = new CancellationTokenSource();

    // Run update check on startup (fire and forget — does not block Playnite)
    _ = _updateChecker.RunAsync();
}
```

### 6.3 — Add OnApplicationStopped() cleanup

The current `OnApplicationStopped` only logs. Add cancellation:

```csharp
public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
{
    logger.Info("BlankPlugin stopped.");
    _updateChecker?.Cancel();
    _pluginCts?.Cancel();
}
```

### 6.4 — Add OnLibraryUpdated() override

This method does not currently exist in `BlankPlugin.cs`. Add it:

```csharp
public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
{
    logger.Info("BlankPlugin: library updated, triggering update check.");
    _ = _updateChecker?.RunAsync();
}
```

### 6.5 — Changes to GetSidebarItems()

The sidebar currently returns one item with `Type = SiderbarItemType.Button`. No changes needed here.

However, the `InstalledGamesPanel` constructor must be updated wherever it is instantiated to pass `_updateChecker`. Search the codebase for `new InstalledGamesPanel(` and update every occurrence to:
```csharp
new InstalledGamesPanel(_insideManager, _updateChecker)
```

> If `InstalledGamesPanel` is instantiated inside `GetSidebarItems()` using a lambda like `Opened = () => new InstalledGamesPanel(InstalledGames)`, change it to `Opened = () => new InstalledGamesPanel(InstalledGames, _updateChecker)`.

### 6.6 — Add "Update Game" to GetGameMenuItems()

Inside the `if (installed != null)` block in `GetGameMenuItems()` (currently lines 91–174), add one more `yield return` after the existing menu items:

```csharp
yield return new GameMenuItem
{
    MenuSection = "BlankPlugin",
    Description = "Update Game",
    Action = menuArgs =>
    {
        var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
        {
            ShowMinimizeButton = false,
            ShowMaximizeButton = false,
            ShowCloseButton = true
        });
        window.Title = "Update Game — " + installed.GameName;
        window.Width = 480;
        window.Height = 300;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
        window.Content = new UpdateGameDialog(
            installed, Settings, InstalledGames, PlayniteApi, _updateChecker);
        window.ShowDialog();
    }
};
```

---

## Part 7: Build Order

Build these in order:
1. Build `ManifestChecker` project first (produces `ManifestChecker.exe`)
2. Build `BlankPlugin` project (the post-build step copies `ManifestChecker.exe` into `deps/`)
3. The `PackPlugin` target runs automatically and produces the `.pext`

In Visual Studio: right-click the Solution → Properties → set build order so `ManifestChecker` builds before `BlankPlugin`.

---

## Error Handling Summary

| Scenario | What happens |
|---|---|
| Steam is offline | `ManifestChecker.exe` exits non-zero; all games get `cannot_determine`; no notification; silent |
| Game has no `ManifestGIDs` (installed before this feature) | Skipped entirely; no badge shown |
| `ManifestChecker.exe` missing from deps | `ManifestCheckerRunner.IsReady` returns false; `Run()` returns error string; games get `cannot_determine` |
| Two check triggers overlap (startup + OnLibraryUpdated at same time) | `SemaphoreSlim(1)` in `UpdateChecker` — second call returns immediately |
| Plugin unloaded while check is running | `Cancel()` sets `CancellationTokenSource`; task exits at next `token.IsCancellationRequested` check |
| ManifestChecker.exe takes too long | 60-second timeout in `ManifestCheckerRunner.Run()`; process killed; error returned |

---

## File Checklist

Files to **create**:
- [ ] `ManifestChecker/ManifestChecker.csproj`
- [ ] `ManifestChecker/Program.cs`
- [ ] `BlankPlugin/source/Pipeline/ManifestCheckerRunner.cs`
- [ ] `BlankPlugin/source/Pipeline/UpdateChecker.cs`
- [ ] `BlankPlugin/source/UI/UpdateGameDialog.cs`

Files to **modify** (add new code only — do not remove existing code):
- [ ] `BlankPlugin/source/BlankPlugin.cs` — add fields, expand OnApplicationStarted, add OnApplicationStopped cleanup, add OnLibraryUpdated, add "Update Game" menu item
- [ ] `BlankPlugin/source/UI/InstalledGamesPanel.cs` — add UpdateChecker parameter, badge logic
- [ ] `BlankPlugin/source/BlankPlugin.csproj` — add CopyManifestChecker post-build target

Do **not** modify:
- `InstalledGamesManager.cs`
- `InstalledGame.cs`
- `GameData.cs`
- `UpdateStateManager.cs`
- `DepotDownloaderRunner.cs`
- `ZipProcessor.cs`
- `GameWindow.cs`
