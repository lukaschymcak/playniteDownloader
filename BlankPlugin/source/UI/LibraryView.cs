using Microsoft.Win32;
using Playnite.SDK;
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
        private static readonly ILogger logger = LogManager.GetLogger();

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

        private readonly BlankPluginSettings _settings;
        private readonly IPlayniteAPI _api;
        private readonly InstalledGamesManager _installedGamesManager;
        private readonly LibraryGamesManager _libraryGames;
        private readonly BlankPlugin _plugin;
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

        public LibraryView(BlankPluginSettings settings, InstalledGamesManager installedGamesManager, LibraryGamesManager libraryGames, IPlayniteAPI api, UpdateChecker updateChecker, BlankPlugin plugin)
        {
            _settings = settings;
            _api = api;
            _installedGamesManager = installedGamesManager;
            _libraryGames = libraryGames;
            _plugin = plugin;
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

            var updateStatus = _updateChecker?.GetStatus(game.AppId);
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
                    var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = false,
                        ShowCloseButton = true
                    });
                    window.Title = "Update Game — " + game.GameName;
                    window.Width = 480;
                    window.Height = 300;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    window.Owner = _api.Dialogs.GetCurrentAppWindow();
                    window.Content = new UpdateGameDialog(game, _settings, _installedGamesManager, _api, _updateChecker);
                    window.ShowDialog();
                    Dispatch(() => RefreshLibraryList());
                };
                btnStack.Children.Add(updateBtn);
            }

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
                if (_plugin != null)
                    _plugin.OpenDownloadForAppId(bookmark.AppId, displayName, bookmark.HeaderImageUrl);
            };

            var removeBtn = new Button
            {
                Content = "Remove",
                Width = 72,
                Height = LibraryCardButtonHeight,
                FontSize = LibraryCardButtonFontSize
            };
            removeBtn.Click += (s, e) => RemoveBookmark(bookmark, displayName);

            btnStack.Children.Add(downloadBtn);
            btnStack.Children.Add(removeBtn);
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
                      "(This does not uninstall games or remove them from Playnite.)";
            var result = _api != null
                ? _api.Dialogs.ShowMessage(msg, "Remove from saved library", MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                : MessageBox.Show(msg, "Remove from saved library", MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _libraryGames.Remove(bookmark.AppId);
            RefreshLibraryList();
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
                            var steamClient = new SteamApiClient(() => _plugin?.Settings?.SteamWebApiKey ?? "");
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
            var result = _api != null
                ? _api.Dialogs.ShowMessage(confirmMsg, "Uninstall Game", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(confirmMsg, "Uninstall Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

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

                if (_api != null && game.PlayniteGameId != Guid.Empty)
                {
                    var removeFromPlaynite = _api.Dialogs.ShowMessage(
                        "Remove \"" + game.GameName + "\" from Playnite library as well?",
                        "Remove from Library",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (removeFromPlaynite == MessageBoxResult.Yes)
                        _api.Database.Games.Remove(game.PlayniteGameId);
                }

                RefreshLibraryList();
            }
            catch (Exception ex)
            {
                if (_api != null)
                    _api.Dialogs.ShowMessage("Error during uninstall: " + ex.Message, "Uninstall Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                else
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
            var manifestCache = ManifestCache.GetCacheDirectory(_plugin.GetPluginUserDataPath());
            window.Content = new DownloadView(null, _settings, _installedGamesManager, _api, _updateChecker, manifestCache, data);
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
