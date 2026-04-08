# Update System Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three related issues in the auto-update system: ManifestChecker.exe crashing on launch due to missing managed DLL, no Playnite tag added to game cards when an update is detected, and the update flow opening the wrong window.

**Architecture:** (1) Fix the build pipeline so `dotnet publish` produces a true single-file exe instead of the bootstrapper-only copy. (2) Extend `UpdateChecker.RunAsync()` to apply/remove a Playnite Tag on each game after status resolution, and surface failures as notifications. (3) Create a minimal `UpdateWindow` that knows the existing install context and replaces `GameWindow` in the update flow.

**Tech Stack:** C# / .NET Framework 4.62, Playnite SDK 6.2, MSBuild, `dotnet publish` (for ManifestChecker)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `BlankPlugin/source/BlankPlugin.csproj` | Modify | Add `PublishManifestChecker` target; fix `CopyManifestChecker` to use publish output |
| `BlankPlugin/source/Pipeline/UpdateChecker.cs` | Modify | Add Playnite tag apply/remove + failure notification |
| `BlankPlugin/source/UI/UpdateWindow.cs` | Create | Minimal update window: fetch/use manifest, depot selection, download, record update |
| `BlankPlugin/source/UI/UpdateGameDialog.cs` | Modify | Replace `GameWindow` calls with `UpdateWindow` in both update paths |

> **Note:** No existing committed files are modified. `UpdateChecker.cs`, `UpdateGameDialog.cs` are untracked new files (status `??`). `BlankPlugin.csproj` is already modified on this branch.

---

## Task 1: Fix ManifestChecker Build Pipeline

**Files:**
- Modify: `BlankPlugin/source/BlankPlugin.csproj`

### Root cause recap
`ManifestChecker.csproj` sets `PublishSingleFile=true` but a regular `dotnet build` ignores this — it only produces a bootstrapper `.exe` and a separate `ManifestChecker.dll`. The current `CopyManifestChecker` target copies only the bootstrapper, so the exe crashes immediately when launched (can't find its DLL). The fix: run `dotnet publish` which produces a true single-file exe, then copy from the publish output.

- [ ] **Step 1: Open `BlankPlugin/source/BlankPlugin.csproj` and locate the existing `CopyManifestChecker` target** (around line 101). It looks like:

```xml
<Target Name="CopyManifestChecker" BeforeTargets="PackPlugin">
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

- [ ] **Step 2: Replace that entire `CopyManifestChecker` target** with the following two targets:

```xml
<!-- Run dotnet publish to produce a true single-file exe before copying -->
<Target Name="PublishManifestChecker" BeforeTargets="CopyManifestChecker">
  <Exec
    Command="dotnet publish &quot;$(SolutionDir)..\..\ManifestChecker\ManifestChecker.csproj&quot; -c $(Configuration) --nologo -v quiet"
    ContinueOnError="true" />
</Target>

<!-- Copy single-file published exe into deps/ so it is packed into the .pext -->
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

- [ ] **Step 3: Build the solution** (`Ctrl+Shift+B` in Visual Studio or `dotnet build` in terminal). Expected: build succeeds, `dotnet publish` runs for ManifestChecker during the build.

- [ ] **Step 4: Verify the published exe was copied.** Check `BlankPlugin/source/bin/Debug/net462/deps/ManifestChecker.exe`. The file should now be noticeably larger than before (the publish single-file bundles the managed DLL — expect ~3–8 MB instead of ~150 KB). If it's still small, check the build output for publish errors.

- [ ] **Step 5: Commit**

```
git add BlankPlugin/source/BlankPlugin.csproj
git commit -m "fix: publish ManifestChecker as single-file exe in build pipeline"
```

---

## Task 2: Add Playnite Tags + Failure Notification to UpdateChecker

**Files:**
- Modify: `BlankPlugin/source/Pipeline/UpdateChecker.cs`

- [ ] **Step 1: Open `UpdateChecker.cs` and locate the `results == null` branch** (around line 94). Currently:

```csharp
if (results == null)
{
    logger.Warn("UpdateChecker: ManifestCheckerRunner failed: " + error);
    // Mark all as cannot_determine
    foreach (var game in checkable)
    {
        _statusCache[game.AppId] = "cannot_determine";
    }
    return;
}
```

- [ ] **Step 2: Add a Playnite failure notification inside that branch**, after the `logger.Warn(...)` line and before the `foreach`:

```csharp
if (results == null)
{
    logger.Warn("UpdateChecker: ManifestCheckerRunner failed: " + error);

    _playniteApi.Notifications.Add(new NotificationMessage(
        "blankplugin_check_failed_" + DateTime.Now.Ticks,
        "BlankPlugin: Update check failed — " + error,
        NotificationType.Error));

    foreach (var game in checkable)
    {
        _statusCache[game.AppId] = "cannot_determine";
    }
    return;
}
```

- [ ] **Step 3: Locate the end of `RunAsync()`**, after the existing notification block (around line 162–175):

```csharp
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
```

- [ ] **Step 4: Add tag application code immediately after that notification block**, still inside the outer `try` and before the `catch`:

```csharp
// Apply / remove "Update Available" Playnite tag for each checked game
if (!token.IsCancellationRequested)
{
    try
    {
        var updateTag = _playniteApi.Database.Tags.Add("Update Available");

        _playniteApi.Database.BufferedUpdate(() =>
        {
            foreach (var game in checkable)
            {
                if (game.PlayniteGameId == Guid.Empty) continue;

                var playniteGame = _playniteApi.Database.Games.Get(game.PlayniteGameId);
                if (playniteGame == null) continue;

                if (playniteGame.TagIds == null)
                    playniteGame.TagIds = new System.Collections.Generic.List<Guid>();

                var status = _statusCache.TryGetValue(game.AppId, out var s) ? s : null;

                if (status == "update_available")
                {
                    if (!playniteGame.TagIds.Contains(updateTag.Id))
                    {
                        playniteGame.TagIds.Add(updateTag.Id);
                        _playniteApi.Database.Games.Update(playniteGame);
                    }
                }
                else if (status == "up_to_date")
                {
                    if (playniteGame.TagIds.Remove(updateTag.Id))
                        _playniteApi.Database.Games.Update(playniteGame);
                }
                // cannot_determine: leave tag unchanged
            }
        });
    }
    catch (Exception tagEx)
    {
        logger.Warn("UpdateChecker: tag update failed: " + tagEx.Message);
    }
}
```

- [ ] **Step 5: Build the solution.** Expected: build succeeds with no errors.

- [ ] **Step 6: Commit**

```
git add BlankPlugin/source/Pipeline/UpdateChecker.cs
git commit -m "feat: add Playnite tags and failure notification to UpdateChecker"
```

---

## Task 3: Create UpdateWindow

**Files:**
- Create: `BlankPlugin/source/UI/UpdateWindow.cs`

- [ ] **Step 1: Create `BlankPlugin/source/UI/UpdateWindow.cs`** with the full implementation below. Read it top to bottom before saving — it is complete and self-contained.

```csharp
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlankPlugin
{
    /// <summary>
    /// Minimal update window. Knows the existing InstalledGame context.
    /// API path: fetches a fresh manifest from Morrenus, shows depot selection.
    /// ZIP path: uses pre-parsed GameData directly (no network call).
    /// On success: updates the InstalledGame record and removes the "Update Available" tag.
    /// </summary>
    public class UpdateWindow : UserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly InstalledGame _existingGame;
        private readonly BlankPluginSettings _settings;
        private readonly InstalledGamesManager _gamesManager;
        private readonly IPlayniteAPI _api;
        private readonly GameData _preloadedData; // null = API path

        private GameData _freshData;
        private readonly List<CheckBox> _depotCheckBoxes = new List<CheckBox>();

        // UI
        private TextBlock _statusLabel;
        private StackPanel _depotPanel;
        private TextBox _installPathBox;
        private Button _updateBtn;
        private ProgressBar _progressBar;
        private TextBox _logBox;

        public UpdateWindow(
            InstalledGame existingGame,
            BlankPluginSettings settings,
            InstalledGamesManager gamesManager,
            IPlayniteAPI api,
            GameData preloadedData = null)
        {
            _existingGame = existingGame;
            _settings = settings;
            _gamesManager = gamesManager;
            _api = api;
            _preloadedData = preloadedData;

            Content = BuildLayout();

            if (_preloadedData != null)
            {
                // ZIP path — data already parsed, no network needed
                _freshData = _preloadedData;
                PopulateDepots(_freshData);
                SetStatus("Ready to update");
            }
            else
            {
                // API path — fetch fresh manifest in background
                SetStatus("Fetching manifest…");
                ThreadPool.QueueUserWorkItem(_ => FetchManifest());
            }
        }

        // ── Layout ───────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new StackPanel { Margin = new Thickness(16) };

            // Header
            root.Children.Add(new TextBlock
            {
                Text = "Updating: " + _existingGame.GameName,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Status label
            _statusLabel = new TextBlock
            {
                Text = string.Empty,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(_statusLabel);

            // Depots header
            root.Children.Add(new TextBlock
            {
                Text = "Depots to update:",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Depot scroll + panel
            _depotPanel = new StackPanel();
            var depotScroll = new ScrollViewer
            {
                Content = _depotPanel,
                MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(depotScroll);

            // Install path
            root.Children.Add(new TextBlock
            {
                Text = "Install path:",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _installPathBox = new TextBox
            {
                Text = _existingGame.InstallPath,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(_installPathBox);

            // Button row
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _updateBtn = new Button
            {
                Content = "Update Game",
                Width = 110,
                Height = 28,
                IsEnabled = false,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _updateBtn.Click += OnUpdateClicked;
            btnRow.Children.Add(_updateBtn);

            var closeBtn = new Button { Content = "Close", Width = 70, Height = 28 };
            closeBtn.Click += (s, e) => Window.GetWindow(this)?.Close();
            btnRow.Children.Add(closeBtn);
            root.Children.Add(btnRow);

            // Progress bar (hidden until download starts)
            _progressBar = new ProgressBar
            {
                Height = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_progressBar);

            // Log box
            _logBox = new TextBox
            {
                IsReadOnly = true,
                Height = 110,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(4)
            };
            root.Children.Add(_logBox);

            var border = new Border { Child = root };
            TextElement.SetForeground(border, Brushes.WhiteSmoke);
            return border;
        }

        // ── Manifest fetch (API path) ─────────────────────────────────────────────

        private void FetchManifest()
        {
            try
            {
                var client = new MorrenusClient(() => _settings.ApiKey);
                var zipPath = client.DownloadManifest(_existingGame.AppId);
                _freshData = new ZipProcessor().Process(zipPath);
                Dispatch(() =>
                {
                    PopulateDepots(_freshData);
                    SetStatus("Ready to update");
                });
            }
            catch (Exception ex)
            {
                logger.Error("UpdateWindow.FetchManifest failed: " + ex.Message);
                Dispatch(() => SetStatus("Failed to fetch manifest: " + ex.Message));
            }
        }

        // ── Depot list ────────────────────────────────────────────────────────────

        private void PopulateDepots(GameData data)
        {
            _depotPanel.Children.Clear();
            _depotCheckBoxes.Clear();

            foreach (var kv in data.Depots)
            {
                var depotId = kv.Key;
                var info = kv.Value;
                var label = string.IsNullOrEmpty(info.Description) ? "Depot " + depotId : info.Description;
                if (info.Size > 0)
                    label += "  (" + SteamLibraryHelper.FormatSize(info.Size) + ")";

                var cb = new CheckBox
                {
                    Content = label,
                    Tag = depotId,
                    IsChecked = _existingGame.SelectedDepots != null
                                && _existingGame.SelectedDepots.Contains(depotId),
                    Margin = new Thickness(4, 2, 0, 2)
                };
                cb.Checked += (s, e) => RefreshUpdateButton();
                cb.Unchecked += (s, e) => RefreshUpdateButton();
                _depotCheckBoxes.Add(cb);
                _depotPanel.Children.Add(cb);
            }

            RefreshUpdateButton();
        }

        private void RefreshUpdateButton()
        {
            _updateBtn.IsEnabled = _depotCheckBoxes.Any(cb => cb.IsChecked == true);
        }

        // ── Download ──────────────────────────────────────────────────────────────

        private void OnUpdateClicked(object sender, RoutedEventArgs e)
        {
            var selectedDepots = _depotCheckBoxes
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (string)cb.Tag)
                .ToList();

            var installPath = _installPathBox.Text.Trim();
            if (string.IsNullOrEmpty(installPath))
            {
                MessageBox.Show("Install path cannot be empty.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _updateBtn.IsEnabled = false;
            _progressBar.Visibility = Visibility.Visible;
            _progressBar.IsIndeterminate = true;
            SetStatus("Downloading…");
            AppendLog("Starting update for " + _existingGame.GameName);

            var worker = new Thread(() => RunDownload(selectedDepots, installPath));
            worker.IsBackground = true;
            worker.Start();
        }

        private void RunDownload(List<string> selectedDepots, string installPath)
        {
            try
            {
                Directory.CreateDirectory(installPath);

                // Clone GameData and set selected depots for the runner
                var downloadData = new GameData
                {
                    AppId = _freshData.AppId,
                    GameName = _freshData.GameName,
                    InstallDir = _freshData.InstallDir,
                    BuildId = _freshData.BuildId,
                    Depots = _freshData.Depots,
                    Manifests = _freshData.Manifests,
                    Dlcs = _freshData.Dlcs,
                    SelectedDepots = selectedDepots
                };

                var downloader = new DepotDownloaderRunner();
                downloader.Run(
                    gameData: downloadData,
                    destPath: installPath,
                    onLog: line => AppendLog(line),
                    onProgress: pct => Dispatch(() =>
                    {
                        _progressBar.IsIndeterminate = false;
                        _progressBar.Value = pct;
                    }),
                    maxDownloads: _settings.MaxDownloads,
                    steamUsername: _settings.SteamUsername);

                // Update the persisted InstalledGame record
                _existingGame.ManifestGIDs = new Dictionary<string, string>(_freshData.Manifests);
                _existingGame.SelectedDepots = new List<string>(selectedDepots);
                _existingGame.SizeOnDisk = CalculateDirSize(installPath);
                _gamesManager.Save(_existingGame);

                // Remove "Update Available" tag from Playnite game record
                RemoveUpdateTag();

                Dispatch(() =>
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = 100;
                    SetStatus("Update complete.");
                    AppendLog("Update complete.");
                    _updateBtn.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                logger.Error("UpdateWindow.RunDownload failed: " + ex.Message);
                Dispatch(() =>
                {
                    SetStatus("Download failed.");
                    AppendLog("ERROR: " + ex.Message);
                    _updateBtn.IsEnabled = true;
                    _progressBar.Visibility = Visibility.Collapsed;
                });
            }
        }

        // ── Post-download helpers ─────────────────────────────────────────────────

        private void RemoveUpdateTag()
        {
            try
            {
                if (_existingGame.PlayniteGameId == Guid.Empty) return;
                var playniteGame = _api.Database.Games.Get(_existingGame.PlayniteGameId);
                if (playniteGame == null) return;

                var tag = _api.Database.Tags.FirstOrDefault(t => t.Name == "Update Available");
                if (tag == null) return;

                if (playniteGame.TagIds != null && playniteGame.TagIds.Remove(tag.Id))
                    _api.Database.Games.Update(playniteGame);
            }
            catch (Exception ex)
            {
                logger.Warn("UpdateWindow.RemoveUpdateTag failed: " + ex.Message);
            }
        }

        private static long CalculateDirSize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // ── UI helpers ────────────────────────────────────────────────────────────

        private void SetStatus(string text) => _statusLabel.Text = text;

        private void AppendLog(string line)
        {
            Dispatch(() =>
            {
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.ScrollToEnd();
            });
        }

        private void Dispatch(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }
    }
}
```

- [ ] **Step 2: Build the solution.** Expected: build succeeds. If there are compile errors, check that `SteamLibraryHelper`, `MorrenusClient`, `ZipProcessor`, `DepotDownloaderRunner`, `InstalledGame`, `InstalledGamesManager`, `GameData`, and `BlankPluginSettings` are all in the `BlankPlugin` namespace.

- [ ] **Step 3: Commit**

```
git add BlankPlugin/source/UI/UpdateWindow.cs
git commit -m "feat: add UpdateWindow for context-aware game update flow"
```

---

## Task 4: Wire UpdateGameDialog to UpdateWindow

**Files:**
- Modify: `BlankPlugin/source/UI/UpdateGameDialog.cs`

The current `OnUpdateViaApi()` and `OnUpdateViaZip()` both open `GameWindow`. Replace them with `UpdateWindow`.

- [ ] **Step 1: Open `UpdateGameDialog.cs` and replace the entire `OnUpdateViaApi()` method** (currently lines 158–178):

```csharp
private void OnUpdateViaApi()
{
    Window.GetWindow(this)?.Close();

    var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = false,
        ShowCloseButton = true
    });
    window.Title = "Update — " + _game.GameName;
    window.Width = 700;
    window.Height = 500;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner = _api.Dialogs.GetCurrentAppWindow();
    window.Content = new UpdateWindow(_game, _settings, _gamesManager, _api);
    window.ShowDialog();
}
```

- [ ] **Step 2: Replace the entire `OnUpdateViaZip()` method** (currently lines 181–225). Keep the ZIP validation logic (file picker, ZipProcessor, AppId check) but replace the final window creation:

```csharp
private void OnUpdateViaZip()
{
    var zipPath = _api.Dialogs.SelectFile("Manifest ZIP|*.zip");
    if (string.IsNullOrEmpty(zipPath)) return;

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

    if (freshData.AppId != _game.AppId)
    {
        MessageBox.Show(
            "This ZIP is for AppID " + freshData.AppId + " but this game is AppID " + _game.AppId + ".",
            "Wrong Game", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    Window.GetWindow(this)?.Close();

    var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = false,
        ShowCloseButton = true
    });
    window.Title = "Update — " + _game.GameName;
    window.Width = 700;
    window.Height = 500;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner = _api.Dialogs.GetCurrentAppWindow();
    window.Content = new UpdateWindow(_game, _settings, _gamesManager, _api, freshData);
    window.ShowDialog();
}
```

- [ ] **Step 3: Build the solution.** Expected: build succeeds and a `.pext` file is produced in `BlankPlugin/source/bin/`.

- [ ] **Step 4: Install and smoke test.**
  1. Double-click the `.pext` to install in Playnite (or copy build output to `%APPDATA%\Playnite\Extensions\lukas_BlankPlugin_Plugin_1_0_0\`)
  2. Restart Playnite
  3. Check `%APPDATA%\Playnite\playnite.log` — should NOT contain "ManifestCheckerRunner failed" anymore after startup. If it still fails, check the publish output path and file size of `deps/ManifestChecker.exe`
  4. Right-click an installed game → BlankPlugin → Update Game → "Update via API" → verify the new `UpdateWindow` opens (header says "Updating: {game name}", install path is pre-filled, depots are pre-checked)
  5. Right-click → Update Game → "Update via ZIP" → pick a ZIP → verify same window opens with depots already listed (no fetch delay)
  6. If a game has a manually-changed GID in `installed_games.json`, restart Playnite and verify the "Update Available" tag appears on its card in the library

- [ ] **Step 5: Commit**

```
git add BlankPlugin/source/UI/UpdateGameDialog.cs
git commit -m "feat: wire UpdateGameDialog to new UpdateWindow"
```
