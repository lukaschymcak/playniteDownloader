using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlankPlugin
{
    /// <summary>
    /// Plugin Library tab — union of installed games (disk + installed_games.json) and saved bookmarks (library_games.json).
    /// One row per AppId; install state is derived from <see cref="InstalledGamesManager.ScanLibrary"/>, not the bookmark file.
    /// </summary>
    public class LibraryView : UserControl
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();

        /// <summary>Steam header 460×215; fixed box matches Search tab header width (184) for readable art.</summary>
        private const int LibraryThumbWidthDip = 184;

        private const int LibraryThumbHeightDip = (int)(LibraryThumbWidthDip * 215.0 / 460.0 + 0.5);

        /// <summary>Decode at ~3× DIP size for HiDPI Steam headers.</summary>
        private const int LibrarySteamDecodeScale = 3;

        private const int LibraryCardTitleFontSize = 15;

        private const int LibraryCardStatusFontSize = 11;

        private const int LibraryCardMetaFontSize = 11;

        private const int LibraryCardPathFontSize = 11;

        private const int LibraryCardButtonHeight = 32;

        private const int LibraryCardButtonFontSize = 12;

        private static readonly Thickness LibraryCardInfoMargin = new Thickness(14, 12, 10, 12);

        private static readonly Thickness LibraryCardBottomMargin = new Thickness(0, 0, 0, 8);

        private sealed class LibraryRow
        {
            public InstalledGame Installed { get; set; }
            public SavedLibraryGame Bookmark { get; set; }
            public string SortName =>
                Installed != null
                    ? (Installed.GameName ?? Installed.AppId ?? "")
                    : (Bookmark?.GameName ?? Bookmark?.AppId ?? "");
        }

        private readonly AppSettings _settings;
        private readonly IDialogService _dialogService;
        private readonly InstalledGamesManager _installedGamesManager;
        private readonly LibraryGamesManager _libraryGames;
        private readonly IAppHost _appHost;
        private readonly UpdateChecker _updateChecker;
        private readonly MorrenusClient _client;
        private readonly SteamIntegrationService _steamIntegration = new SteamIntegrationService();
        private Dictionary<string, LuaIntegrationState> _luaByAppId = new Dictionary<string, LuaIntegrationState>(StringComparer.Ordinal);

        private sealed class LuaIntegrationState
        {
            public bool CanAdd { get; set; }
            public bool AlreadyAdded { get; set; }
            public string Tooltip { get; set; }
            public string ManifestZipPath { get; set; }
            public string LuaFileName { get; set; }
        }

        // API status bar
        private TextBlock _statusLabel;
        private TextBlock _apiStatusDot;
        private TextBlock _usageLabel;

        // Library UI
        private StackPanel _libraryList;
        private TextBlock _librarySummaryLabel;
        private TextBox _libraryFilterBox;

        public LibraryView(AppSettings settings, InstalledGamesManager installedGamesManager, LibraryGamesManager libraryGames, IDialogService dialogService, UpdateChecker updateChecker, IAppHost appHost)
        {
            _settings = settings;
            _dialogService = dialogService;
            _installedGamesManager = installedGamesManager;
            _libraryGames = libraryGames;
            _appHost = appHost;
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
                Text = "Library:",
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

            var reconcileBtn = new Button
            {
                Content = "Reconcile Steam Installs",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0)
            };
            reconcileBtn.Click += (s, e) =>
            {
                try
                {
                    var r = _appHost?.ReconcileInstalledState();
                    if (r != null)
                        logger.Info("Manual reconcile: added=" + r.Added + ", updated=" + r.Updated + ", removed=" + r.Removed);
                }
                catch (Exception ex)
                {
                    logger.Warn("Manual reconcile failed: " + ex.Message);
                }
                RefreshLibraryList();
            };
            DockPanel.SetDock(reconcileBtn, Dock.Right);
            bottomBar.Children.Add(reconcileBtn);

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
            var installedList = _installedGamesManager.ScanLibrary();
            var installedByAppId = new Dictionary<string, InstalledGame>(StringComparer.Ordinal);
            foreach (var g in installedList)
            {
                if (string.IsNullOrEmpty(g.AppId)) continue;
                if (!installedByAppId.ContainsKey(g.AppId))
                    installedByAppId[g.AppId] = g;
            }

            var merged = new List<LibraryRow>();
            foreach (var g in installedByAppId.Values)
                merged.Add(new LibraryRow { Installed = g, Bookmark = null });

            if (_libraryGames != null)
            {
                foreach (var b in _libraryGames.GetAll())
                {
                    if (string.IsNullOrWhiteSpace(b.AppId)) continue;
                    if (installedByAppId.ContainsKey(b.AppId)) continue;
                    merged.Add(new LibraryRow { Installed = null, Bookmark = b });
                }
            }

            var totalSize = installedList.Sum(g => g.SizeOnDisk);
            var installedCount = installedByAppId.Count;
            var savedOnlyCount = merged.Count - installedCount;
            _librarySummaryLabel.Text = installedCount + " installed \u00b7 " + savedOnlyCount + " saved  |  " +
                SteamLibraryHelper.FormatSize(totalSize);

            var appIds = merged
                .Select(row => row.Installed != null ? row.Installed.AppId : (row.Bookmark != null ? row.Bookmark.AppId : null))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            _luaByAppId = BuildLuaStateMap(appIds);

            var filtered = merged
                .Where(row => MatchesLibraryFilter(row, filter))
                .OrderBy(row => row.SortName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _libraryList.Children.Clear();

            if (filtered.Count == 0)
            {
                _libraryList.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(filter)
                        ? "No games in Library. Install a game or use Search \u2192 + Add to Library."
                        : "No games match the filter.",
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                });
                return;
            }

            foreach (var row in filtered)
            {
                if (row.Installed != null)
                    _libraryList.Children.Add(CreateLibraryGameEntry(row.Installed));
                else if (row.Bookmark != null)
                    _libraryList.Children.Add(CreateBookmarkLibraryEntry(row.Bookmark));
            }
        }

        private static bool MatchesLibraryFilter(LibraryRow row, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (row.Installed != null)
            {
                var name = row.Installed.GameName ?? "";
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                var id = row.Installed.AppId ?? "";
                if (id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            else if (row.Bookmark != null)
            {
                var name = row.Bookmark.GameName ?? "";
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                var id = row.Bookmark.AppId ?? "";
                if (id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private Border CreateLibraryGameEntry(InstalledGame game)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = LibraryCardBottomMargin,
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 42)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LibraryThumbWidthDip) }); // thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // info
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // buttons

            // ── Thumbnail ────────────────────────────────────────────────────────
            var thumbContainer = new Border
            {
                Width  = LibraryThumbWidthDip,
                Height = LibraryThumbHeightDip,
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                ClipToBounds = true
            };

            var imgElementInstalled = new Image
            {
                Stretch             = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            thumbContainer.Child = imgElementInstalled;
            LoadLibraryCardHeaderAsync(game.AppId, game.HeaderImageUrl, imgElementInstalled,
                LibraryHeaderPersistTarget.Installed);

            Grid.SetColumn(thumbContainer, 0);
            grid.Children.Add(thumbContainer);

            // ── Game info ────────────────────────────────────────────────────────
            var info = new StackPanel
            {
                Margin = LibraryCardInfoMargin,
                VerticalAlignment = VerticalAlignment.Center
            };

            info.Children.Add(new TextBlock
            {
                Text = game.GameName,
                FontWeight = FontWeights.SemiBold,
                FontSize = LibraryCardTitleFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var canReliablyCheckUpdates = game.ManifestGIDs != null && game.ManifestGIDs.Count > 0;
            var updateStatus = canReliablyCheckUpdates ? _updateChecker?.GetStatus(game.AppId) : null;
            if (updateStatus == "up_to_date")
            {
                info.Children.Add(new TextBlock
                {
                    Text = "Up to date",
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 205, 50)),
                    FontSize = LibraryCardStatusFontSize,
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
                    FontSize = LibraryCardStatusFontSize,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            info.Children.Add(new TextBlock
            {
                Text = SteamLibraryHelper.FormatSize(game.SizeOnDisk) + "   ·   " +
                       game.InstalledDate.ToString("yyyy-MM-dd"),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                FontSize = LibraryCardMetaFontSize,
                Margin = new Thickness(0, 3, 0, 0)
            });

            info.Children.Add(new TextBlock
            {
                Text = game.InstallPath,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 110)),
                FontSize = LibraryCardPathFontSize,
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
                Margin = new Thickness(0, 0, 14, 0)
            };

            var openBtn = new Button
            {
                Content = "Open",
                Width = 58,
                Height = LibraryCardButtonHeight,
                FontSize = LibraryCardButtonFontSize,
                Margin = new Thickness(0, 0, 8, 0)
            };
            openBtn.Click += (s, e) =>
            {
                if (Directory.Exists(game.InstallPath))
                    System.Diagnostics.Process.Start("explorer.exe", game.InstallPath);
            };

            var uninstallBtn = new Button
            {
                Content = "Uninstall",
                Width = 82,
                Height = LibraryCardButtonHeight,
                FontSize = LibraryCardButtonFontSize
            };
            uninstallBtn.Click += (s, e) => UninstallGame(game);

            var moreBtn = new Button
            {
                Content = "\u22EE",
                Width = 34,
                Height = LibraryCardButtonHeight,
                FontSize = 16,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "More actions"
            };
            var installedMenu = BuildLibraryCardMenuForInstalled(game, game.GameName ?? game.AppId);
            moreBtn.Click += (s, e) =>
            {
                installedMenu.PlacementTarget = moreBtn;
                installedMenu.IsOpen = true;
            };

            if (updateStatus == "update_available")
            {
                var updateBtn = new Button
                {
                    Content = "Update",
                    Width = 72,
                    Height = LibraryCardButtonHeight,
                    FontSize = LibraryCardButtonFontSize,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x66, 0x00)),
                    Foreground = Brushes.White
                };
                updateBtn.Click += (s, e) =>
                {
                    var window = _dialogService.CreateWindow(
                        "Update Game — " + game.GameName,
                        new UpdateGameDialog(game, _settings, _installedGamesManager, _dialogService, _updateChecker),
                        _dialogService.GetMainWindow());
                    window.Width = 480;
                    window.Height = 300;
                    window.ShowDialog();
                    Dispatch(() => RefreshLibraryList());
                };
                btnStack.Children.Add(updateBtn);
            }

            btnStack.Children.Add(openBtn);
            btnStack.Children.Add(uninstallBtn);
            btnStack.Children.Add(moreBtn);

            Grid.SetColumn(btnStack, 2);
            grid.Children.Add(btnStack);

            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && Directory.Exists(game.InstallPath))
                    System.Diagnostics.Process.Start("explorer.exe", game.InstallPath);
            };

            card.Child = grid;

            // ── Update status accent stripe ──────────────────────────────────────
            var status = canReliablyCheckUpdates ? _updateChecker?.GetStatus(game.AppId) : null;
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

        /// <summary>Saved-only row: Download opens the same flow as Search.</summary>
        private Border CreateBookmarkLibraryEntry(SavedLibraryGame bookmark)
        {
            var displayName = string.IsNullOrWhiteSpace(bookmark.GameName) ? bookmark.AppId : bookmark.GameName;

            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = LibraryCardBottomMargin,
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 42)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LibraryThumbWidthDip) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumbContainer = new Border
            {
                Width  = LibraryThumbWidthDip,
                Height = LibraryThumbHeightDip,
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                ClipToBounds = true
            };

            var imgElementBookmark = new Image
            {
                Stretch             = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            thumbContainer.Child = imgElementBookmark;
            LoadLibraryCardHeaderAsync(bookmark.AppId, bookmark.HeaderImageUrl, imgElementBookmark,
                LibraryHeaderPersistTarget.Bookmark);

            Grid.SetColumn(thumbContainer, 0);
            grid.Children.Add(thumbContainer);

            var info = new StackPanel
            {
                Margin = LibraryCardInfoMargin,
                VerticalAlignment = VerticalAlignment.Center
            };

            info.Children.Add(new TextBlock
            {
                Text = displayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = LibraryCardTitleFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            info.Children.Add(new TextBlock
            {
                Text = "Saved \u00b7 not installed",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 170)),
                FontSize = LibraryCardStatusFontSize,
                Margin = new Thickness(0, 2, 0, 0)
            });

            info.Children.Add(new TextBlock
            {
                Text = "App " + bookmark.AppId,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 110)),
                FontSize = LibraryCardMetaFontSize,
                Margin = new Thickness(0, 3, 0, 0)
            });

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };

            var downloadBtn = new Button
            {
                Content = "Download",
                Width = 96,
                Height = LibraryCardButtonHeight,
                FontSize = LibraryCardButtonFontSize,
                Margin = new Thickness(0, 0, 8, 0)
            };
            downloadBtn.Click += (s, e) =>
            {
                if (_appHost != null)
                    _appHost.OpenDownloadForAppId(bookmark.AppId, displayName, bookmark.HeaderImageUrl);
            };

            var removeBtn = new Button
            {
                Content = "Remove",
                Width = 72,
                Height = LibraryCardButtonHeight,
                FontSize = LibraryCardButtonFontSize
            };
            removeBtn.Click += (s, e) => RemoveBookmark(bookmark, displayName);

            var moreBtn = new Button
            {
                Content = "\u22EE",
                Width = 34,
                Height = LibraryCardButtonHeight,
                FontSize = 16,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "More actions"
            };
            var bookmarkMenu = BuildLibraryCardMenuForBookmark(bookmark, displayName);
            moreBtn.Click += (s, e) =>
            {
                bookmarkMenu.PlacementTarget = moreBtn;
                bookmarkMenu.IsOpen = true;
            };

            btnStack.Children.Add(downloadBtn);
            btnStack.Children.Add(removeBtn);
            btnStack.Children.Add(moreBtn);
            Grid.SetColumn(btnStack, 2);
            grid.Children.Add(btnStack);

            var accentStripe = new Border { Width = 4, Background = new SolidColorBrush(Color.FromRgb(100, 100, 120)) };
            var dockPanel = new DockPanel();
            DockPanel.SetDock(accentStripe, Dock.Left);
            dockPanel.Children.Add(accentStripe);
            dockPanel.Children.Add(grid);
            card.Child = dockPanel;

            return card;
        }

        private void RemoveBookmark(SavedLibraryGame bookmark, string displayName)
        {
            if (_libraryGames == null || bookmark == null || string.IsNullOrWhiteSpace(bookmark.AppId))
                return;

            var msg = "Remove \"" + displayName + "\" from the LuDownloader saved library?\n\n" +
                      "(This does not uninstall games or remove them from the host library.)";
            var result = _dialogService.ShowMessage(msg, "Remove from saved library", MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _libraryGames.Remove(bookmark.AppId);
            RefreshLibraryList();
        }

        private ContextMenu BuildLibraryCardMenuForInstalled(InstalledGame game, string displayName)
        {
            var menu = new ContextMenu();

            var addToSteam = new MenuItem { Header = "Add to Steam" };
            if (string.IsNullOrWhiteSpace(game?.AppId) || !_luaByAppId.TryGetValue(game.AppId, out var st))
            {
                addToSteam.IsEnabled = !string.IsNullOrWhiteSpace(game?.AppId);
                addToSteam.ToolTip = "Try Add to Steam (will report exact missing manifest paths if not found).";
                addToSteam.Click += (s, e) => AddToSteamFromLibrary(game.AppId, displayName, null, false);
            }
            else
            {
                addToSteam.IsEnabled = true;
                addToSteam.ToolTip = st.Tooltip;
                addToSteam.Click += (s, e) => AddToSteamFromLibrary(game.AppId, displayName, st.ManifestZipPath, st.AlreadyAdded);
            }
            menu.Items.Add(addToSteam);

            var createManifest = new MenuItem { Header = "Create manifest" };
            createManifest.Click += (s, e) => CreateSteamManifestForInstalled(game.AppId, displayName);
            menu.Items.Add(createManifest);

            var chooseInstallFolder = new MenuItem { Header = "Choose install folder" };
            chooseInstallFolder.Click += (s, e) => LinkInstalledGameToInstallFolder(game, displayName);
            menu.Items.Add(chooseInstallFolder);

            return menu;
        }

        private ContextMenu BuildLibraryCardMenuForBookmark(SavedLibraryGame bookmark, string displayName)
        {
            var menu = new ContextMenu();

            var addToSteam = new MenuItem { Header = "Add to Steam" };
            if (string.IsNullOrWhiteSpace(bookmark?.AppId) || !_luaByAppId.TryGetValue(bookmark.AppId, out var st))
            {
                addToSteam.IsEnabled = !string.IsNullOrWhiteSpace(bookmark?.AppId);
                addToSteam.ToolTip = "Try Add to Steam (will report exact missing manifest paths if not found).";
                addToSteam.Click += (s, e) => AddToSteamFromLibrary(bookmark.AppId, displayName, null, false);
            }
            else
            {
                addToSteam.IsEnabled = true;
                addToSteam.ToolTip = st.Tooltip;
                addToSteam.Click += (s, e) => AddToSteamFromLibrary(bookmark.AppId, displayName, st.ManifestZipPath, st.AlreadyAdded);
            }
            menu.Items.Add(addToSteam);

            var createManifest = new MenuItem { Header = "Create manifest" };
            createManifest.Click += (s, e) => CreateSteamManifestForInstalled(bookmark.AppId, displayName);
            menu.Items.Add(createManifest);

            var chooseInstallFolder = new MenuItem { Header = "Choose install folder" };
            chooseInstallFolder.Click += (s, e) => LinkBookmarkToInstallFolder(bookmark, displayName);
            menu.Items.Add(chooseInstallFolder);

            return menu;
        }

        private void LinkBookmarkToInstallFolder(SavedLibraryGame bookmark, string displayName)
        {
            if (bookmark == null || string.IsNullOrWhiteSpace(bookmark.AppId) || _installedGamesManager == null)
                return;

            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the installed game folder for \"" + displayName + "\"",
                ShowNewFolderButton = false
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            var selectedPath = dialog.SelectedPath.Trim();
            if (!Directory.Exists(selectedPath))
                return;

            var installDir = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var libraryRoot = ResolveSteamLibraryRootFromInstallPath(selectedPath);
            var size = GetDirectorySizeSafe(selectedPath);

            var existing = _installedGamesManager.FindByAppId(bookmark.AppId);
            var linked = existing ?? new InstalledGame { AppId = bookmark.AppId };
            linked.GameName = displayName;
            linked.InstallPath = selectedPath;
            linked.LibraryPath = libraryRoot;
            linked.InstallDir = string.IsNullOrWhiteSpace(installDir) ? linked.InstallDir : installDir;
            linked.InstalledDate = linked.InstalledDate == default(DateTime) ? DateTime.UtcNow : linked.InstalledDate;
            linked.SizeOnDisk = size > 0 ? size : linked.SizeOnDisk;
            if (!string.IsNullOrWhiteSpace(bookmark.HeaderImageUrl) && string.IsNullOrWhiteSpace(linked.HeaderImageUrl))
                linked.HeaderImageUrl = bookmark.HeaderImageUrl;

            _installedGamesManager.Save(linked);
            logger.Info("Manually linked install for app " + bookmark.AppId + " to: " + selectedPath);
            RefreshLibraryList();
        }

        private void LinkInstalledGameToInstallFolder(InstalledGame game, string displayName)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.AppId) || _installedGamesManager == null)
                return;

            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the installed game folder for \"" + displayName + "\"",
                ShowNewFolderButton = false
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            var selectedPath = dialog.SelectedPath.Trim();
            if (!Directory.Exists(selectedPath))
                return;

            game.InstallPath = selectedPath;
            game.InstallDir = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var libraryRoot = ResolveSteamLibraryRootFromInstallPath(selectedPath);
            if (!string.IsNullOrWhiteSpace(libraryRoot))
                game.LibraryPath = libraryRoot;
            var size = GetDirectorySizeSafe(selectedPath);
            if (size > 0)
                game.SizeOnDisk = size;
            _installedGamesManager.Save(game);
            RefreshLibraryList();
        }

        private void FetchManifestGidsForApp(string appId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(appId) || _installedGamesManager == null)
                return;

            var installed = _installedGamesManager.FindByAppId(appId);
            if (installed == null || string.IsNullOrWhiteSpace(installed.InstallPath) || !Directory.Exists(installed.InstallPath))
            {
                _dialogService?.ShowMessage(
                    "Pick the game folder first via \"Choose install folder\", then fetch manifest GIDs.",
                    "Fetch manifest GIDs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var runner = new ManifestCheckerRunner();
                    if (!runner.IsReady)
                    {
                        Dispatch(() => _dialogService?.ShowMessage(
                            "ManifestChecker.exe is not available in plugin deps.",
                            "Fetch manifest GIDs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                        return;
                    }

                    var run = runner.Run(new[] { appId });
                    if (run.results == null)
                    {
                        Dispatch(() => _dialogService?.ShowMessage(
                            "Could not fetch manifest GIDs: " + (run.error ?? "Unknown error"),
                            "Fetch manifest GIDs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                        return;
                    }

                    var appResults = run.results
                        .Where(r => string.Equals(r.AppId, appId, StringComparison.Ordinal))
                        .Where(r => !string.IsNullOrWhiteSpace(r.DepotId) && !string.IsNullOrWhiteSpace(r.ManifestGid))
                        .ToList();

                    if (appResults.Count == 0)
                    {
                        Dispatch(() => _dialogService?.ShowMessage(
                            "Steam returned no public depot manifest GIDs for this app.",
                            "Fetch manifest GIDs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                        return;
                    }

                    if (installed.ManifestGIDs == null)
                        installed.ManifestGIDs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var item in appResults)
                        installed.ManifestGIDs[item.DepotId] = item.ManifestGid;

                    installed.SelectedDepots = installed.ManifestGIDs.Keys
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var firstBuildId = appResults
                        .Select(r => r.BuildId)
                        .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
                    if (!string.IsNullOrWhiteSpace(firstBuildId))
                        installed.SteamBuildId = firstBuildId;

                    _installedGamesManager.Save(installed);

                    Dispatch(() =>
                    {
                        _dialogService?.ShowMessage(
                            "Fetched " + appResults.Count + " manifest GID(s) for \"" + displayName + "\".",
                            "Fetch manifest GIDs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        RefreshLibraryList();
                    });
                }
                catch (Exception ex)
                {
                    logger.Warn("FetchManifestGidsForApp failed: " + ex.Message);
                    Dispatch(() => _dialogService?.ShowMessage(
                        "Failed to fetch manifest GIDs: " + ex.Message,
                        "Fetch manifest GIDs",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
                }
            });
        }

        private static string ResolveSteamLibraryRootFromInstallPath(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
                return null;

            var fullPath = Path.GetFullPath(installPath);
            var di = new DirectoryInfo(fullPath);
            if (di.Parent == null || di.Parent.Parent == null)
                return null;

            // Expect: <library>\steamapps\common\<game folder>
            if (!string.Equals(di.Parent.Name, "common", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(di.Parent.Parent.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return null;

            var root = di.Parent.Parent.Parent;
            return root != null ? root.FullName : null;
        }

        private static long GetDirectorySizeSafe(string path)
        {
            try
            {
                long total = 0;
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    total += file.Length;
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        private Dictionary<string, LuaIntegrationState> BuildLuaStateMap(IEnumerable<string> appIds)
        {
            var map = new Dictionary<string, LuaIntegrationState>(StringComparer.Ordinal);
            var steamPath = _steamIntegration.GetSteamPath();

            foreach (var appIdRaw in appIds)
            {
                var appId = appIdRaw?.Trim();
                if (string.IsNullOrWhiteSpace(appId))
                    continue;

                var st = new LuaIntegrationState();
                map[appId] = st;

                var cacheRoot = _appHost != null ? ManifestCache.GetCacheDirectory(_appHost.UserDataPath) : null;
                var installed = _installedGamesManager != null ? _installedGamesManager.FindByAppId(appId) : null;
                var preferredZip = installed != null ? installed.ManifestZipPath : null;
                var zip = _steamIntegration.ResolveManifestZip(appId, preferredZip, cacheRoot);
                st.ManifestZipPath = zip;
                if (string.IsNullOrWhiteSpace(zip) || !File.Exists(zip))
                {
                    st.CanAdd = false;
                    st.AlreadyAdded = false;
                    st.Tooltip = "No cached manifest ZIP for this game.";
                    continue;
                }

                try
                {
                    var luaPath = _steamIntegration.ExtractSingleLua(zip);
                    var luaName = Path.GetFileName(luaPath);
                    st.LuaFileName = luaName;
                    if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                    {
                        st.CanAdd = false;
                        st.AlreadyAdded = false;
                        st.Tooltip = "Steam install path not detected.";
                        continue;
                    }

                    st.AlreadyAdded = _steamIntegration.IsLuaPresentInSteamConfig(steamPath, luaName);
                    st.CanAdd = !st.AlreadyAdded;
                    st.Tooltip = st.AlreadyAdded ? "Lua already exists in Steam config." : "Copy Lua to Steam config.";
                }
                catch (Exception ex)
                {
                    st.CanAdd = false;
                    st.AlreadyAdded = false;
                    st.Tooltip = "Manifest Lua error: " + ex.Message;
                }
            }

            return map;
        }

        private void ConfigureAddToSteamButton(Button button, string appId, string displayName)
        {
            if (button == null)
                return;

            if (string.IsNullOrWhiteSpace(appId) || !_luaByAppId.TryGetValue(appId, out var st))
            {
                button.IsEnabled = false;
                button.ToolTip = "No Lua info available.";
                return;
            }

            button.IsEnabled = st.CanAdd;
            button.ToolTip = st.Tooltip;
            button.Click += (s, e) => AddToSteamFromLibrary(appId, displayName, st.ManifestZipPath, st.AlreadyAdded);
        }

        private void AddToSteamFromLibrary(string appId, string displayName, string manifestZipPath, bool luaAlreadyAdded)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var steamPath = _steamIntegration.GetSteamPath();
                    string luaTarget = null;
                    if (!luaAlreadyAdded)
                    {
                        var zip = ResolveManifestZipStrict(appId, manifestZipPath);
                        var lua = _steamIntegration.ExtractSingleLua(zip);
                        luaTarget = _steamIntegration.CopyLuaToSteamConfig(lua, steamPath, true);
                        logger.Info("Library Add to Steam (Lua): " + appId + " -> " + luaTarget);
                    }
                    var manifestStatus = EnsureSteamManifestForInstalled(appId, displayName);

                    var restart = false;
                    Dispatch(() =>
                    {
                        var summary = string.IsNullOrWhiteSpace(luaTarget)
                            ? "Lua already exists in Steam config."
                            : "Added \"" + displayName + "\" Lua to Steam config.";
                        restart = _dialogService.ShowMessage(
                            summary + "\n" + manifestStatus + "\n\nRestart Steam now?",
                            "Add to Steam",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes;
                    });

                    if (restart)
                        _steamIntegration.RestartSteam(steamPath);
                }
                catch (Exception ex)
                {
                    Dispatch(() =>
                    {
                        _dialogService.ShowMessage("Add to Steam failed: " + ex.Message,
                            "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    Dispatch(() => RefreshLibraryList());
                }
            });
        }

        private string ResolveManifestZipStrict(string appId, string manifestZipPath)
        {
            var tried = new List<string>();
            string found = null;

            if (!string.IsNullOrWhiteSpace(manifestZipPath))
            {
                var p = Path.GetFullPath(manifestZipPath);
                tried.Add(p + " (explicit)");
                if (File.Exists(p))
                    found = p;
            }

            var cacheRoot = _appHost != null ? ManifestCache.GetCacheDirectory(_appHost.UserDataPath) : null;
            var cached = ManifestCache.GetCachedZipPath(cacheRoot, appId);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                tried.Add(cached + " (cache by appId)");
                if (found == null && File.Exists(cached))
                    found = cached;
            }

            var installed = _installedGamesManager != null ? _installedGamesManager.FindByAppId(appId) : null;
            if (installed != null && !string.IsNullOrWhiteSpace(installed.ManifestZipPath))
            {
                var p = Path.GetFullPath(installed.ManifestZipPath);
                tried.Add(p + " (installed record)");
                if (found == null && File.Exists(p))
                    found = p;
            }

            if (found != null)
            {
                if (installed != null && !string.Equals(installed.ManifestZipPath, found, StringComparison.OrdinalIgnoreCase))
                {
                    installed.ManifestZipPath = found;
                    _installedGamesManager.Save(installed);
                }
                return found;
            }

            throw new FileNotFoundException(
                "Manifest ZIP not found for app " + appId + ". Checked:\n" + string.Join("\n", tried));
        }

        private string EnsureSteamManifestForInstalled(string appId, string displayName)
        {
            if (_installedGamesManager == null)
                return "Manifest: skipped (manager unavailable).";

            var installed = _installedGamesManager.FindByAppId(appId);
            if (installed == null || string.IsNullOrWhiteSpace(installed.InstallPath) || !Directory.Exists(installed.InstallPath))
                return "Manifest: skipped (choose install folder first).";

            if (installed.ManifestGIDs == null || installed.ManifestGIDs.Count == 0)
            {
                var runner = new ManifestCheckerRunner();
                if (!runner.IsReady)
                    return "Manifest: skipped (ManifestChecker not available).";

                var run = runner.Run(new[] { appId });
                if (run.results == null)
                    return "Manifest: skipped (could not fetch GIDs: " + (run.error ?? "unknown") + ").";

                var appResults = run.results
                    .Where(r => string.Equals(r.AppId, appId, StringComparison.Ordinal))
                    .Where(r => !string.IsNullOrWhiteSpace(r.DepotId) && !string.IsNullOrWhiteSpace(r.ManifestGid))
                    .ToList();
                if (appResults.Count == 0)
                    return "Manifest: skipped (Steam returned no public depot GIDs).";

                installed.ManifestGIDs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in appResults)
                    installed.ManifestGIDs[item.DepotId] = item.ManifestGid;
                installed.SelectedDepots = installed.ManifestGIDs.Keys.ToList();
                var firstBuildId = appResults
                    .Select(r => r.BuildId)
                    .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
                if (!string.IsNullOrWhiteSpace(firstBuildId))
                    installed.SteamBuildId = firstBuildId;
                _installedGamesManager.Save(installed);
            }

            var libraryRoot = string.IsNullOrWhiteSpace(installed.LibraryPath)
                ? ResolveSteamLibraryRootFromInstallPath(installed.InstallPath)
                : installed.LibraryPath;
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return "Manifest: skipped (Steam library root not detected from install path).";

            var gameData = new GameData
            {
                AppId = appId,
                GameName = string.IsNullOrWhiteSpace(installed.GameName) ? displayName : installed.GameName,
                InstallDir = string.IsNullOrWhiteSpace(installed.InstallDir)
                    ? Path.GetFileName(installed.InstallPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : installed.InstallDir,
                BuildId = string.IsNullOrWhiteSpace(installed.SteamBuildId) ? "0" : installed.SteamBuildId,
                Manifests = new Dictionary<string, string>(installed.ManifestGIDs, StringComparer.OrdinalIgnoreCase),
                SelectedDepots = installed.ManifestGIDs.Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var acfPath = _steamIntegration.WriteAcfForInstall(gameData, libraryRoot, line => logger.Info(line));
            installed.LibraryPath = libraryRoot;
            installed.InstallDir = gameData.InstallDir;
            installed.RegisteredWithSteam = true;
            _installedGamesManager.Save(installed);
            _updateChecker?.MarkUpToDate(appId);
            return "Manifest: written to " + acfPath + ".";
        }

        private void CreateSteamManifestForInstalled(string appId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var manifestStatus = EnsureSteamManifestForInstalled(appId, displayName);
                    Dispatch(() =>
                    {
                        _dialogService?.ShowMessage(manifestStatus, "Create manifest",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        RefreshLibraryList();
                    });
                }
                catch (Exception ex)
                {
                    logger.Warn("CreateSteamManifestForInstalled failed: " + ex.Message);
                    Dispatch(() => _dialogService?.ShowMessage(
                        "Create manifest failed: " + ex.Message,
                        "Create manifest",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
                }
            });
        }

        private enum LibraryHeaderPersistTarget
        {
            Installed,
            Bookmark
        }

        /// <summary>
        /// Landscape header: use cached <paramref name="storedHeaderUrl"/> if it downloads, else Steam store
        /// <c>header_image</c> (persisted to JSON), else CDN guesses. Does not use Playnite covers.
        /// </summary>
        private void LoadLibraryCardHeaderAsync(string appId, string storedHeaderUrl, Image imgElement,
            LibraryHeaderPersistTarget persistTarget)
        {
            if (string.IsNullOrWhiteSpace(appId) || imgElement == null)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ImageSource bmp = null;
                    var resolvedUrl = string.IsNullOrWhiteSpace(storedHeaderUrl) ? null : storedHeaderUrl.Trim();

                    if (!string.IsNullOrWhiteSpace(resolvedUrl))
                        bmp = TryDownloadHeaderBitmap(resolvedUrl, appId);

                    if (bmp == null)
                    {
                        try
                        {
                            var steamClient = new SteamApiClient(() => _settings?.SteamWebApiKey ?? "");
                            var details = steamClient.GetGameDetails(appId);
                            if (details != null && !string.IsNullOrWhiteSpace(details.HeaderImageUrl))
                            {
                                resolvedUrl = details.HeaderImageUrl.Trim();
                                try
                                {
                                    PersistLibraryHeaderUrl(appId, resolvedUrl, persistTarget);
                                }
                                catch (Exception ex)
                                {
                                    logger.Debug("LoadLibraryCardHeaderAsync: cache write failed: " + ex.Message);
                                }

                                bmp = TryDownloadHeaderBitmap(resolvedUrl, appId);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Debug("LoadLibraryCardHeaderAsync: GetGameDetails failed appId=" + appId + ": " +
                                         ex.Message);
                        }
                    }

                    if (bmp == null)
                        bmp = LoadSteamHeaderImageFromCdn(appId);

                    if (bmp != null)
                        Dispatcher.Invoke(() => imgElement.Source = bmp);
                }
                catch (Exception ex)
                {
                    logger.Debug("LoadLibraryCardHeaderAsync failed for appId=" + appId + ": " + ex.Message);
                }
            });
        }

        private void PersistLibraryHeaderUrl(string appId, string imageUrl, LibraryHeaderPersistTarget target)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(imageUrl))
                return;

            switch (target)
            {
                case LibraryHeaderPersistTarget.Installed:
                    if (_installedGamesManager != null)
                    {
                        var entry = _installedGamesManager.FindByAppId(appId);
                        if (entry != null)
                        {
                            entry.HeaderImageUrl = imageUrl;
                            _installedGamesManager.Save(entry);
                        }
                    }
                    break;
                case LibraryHeaderPersistTarget.Bookmark:
                    if (_libraryGames != null)
                    {
                        var bm = _libraryGames.GetAll().FirstOrDefault(b => b.AppId == appId);
                        _libraryGames.AddOrUpdate(appId, bm?.GameName, imageUrl);
                    }
                    break;
            }
        }

        /// <summary>
        /// Stream must stay open through <see cref="BitmapImage.EndInit"/> with <see cref="BitmapCacheOption.OnLoad"/>.
        /// </summary>
        private static ImageSource DecodeLibraryHeaderFromStream(Stream stream)
        {
            if (stream == null)
                return null;

            var decodeW = LibraryThumbWidthDip * LibrarySteamDecodeScale;
            var decodeH = (int)(decodeW * 215.0 / 460.0 + 0.5);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource     = stream;
            bmp.DecodePixelWidth  = decodeW;
            bmp.DecodePixelHeight = decodeH;
            bmp.CacheOption       = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }

        private static ImageSource TryDownloadHeaderBitmap(string imageUrl, string appIdForLog)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return null;

            try
            {
                using (var wc = new WebClient())
                {
                    var bytes = wc.DownloadData(imageUrl);
                    using (var ms = new MemoryStream(bytes))
                        return DecodeLibraryHeaderFromStream(ms);
                }
            }
            catch (Exception ex)
            {
                logger.Debug("TryDownloadHeaderBitmap: failed appId=" + appIdForLog + " url=" + imageUrl + ": " +
                             ex.Message);
                return null;
            }
        }

        /// <summary>Last resort: public Steam CDN filenames (e.g. header.jpg).</summary>
        private static ImageSource LoadSteamHeaderImageFromCdn(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return null;

            foreach (var uri in SteamStoreImageUrls.GetHeaderStyleCoverUris(appId))
            {
                try
                {
                    using (var wc = new WebClient())
                    {
                        var bytes = wc.DownloadData(uri);
                        using (var ms = new MemoryStream(bytes))
                            return DecodeLibraryHeaderFromStream(ms);
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug("LoadSteamHeaderImageFromCdn: failed " + uri + " for appId=" + appId + ": " +
                                 ex.Message);
                }
            }

            return null;
        }

        // ── Uninstall ────────────────────────────────────────────────────────────

        private void UninstallGame(InstalledGame game)
        {
                       var confirmMsg = string.Format(
                "Uninstall \"{0}\"?\n\nThis will delete:\n{1}\n\nSave files in Documents/My Games and AppData will be preserved.",
                game.GameName, game.InstallPath);
            var result = _dialogService.ShowMessage(confirmMsg, "Uninstall Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

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

                if (game.PlayniteGameId != Guid.Empty)
                {
                    var removeFromHost = _dialogService.ShowMessage(
                        "Remove \"" + game.GameName + "\" from library as well?",
                        "Uninstall Game",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (removeFromHost == MessageBoxResult.Yes)
                        _appHost?.RemoveFromHostLibrary(game.PlayniteGameId.ToString());
                }

                RefreshLibraryList();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error during uninstall: " + ex.Message, "Uninstall Error",
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
                    CacheManifestZipForApp(zipPath, data);
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

        private void CacheManifestZipForApp(string sourceZipPath, GameData data)
        {
            try
            {
                if (_appHost == null || data == null || string.IsNullOrWhiteSpace(data.AppId) || string.IsNullOrWhiteSpace(sourceZipPath))
                    return;

                var cacheRoot = ManifestCache.GetCacheDirectory(_appHost.UserDataPath);
                if (string.IsNullOrWhiteSpace(cacheRoot))
                    return;

                var partPath = ManifestCache.GetPartPath(cacheRoot, data.AppId);
                var finalPath = ManifestCache.GetCachedZipPath(cacheRoot, data.AppId);
                if (string.IsNullOrWhiteSpace(partPath) || string.IsNullOrWhiteSpace(finalPath))
                    return;

                ManifestCache.TryDeletePartFile(partPath);
                File.Copy(sourceZipPath, partPath, true);
                ManifestCache.CommitPartToZip(cacheRoot, data.AppId);
                ManifestCache.WriteMeta(cacheRoot, data);
                logger.Info("Cached manifest ZIP from Install-from-ZIP: " + finalPath);
            }
            catch (Exception ex)
            {
                logger.Warn("Could not cache manifest ZIP from Install-from-ZIP: " + ex.Message);
            }
        }

        /// <summary>
        /// Replaces the library view with a download view in the same window.
        /// Called when the user picks a manifest ZIP to install from.
        /// </summary>
        private void SwitchToDownloadView(GameData data)
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            window.Title = "LuDownloader — " + data.GameName;
            var manifestCache = ManifestCache.GetCacheDirectory(_appHost.UserDataPath);
            window.Content = new DownloadView(_settings, _installedGamesManager, _dialogService, _appHost, _updateChecker, manifestCache, data);
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
