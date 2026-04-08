using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private readonly UpdateChecker _updateChecker;

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
            GameData preloadedData = null,
            UpdateChecker updateChecker = null)
        {
            _existingGame = existingGame;
            _settings = settings;
            _gamesManager = gamesManager;
            _api = api;
            _preloadedData = preloadedData;
            _updateChecker = updateChecker;

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
                // Only show depots that were previously downloaded
                if (_existingGame.SelectedDepots == null
                    || !_existingGame.SelectedDepots.Contains(depotId)) continue;

                var info = kv.Value;
                var label = string.IsNullOrEmpty(info.Description) ? "Depot " + depotId : info.Description;
                if (info.Size > 0)
                    label += "  (" + SteamLibraryHelper.FormatSize(info.Size) + ")";

                // Compare saved vs fresh GID for this depot
                bool gidChanged = false;

                if (_existingGame.ManifestGIDs != null
                    && _existingGame.ManifestGIDs.TryGetValue(depotId, out var savedGid)
                    && data.Manifests != null
                    && data.Manifests.TryGetValue(depotId, out var freshGid))
                {
                    gidChanged = (savedGid != freshGid);
                }

                var cb = new CheckBox
                {
                    Content = label,
                    Tag = depotId,
                    Margin = new Thickness(4, 2, 0, 2)
                };

                if (gidChanged)
                {
                    // GID changed — pre-check for update
                    cb.IsChecked = true;
                }
                else
                {
                    // Still current — show as up to date
                    cb.IsChecked = false;
                    cb.Content = label + "  (up to date)";
                    cb.Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110));
                    cb.IsEnabled = false;
                }

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

                // Update the persisted InstalledGame record — only store GIDs for downloaded depots
                _existingGame.ManifestGIDs = _freshData.Manifests
                    .Where(kv => selectedDepots.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                _existingGame.SelectedDepots = new List<string>(selectedDepots);
                _existingGame.SizeOnDisk = CalculateDirSize(installPath);
                _gamesManager.Save(_existingGame);

                // Clear the update status cache so UpdateGameDialog shows correct status
                _updateChecker?.MarkUpToDate(_existingGame.AppId);

                // Re-run the update checker to refresh all game statuses
                _updateChecker?.RunAsync();

                Dispatch(() =>
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = 100;
                    SetStatus("Update complete.");
                    AppendLog("Update complete.");
                    _updateBtn.IsEnabled = false;

                    // Auto-close the window after a brief delay so the user sees "Update complete."
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        Window.GetWindow(this)?.Close();
                    };
                    timer.Start();
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
