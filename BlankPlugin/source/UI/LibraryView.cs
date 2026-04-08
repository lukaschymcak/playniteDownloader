using Microsoft.Win32;
using Playnite.SDK;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlankPlugin
{
    /// <summary>
    /// Sidebar library view — lists all games installed through BlankPlugin.
    /// Opened when the user clicks the plugin sidebar button without selecting a game.
    /// </summary>
    public class LibraryView : UserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BlankPluginSettings _settings;
        private readonly IPlayniteAPI _api;
        private readonly InstalledGamesManager _installedGamesManager;
        private readonly UpdateChecker _updateChecker;
        private readonly MorrenusClient _client;

        // API status bar
        private TextBlock _statusLabel;
        private TextBlock _apiStatusDot;
        private TextBlock _usageLabel;

        // Library UI
        private StackPanel _libraryList;
        private TextBlock _librarySummaryLabel;
        private TextBox _libraryFilterBox;

        public LibraryView(BlankPluginSettings settings, InstalledGamesManager installedGamesManager, IPlayniteAPI api, UpdateChecker updateChecker)
        {
            _settings = settings;
            _api = api;
            _installedGamesManager = installedGamesManager;
            _updateChecker = updateChecker;
            _client = new MorrenusClient(() => _settings.ApiKey);

            Content = BuildLayout();

            RefreshLibraryList();
            ThreadPool.QueueUserWorkItem(_ => RefreshApiStatus());
        }

        // ── Layout ───────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0 status bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 1 header + filter
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2 games list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 3 summary + buttons

            var statusBar = BuildApiStatusBar();
            Grid.SetRow(statusBar, 0);
            root.Children.Add(statusBar);

            // Row 1: title + filter box
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            _libraryFilterBox = new TextBox
            {
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Filter by game name"
            };
            _libraryFilterBox.TextChanged += (s, e) => RefreshLibraryList();
            DockPanel.SetDock(_libraryFilterBox, Dock.Right);
            header.Children.Add(_libraryFilterBox);

            header.Children.Add(new TextBlock
            {
                Text = "Installed Games",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            Grid.SetRow(header, 1);
            root.Children.Add(header);

            // Row 2: scrollable games list
            _libraryList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _libraryList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // Row 3: summary label + Refresh + Install from ZIP
            var bottomBar = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

            var installZipBtn = new Button
            {
                Content = "Install from ZIP",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0)
            };
            installZipBtn.Click += OnInstallFromZipClicked;
            DockPanel.SetDock(installZipBtn, Dock.Right);
            bottomBar.Children.Add(installZipBtn);

            var refreshBtn = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(12, 6, 12, 6)
            };
            refreshBtn.Click += (s, e) => RefreshLibraryList();
            DockPanel.SetDock(refreshBtn, Dock.Right);
            bottomBar.Children.Add(refreshBtn);

            _librarySummaryLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170))
            };
            bottomBar.Children.Add(_librarySummaryLabel);

            Grid.SetRow(bottomBar, 3);
            root.Children.Add(bottomBar);

            var border = new Border { Padding = new Thickness(12), Child = root };
            TextElement.SetForeground(border, Brushes.WhiteSmoke);
            return border;
        }

        private UIElement BuildApiStatusBar()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            _apiStatusDot = new TextBlock
            {
                Text = "●",
                Foreground = Brushes.Gray,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            _statusLabel = new TextBlock { Text = "Checking API...", VerticalAlignment = VerticalAlignment.Center };
            _usageLabel = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            DockPanel.SetDock(_usageLabel, Dock.Right);
            panel.Children.Add(_usageLabel);
            panel.Children.Add(_apiStatusDot);
            panel.Children.Add(_statusLabel);
            return panel;
        }

        // ── API status ───────────────────────────────────────────────────────────

        private void RefreshApiStatus()
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                Dispatch(() =>
                {
                    _apiStatusDot.Foreground = Brushes.Orange;
                    _statusLabel.Text = "No API key configured — open Settings.";
                });
                return;
            }

            var healthy = _client.CheckHealth();
            var stats = healthy ? _client.GetUserStats() : null;

            Dispatch(() =>
            {
                if (healthy)
                {
                    _apiStatusDot.Foreground = Brushes.LimeGreen;
                    _statusLabel.Text = "Morrenus online";
                    if (stats != null && string.IsNullOrEmpty(stats.Error))
                        _usageLabel.Text = "User: " + stats.Username + "  |  Daily: " + stats.DailyUsage + "/" + stats.DailyLimit;
                }
                else
                {
                    _apiStatusDot.Foreground = Brushes.Red;
                    _statusLabel.Text = "Morrenus offline";
                }
            });
        }

        // ── Library list ─────────────────────────────────────────────────────────

        private void RefreshLibraryList()
        {
            if (_installedGamesManager == null) return;

            var filter = _libraryFilterBox.Text.Trim();
            var all = _installedGamesManager.ScanLibrary();
            var filtered = all
                .Where(g => string.IsNullOrEmpty(filter) ||
                            g.GameName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(g => g.GameName)
                .ToList();

            _libraryList.Children.Clear();

            var totalSize = all.Sum(g => g.SizeOnDisk);
            _librarySummaryLabel.Text = all.Count + " game(s)  |  " + SteamLibraryHelper.FormatSize(totalSize);

            if (filtered.Count == 0)
            {
                _libraryList.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(filter)
                        ? "No games installed through BlankPlugin."
                        : "No games match the filter.",
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                });
                return;
            }

            foreach (var game in filtered)
                _libraryList.Children.Add(CreateLibraryGameEntry(game));
        }

        private Border CreateLibraryGameEntry(InstalledGame game)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 42)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });                     // thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // info
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // buttons

            // ── Thumbnail ────────────────────────────────────────────────────────
            var thumbContainer = new Border
            {
                Width = 90,
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                ClipToBounds = true
            };

            var coverSource = GetGameCoverSource(game);
            if (coverSource != null)
            {
                thumbContainer.Child = new Image
                {
                    Source = coverSource,
                    Stretch = Stretch.UniformToFill,
                    StretchDirection = StretchDirection.Both
                };
            }
            else
            {
                thumbContainer.Child = new TextBlock
                {
                    Text = game.GameName.Length > 0 ? game.GameName[0].ToString().ToUpper() : "?",
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Grid.SetColumn(thumbContainer, 0);
            grid.Children.Add(thumbContainer);

            // ── Game info ────────────────────────────────────────────────────────
            var info = new StackPanel
            {
                Margin = new Thickness(12, 10, 8, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            info.Children.Add(new TextBlock
            {
                Text = game.GameName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var updateStatus = _updateChecker?.GetStatus(game.AppId);
            if (updateStatus == "up_to_date")
            {
                info.Children.Add(new TextBlock
                {
                    Text = "Up to date",
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 205, 50)),
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            else if (updateStatus == "update_available")
            {
                info.Children.Add(new TextBlock
                {
                    Text = "Update available",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            info.Children.Add(new TextBlock
            {
                Text = SteamLibraryHelper.FormatSize(game.SizeOnDisk) + "   ·   " +
                       game.InstalledDate.ToString("yyyy-MM-dd"),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });

            info.Children.Add(new TextBlock
            {
                Text = game.InstallPath,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 110)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // ── Buttons ──────────────────────────────────────────────────────────
            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };

            var openBtn = new Button
            {
                Content = "Open",
                Width = 52,
                Height = 26,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0)
            };
            openBtn.Click += (s, e) =>
            {
                if (Directory.Exists(game.InstallPath))
                    System.Diagnostics.Process.Start("explorer.exe", game.InstallPath);
            };

            var uninstallBtn = new Button
            {
                Content = "Uninstall",
                Width = 72,
                Height = 26,
                FontSize = 11
            };
            uninstallBtn.Click += (s, e) => UninstallGame(game, card);

            btnStack.Children.Add(openBtn);
            btnStack.Children.Add(uninstallBtn);

            Grid.SetColumn(btnStack, 2);
            grid.Children.Add(btnStack);

            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && Directory.Exists(game.InstallPath))
                    System.Diagnostics.Process.Start("explorer.exe", game.InstallPath);
            };

            card.Child = grid;

            // ── Update status accent stripe ──────────────────────────────────────
            var status = _updateChecker?.GetStatus(game.AppId);
            Brush accentBrush = null;
            if (status == "up_to_date")
                accentBrush = new SolidColorBrush(Color.FromRgb(50, 205, 50));
            else if (status == "update_available")
                accentBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0));

            if (accentBrush != null)
            {
                var dockPanel = new DockPanel();
                var accentStripe = new Border { Width = 4, Background = accentBrush };
                DockPanel.SetDock(accentStripe, Dock.Left);
                dockPanel.Children.Add(accentStripe);
                card.Child = null;
                dockPanel.Children.Add(grid);
                card.Child = dockPanel;
            }

            return card;
        }

        private ImageSource GetGameCoverSource(InstalledGame game)
        {
            // 1. Playnite database cover
            if (_api != null && game.PlayniteGameId != Guid.Empty)
            {
                try
                {
                    var playniteGame = _api.Database.Games.Get(game.PlayniteGameId);
                    if (playniteGame != null && !string.IsNullOrEmpty(playniteGame.CoverImage))
                    {
                        var path = _api.Database.GetFullFilePath(playniteGame.CoverImage);
                        if (File.Exists(path))
                            return new BitmapImage(new Uri(path, UriKind.Absolute));
                    }
                }
                catch { /* fall through */ }
            }

            // 2. Steam CDN header — WPF loads this asynchronously after the card renders
            if (!string.IsNullOrWhiteSpace(game.AppId))
            {
                try
                {
                    var uri = new Uri(
                        "https://cdn.akamai.steamstatic.com/steam/apps/" + game.AppId + "/header.jpg",
                        UriKind.Absolute);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = uri;
                    bmp.DecodePixelWidth = 90;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    return bmp;
                }
                catch { /* fall through */ }
            }

            return null;
        }

        // ── Uninstall ────────────────────────────────────────────────────────────

        private void UninstallGame(InstalledGame game, Border entryCard)
        {
            var result = MessageBox.Show(
                string.Format("Uninstall \"{0}\"?\n\nThis will delete:\n{1}\n\nSave files in Documents/My Games and AppData will be preserved.",
                    game.GameName, game.InstallPath),
                "Uninstall Game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                PreserveSaveFiles(game);

                if (Directory.Exists(game.InstallPath))
                    Directory.Delete(game.InstallPath, true);

                if (!string.IsNullOrEmpty(game.LibraryPath))
                {
                    var acfPath = Path.Combine(game.LibraryPath, "steamapps",
                        "appmanifest_" + game.AppId + ".acf");
                    if (File.Exists(acfPath))
                        File.Delete(acfPath);
                }

                _installedGamesManager.Remove(game.AppId);
                _libraryList.Children.Remove(entryCard);

                var remaining = _installedGamesManager.GetAll();
                var totalSize = remaining.Sum(g => g.SizeOnDisk);
                _librarySummaryLabel.Text = remaining.Count + " game(s)  |  " + SteamLibraryHelper.FormatSize(totalSize);

                if (remaining.Count == 0)
                    RefreshLibraryList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during uninstall: " + ex.Message, "Uninstall Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void PreserveSaveFiles(InstalledGame game)
        {
            try
            {
                var backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BlankPlugin", "save_backups", game.AppId);

                var saveLocations = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", game.GameName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), game.GameName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), game.GameName)
                };

                foreach (var savePath in saveLocations)
                {
                    if (Directory.Exists(savePath))
                        CopyDirectory(savePath, Path.Combine(backupDir, Path.GetFileName(savePath)));
                }
            }
            catch
            {
                // Save preservation is best-effort; don't block uninstall
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        // ── Install from ZIP ─────────────────────────────────────────────────────

        private void OnInstallFromZipClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Manifest ZIP",
                Filter = "ZIP files (*.zip)|*.zip",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            var zipPath = dialog.FileName;
            var btn = (Button)sender;
            btn.IsEnabled = false;
            _librarySummaryLabel.Text = "Processing ZIP...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var data = new ZipProcessor().Process(zipPath);
                    Dispatch(() => SwitchToDownloadView(data));
                }
                catch (Exception ex)
                {
                    Dispatch(() =>
                    {
                        _librarySummaryLabel.Text = "Error: " + ex.Message;
                        btn.IsEnabled = true;
                    });
                }
            });
        }

        /// <summary>
        /// Replaces the library view with a download view in the same window.
        /// Called when the user picks a manifest ZIP to install from.
        /// </summary>
        private void SwitchToDownloadView(GameData data)
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            window.Title = "BlankPlugin — " + data.GameName;
            window.Content = new DownloadView(null, _settings, _installedGamesManager, _api, _updateChecker, data);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void Dispatch(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }
    }
}
