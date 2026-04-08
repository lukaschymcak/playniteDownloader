using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlankPlugin
{
    /// <summary>
    /// Download view — shown when a game is right-clicked and opened in BlankPlugin,
    /// or when "Install from ZIP" is chosen in the library view.
    /// Handles manifest fetch, depot selection, and DepotDownloader invocation.
    /// </summary>
    public class DownloadView : UserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly Guid SteamPluginGuid = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        // ── Services ─────────────────────────────────────────────────────────────
        private readonly Game _game;
        private readonly BlankPluginSettings _settings;
        private readonly IPlayniteAPI _api;
        private readonly MorrenusClient _client;
        private readonly DepotDownloaderRunner _downloader;

        // ── State ────────────────────────────────────────────────────────────────
        private GameData _gameData;
        private Thread _workerThread;
        private bool _downloading;
        private string _resolvedAppId;
        private string _currentDownloadDir;

        // ── Network speed monitor ─────────────────────────────────────────────────
        private DispatcherTimer _speedTimer;
        private long _lastBytesReceived = -1;
        private readonly Queue<long> _speedSamples = new Queue<long>();
        private const int SpeedSampleWindow = 5;

        // ── ETA / elapsed tracking ───────────────────────────────────────────────
        private DateTime _downloadStartTime;
        private TextBlock _etaLabel;

        // ── Post-download actions ─────────────────────────────────────────────────
        private Button _postSteamlessBtn;
        private Button _postRegisterBtn;
        private string _lastDestPath;

        // ── Installed game tracking ───────────────────────────────────────────────
        private readonly InstalledGamesManager _installedGamesManager;
        private InstalledGame _installedGame;
        private readonly UpdateChecker _updateChecker;

        // ── Installed game UI ─────────────────────────────────────────────────────
        private UIElement _installedPanel;
        private Button _uninstallBtn;
        private Button _openFolderBtn;
        private Button _reinstallBtn;

        // ── UI: Status bar ────────────────────────────────────────────────────────
        private TextBlock _statusLabel;
        private TextBlock _apiStatusDot;
        private TextBlock _usageLabel;

        // ── UI: Game info ─────────────────────────────────────────────────────────
        private TextBlock _gameInfoLabel;
        private TextBlock _resolveStatusLabel;
        private UIElement _foundPanel;
        private TextBlock _foundLabel;
        private Button _fetchBtn;
        private UIElement _searchPanel;
        private TextBox _searchTextBox;
        private Button _searchBtn;
        private ComboBox _searchResultsCombo;
        private Button _fetchBtnSearch;

        // ── UI: Depots ────────────────────────────────────────────────────────────
        private ScrollViewer _depotScroll;
        private StackPanel _depotList;

        // ── UI: Options ───────────────────────────────────────────────────────────
        private CheckBox _steamlessCheck;
        private CheckBox _registerSteamCheck;
        private string _selectedLibraryPath;
        private TextBlock _selectedLibLabel;

        // ── UI: Progress / log ────────────────────────────────────────────────────
        private Button _downloadBtn;
        private Button _stopBtn;
        private Button _deleteBtn;
        private ProgressBar _progressBar;
        private TextBlock _speedLabel;
        private TextBox _logBox;

        /// <param name="game">The Playnite game to download. Pass null when pre-loading from a manifest ZIP.</param>
        /// <param name="initialData">Pre-loaded manifest data (from "Install from ZIP"). When provided, depots are populated immediately.</param>
        public DownloadView(Game game, BlankPluginSettings settings, InstalledGamesManager installedGamesManager, IPlayniteAPI api, UpdateChecker updateChecker, GameData initialData = null)
        {
            _game = game;
            _settings = settings;
            _api = api;
            _client = new MorrenusClient(() => _settings.ApiKey);
            _downloader = new DepotDownloaderRunner();
            _installedGamesManager = installedGamesManager;
            _updateChecker = updateChecker;

            Content = BuildLayout();

            ThreadPool.QueueUserWorkItem(_ => RefreshApiStatus());

            if (initialData != null)
            {
                // Pre-loaded from a manifest ZIP — skip resolution, go straight to depot selection
                _gameData = initialData;
                _resolvedAppId = initialData.AppId;
                _gameInfoLabel.Text = initialData.GameName + "  (AppID: " + initialData.AppId + ")";
                _resolveStatusLabel.Visibility = Visibility.Collapsed;
                PopulateDepots(initialData);
            }
            else if (_game != null)
            {
                _gameInfoLabel.Text = _game.Name;

                if (_installedGamesManager != null)
                    CheckIfInstalled();

                if (_installedGame == null)
                    ThreadPool.QueueUserWorkItem(_ => ResolveAppId());
            }
        }

        /// <summary>
        /// Constructor used when launching from a Steam search result.
        /// Skips game resolution and goes directly to the "found" state with the given appId.
        /// </summary>
        public DownloadView(
            string appId,
            string name,
            BlankPluginSettings settings,
            InstalledGamesManager installedGamesManager,
            IPlayniteAPI api,
            UpdateChecker updateChecker)
        {
            _game    = null;
            _settings = settings;
            _api      = api;
            _client   = new MorrenusClient(() => _settings.ApiKey);
            _downloader = new DepotDownloaderRunner();
            _installedGamesManager = installedGamesManager;
            _updateChecker = updateChecker;

            Content = BuildLayout();

            ThreadPool.QueueUserWorkItem(_ => RefreshApiStatus());

            // Skip resolution — go directly to "found" state
            _resolvedAppId = appId;
            _gameInfoLabel.Text = name;
            ShowFoundPanel(appId, "\u2713 " + name + " (" + appId + ")");
        }

        // ── Layout ───────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 0 status bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 1 game info
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2 depots
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 3 options
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 4 progress
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });                  // 5 log

            var statusBar = BuildApiStatusBar();
            Grid.SetRow(statusBar, 0);
            root.Children.Add(statusBar);

            var gameRow = BuildGameInfoRow();
            Grid.SetRow(gameRow, 1);
            root.Children.Add(gameRow);

            var depotPanel = BuildDepotPanel();
            Grid.SetRow(depotPanel, 2);
            root.Children.Add(depotPanel);

            var optionsPanel = BuildOptionsPanel();
            Grid.SetRow(optionsPanel, 3);
            root.Children.Add(optionsPanel);

            var progressRow = BuildProgressRow();
            Grid.SetRow(progressRow, 4);
            root.Children.Add(progressRow);

            var logPanel = BuildLogPanel();
            Grid.SetRow(logPanel, 5);
            root.Children.Add(logPanel);

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

        private UIElement BuildGameInfoRow()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            _gameInfoLabel = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 6)
            };
            panel.Children.Add(_gameInfoLabel);

            // ── State A: resolving ────────────────────────────────────────────────
            _resolveStatusLabel = new TextBlock
            {
                Text = "Identifying game...",
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Visibility = _game != null ? Visibility.Visible : Visibility.Collapsed
            };
            panel.Children.Add(_resolveStatusLabel);

            // ── State B: confident match ──────────────────────────────────────────
            var foundRow = new DockPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };

            _fetchBtn = new Button
            {
                Content = "Fetch from Morrenus",
                Padding = new Thickness(16, 8, 16, 8),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false
            };
            _fetchBtn.Click += OnFetchClicked;
            DockPanel.SetDock(_fetchBtn, Dock.Right);

            _foundLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.LimeGreen,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0)
            };
            foundRow.Children.Add(_fetchBtn);
            foundRow.Children.Add(_foundLabel);
            _foundPanel = foundRow;
            panel.Children.Add(_foundPanel);

            // ── State C: search needed ────────────────────────────────────────────
            var searchStack = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };

            searchStack.Children.Add(new TextBlock
            {
                Text = "Could not auto-identify game. Search by name:",
                Foreground = Brushes.Orange,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var searchInputRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            _searchBtn = new Button
            {
                Content = "Search",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0)
            };
            _searchBtn.Click += OnSearchClicked;
            DockPanel.SetDock(_searchBtn, Dock.Right);
            _searchTextBox = new TextBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = _game != null ? _game.Name : ""
            };
            searchInputRow.Children.Add(_searchBtn);
            searchInputRow.Children.Add(_searchTextBox);
            searchStack.Children.Add(searchInputRow);

            _searchResultsCombo = new ComboBox
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _searchResultsCombo.SelectionChanged += OnSearchResultSelected;
            searchStack.Children.Add(_searchResultsCombo);

            _fetchBtnSearch = new Button
            {
                Content = "Fetch from Morrenus",
                Padding = new Thickness(16, 8, 16, 8),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _fetchBtnSearch.Click += OnFetchClicked;
            searchStack.Children.Add(_fetchBtnSearch);

            _searchPanel = searchStack;
            panel.Children.Add(_searchPanel);

            // ── State D: already installed ────────────────────────────────────────
            var installedRow = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };

            installedRow.Children.Add(new TextBlock
            {
                Text = "This game is installed through BlankPlugin.",
                Foreground = Brushes.Cyan,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var installedBtnRow = new StackPanel { Orientation = Orientation.Horizontal };

            _openFolderBtn = new Button
            {
                Content = "Open Install Folder",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _openFolderBtn.Click += OnOpenFolderClicked;

            _reinstallBtn = new Button
            {
                Content = "Reinstall",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _reinstallBtn.Click += OnReinstallClicked;

            _uninstallBtn = new Button
            {
                Content = "Uninstall",
                Padding = new Thickness(12, 6, 12, 6)
            };
            _uninstallBtn.Click += OnUninstallClicked;

            installedBtnRow.Children.Add(_openFolderBtn);
            installedBtnRow.Children.Add(_reinstallBtn);
            installedBtnRow.Children.Add(_uninstallBtn);
            installedRow.Children.Add(installedBtnRow);

            _installedPanel = installedRow;
            panel.Children.Add(_installedPanel);

            return panel;
        }

        private UIElement BuildDepotPanel()
        {
            _depotList = new StackPanel();
            _depotList.Children.Add(new TextBlock
            {
                Text = "Click \"Fetch from Morrenus\" to load available depots.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(4)
            });

            _depotScroll = new ScrollViewer
            {
                Content = _depotList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            return _depotScroll;
        }

        private UIElement BuildOptionsPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Load from ZIP row — alternative to fetching from Morrenus
            var zipRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var loadZipBtn = new Button
            {
                Content = "Load manifest from ZIP file",
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            loadZipBtn.Click += (s, e) => OnLoadFromZipClicked(loadZipBtn);
            zipRow.Children.Add(loadZipBtn);
            panel.Children.Add(zipRow);

            // Steam library selector row
            var libraryRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var selectLibBtn = new Button
            {
                Content = "Select Steam Library",
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            DockPanel.SetDock(selectLibBtn, Dock.Left);
            _selectedLibLabel = new TextBlock
            {
                Text = "No library selected",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.Gray
            };
            selectLibBtn.Click += (s, e) =>
            {
                var libs = SteamLibraryHelper.GetSteamLibraries();
                if (libs.Count == 0)
                {
                    AppendLog("No Steam libraries detected.");
                    return;
                }
                var ownerWindow = Window.GetWindow(this);
                var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libs, _api);
                if (picked != null)
                {
                    _selectedLibraryPath = picked;
                    _selectedLibLabel.Text = picked;
                    _selectedLibLabel.Foreground = Brushes.White;
                }
            };
            libraryRow.Children.Add(selectLibBtn);
            libraryRow.Children.Add(_selectedLibLabel);
            panel.Children.Add(libraryRow);

            // Steam account row
            var steamRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var steamLabel = new TextBlock
            {
                Text = "Steam account:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Width = 100,
                ToolTip = "Optional — logging in removes CDN download throttling"
            };
            DockPanel.SetDock(steamLabel, Dock.Left);

            var authBtn = new Button
            {
                Content = "Authenticate",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Opens a console window to log in to Steam (required once for Steam Guard)"
            };
            DockPanel.SetDock(authBtn, Dock.Right);

            var usernameBox = new TextBox
            {
                Text = _settings.SteamUsername,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Leave blank to download without a Steam account (slower CDN)"
            };
            usernameBox.TextChanged += (s, e) =>
            {
                _settings.SteamUsername = usernameBox.Text.Trim();
                _settings.EndEdit();
            };

            authBtn.Click += (s, e) => OnAuthenticateClicked(usernameBox.Text.Trim());

            steamRow.Children.Add(steamLabel);
            steamRow.Children.Add(authBtn);
            steamRow.Children.Add(usernameBox);
            panel.Children.Add(steamRow);

            _steamlessCheck = new CheckBox
            {
                Content = "Strip DRM with Steamless (optional)",
                Margin = new Thickness(0, 2, 0, 2)
            };
            _registerSteamCheck = new CheckBox
            {
                Content = "Register with Steam as installed (writes appmanifest .acf)",
                Margin = new Thickness(0, 2, 0, 2)
            };
            panel.Children.Add(_steamlessCheck);
            panel.Children.Add(_registerSteamCheck);
            return panel;
        }

        private UIElement BuildProgressRow()
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            _speedLabel = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = Visibility.Collapsed
            };
            container.Children.Add(_speedLabel);

            _etaLabel = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = Visibility.Collapsed
            };
            container.Children.Add(_etaLabel);

            _progressBar = new ProgressBar
            {
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Margin = new Thickness(0, 0, 0, 4)
            };
            container.Children.Add(_progressBar);

            var buttonRow = new DockPanel();

            _downloadBtn = new Button
            {
                Content = "Download Selected Depots",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = false
            };
            _downloadBtn.Click += OnDownloadClicked;
            DockPanel.SetDock(_downloadBtn, Dock.Left);

            _stopBtn = new Button
            {
                Content = "Stop",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = false
            };
            _stopBtn.Click += OnStopClicked;
            DockPanel.SetDock(_stopBtn, Dock.Left);

            _deleteBtn = new Button
            {
                Content = "Delete Folder",
                Padding = new Thickness(12, 6, 12, 6),
                IsEnabled = false
            };
            _deleteBtn.Click += OnDeleteClicked;
            DockPanel.SetDock(_deleteBtn, Dock.Left);

            buttonRow.Children.Add(_downloadBtn);
            buttonRow.Children.Add(_stopBtn);
            buttonRow.Children.Add(_deleteBtn);
            container.Children.Add(buttonRow);

            var postRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

            _postSteamlessBtn = new Button
            {
                Content = "Run Steamless",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Strip Steam DRM from downloaded executables"
            };
            _postSteamlessBtn.Click += OnPostSteamlessClicked;
            DockPanel.SetDock(_postSteamlessBtn, Dock.Left);

            _postRegisterBtn = new Button
            {
                Content = "Register with Steam",
                Padding = new Thickness(12, 6, 12, 6),
                Visibility = Visibility.Collapsed,
                ToolTip = "Write Steam appmanifest .acf so Steam recognizes the game"
            };
            _postRegisterBtn.Click += OnPostRegisterClicked;
            DockPanel.SetDock(_postRegisterBtn, Dock.Left);

            postRow.Children.Add(_postSteamlessBtn);
            postRow.Children.Add(_postRegisterBtn);
            container.Children.Add(postRow);

            return container;
        }

        private UIElement BuildLogPanel()
        {
            _logBox = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap
            };
            return new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = _logBox
            };
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

        // ── AppID resolution ─────────────────────────────────────────────────────

        private void ResolveAppId()
        {
            // Step 1a: Steam library plugin — GameId IS the AppID
            if (_game.PluginId == SteamPluginGuid)
            {
                SetConfident(_game.GameId, "✓  " + _game.Name + "  (" + _game.GameId + ")");
                return;
            }

            // Step 1b: Steam store URL in game links
            var steamId = TryExtractSteamIdFromLinks();
            if (steamId != null)
            {
                SetConfident(steamId, "✓  " + _game.Name + "  (" + steamId + ")");
                return;
            }

            // Step 2: search by name
            try
            {
                var results = _client.SearchGames(_game.Name);
                if (results.Count == 1)
                    SetConfident(results[0].GameId, "✓  " + results[0].GameName + "  (" + results[0].GameId + ")");
                else
                    Dispatch(() => ShowSearchPanel(results));
            }
            catch (Exception ex)
            {
                logger.Warn("ResolveAppId search failed: " + ex.Message);
                Dispatch(() => ShowSearchPanel(new List<MorrenusSearchResult>()));
            }
        }

        private void SetConfident(string appId, string displayText)
        {
            _resolvedAppId = appId;
            ShowFoundPanel(appId, displayText);
        }

        private void ShowFoundPanel(string appId, string displayText)
        {
            Dispatcher.Invoke(() =>
            {
                _resolvedAppId            = appId;
                _foundLabel.Text          = displayText;
                _resolveStatusLabel.Visibility = Visibility.Collapsed;
                _searchPanel.Visibility   = Visibility.Collapsed;
                _foundPanel.Visibility    = Visibility.Visible;
                _fetchBtn.IsEnabled       = true;
            });
        }

        private void ShowSearchPanel(List<MorrenusSearchResult> initialResults)
        {
            _resolveStatusLabel.Visibility = Visibility.Collapsed;
            _searchPanel.Visibility = Visibility.Visible;

            if (initialResults.Count > 0)
            {
                foreach (var r in initialResults)
                    _searchResultsCombo.Items.Add(r);
                _searchResultsCombo.Visibility = Visibility.Visible;
                _searchResultsCombo.SelectedIndex = 0;
            }
        }

        private string TryExtractSteamIdFromLinks()
        {
            if (_game.Links == null) return null;
            foreach (var link in _game.Links)
            {
                if (link?.Url == null) continue;
                var m = Regex.Match(link.Url, @"store\.steampowered\.com/app/(\d+)");
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        private void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            var query = _searchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            _searchBtn.IsEnabled = false;
            _searchResultsCombo.Items.Clear();
            _searchResultsCombo.Visibility = Visibility.Collapsed;
            _fetchBtnSearch.IsEnabled = false;
            _resolvedAppId = null;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var results = _client.SearchGames(query);
                    Dispatch(() =>
                    {
                        _searchBtn.IsEnabled = true;
                        if (results.Count == 0)
                        {
                            AppendLog("No results for \"" + query + "\".");
                            return;
                        }
                        foreach (var r in results)
                            _searchResultsCombo.Items.Add(r);
                        _searchResultsCombo.Visibility = Visibility.Visible;
                        _searchResultsCombo.SelectedIndex = 0;
                    });
                }
                catch (Exception ex)
                {
                    Dispatch(() =>
                    {
                        _searchBtn.IsEnabled = true;
                        AppendLog("Search failed: " + ex.Message);
                    });
                }
            });
        }

        private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_searchResultsCombo.SelectedItem is MorrenusSearchResult result)
            {
                _resolvedAppId = result.GameId;
                _fetchBtnSearch.IsEnabled = !_downloading;
            }
        }

        // ── Fetch ────────────────────────────────────────────────────────────────

        private void OnFetchClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_resolvedAppId))
            {
                AppendLog("ERROR: No AppID resolved. Search for the game first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                AppendLog("ERROR: No API key configured. Open Settings first.");
                return;
            }

            var appId = _resolvedAppId;
            SetBusy(true, "Fetching manifest...");
            _depotList.Children.Clear();

            _workerThread = new Thread(() =>
            {
                try
                {
                    AppendLog("Downloading manifest for AppID " + appId + "...");
                    var zipPath = _client.DownloadManifest(appId,
                        new Progress<int>(pct => Dispatch(() => _progressBar.Value = pct)));

                    AppendLog("Processing ZIP...");
                    var data = new ZipProcessor().Process(zipPath);
                    _gameData = data;

                    Dispatch(() => PopulateDepots(data));
                    AppendLog("Ready. " + data.Depots.Count + " depots available.");
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR: " + ex.Message);
                    logger.Error("Fetch failed: " + ex);
                }
                finally
                {
                    Dispatch(() => SetBusy(false, ""));
                }
            });
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void PopulateDepots(GameData data)
        {
            _depotList.Children.Clear();

            if (data.Depots.Count == 0)
            {
                _depotList.Children.Add(new TextBlock
                {
                    Text = "No downloadable depots found.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(4)
                });
                return;
            }

            var sorted = data.Depots
                .OrderBy(kv => OsSortKey(kv.Value.OsList))
                .ThenBy(kv => kv.Value.Description)
                .ToList();

            foreach (var kv in sorted)
            {
                var depotId = kv.Key;
                var info = kv.Value;
                var cb = new CheckBox
                {
                    Tag = depotId,
                    Margin = new Thickness(4, 2, 4, 2),
                    IsChecked = info.OsList == null || info.OsList.Equals("windows", StringComparison.OrdinalIgnoreCase)
                };
                var sizeText = info.Size > 0 ? "  [" + SteamLibraryHelper.FormatSize(info.Size) + "]" : "";
                cb.Content = string.Format("[{0}] {1}{2}  (id: {3})",
                    (info.OsList ?? "?").ToUpper(), info.Description, sizeText, depotId);
                _depotList.Children.Add(cb);
            }

            _downloadBtn.IsEnabled = true;
        }

        // ── Download ─────────────────────────────────────────────────────────────

        private void OnDownloadClicked(object sender, RoutedEventArgs e)
        {
            if (_gameData == null || _downloading) return;

            string destPath;
            if (_selectedLibraryPath != null)
            {
                destPath = _selectedLibraryPath;
            }
            else
            {
                var libraries = SteamLibraryHelper.GetSteamLibraries();
                if (libraries.Count == 0)
                {
                    AppendLog("ERROR: No Steam libraries detected and no library selected.");
                    return;
                }
                var ownerWindow = Window.GetWindow(this);
                var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libraries, _api);
                if (picked == null)
                {
                    AppendLog("Download cancelled — no library selected.");
                    return;
                }
                destPath = picked;
                _selectedLibraryPath = picked;
                _selectedLibLabel.Text = picked;
                _selectedLibLabel.Foreground = Brushes.White;
            }
            AppendLog("Installing to Steam library: " + destPath);

            if (!_downloader.IsReady)
            {
                AppendLog("ERROR: dotnet or DepotDownloader.dll not found. " +
                          "Install .NET 9 runtime and ensure it is on PATH.");
                return;
            }

            var selected = new List<string>();
            foreach (var child in _depotList.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is string id)
                    selected.Add(id);
            }

            if (selected.Count == 0)
            {
                AppendLog("No depots selected.");
                return;
            }

            _gameData.SelectedDepots = selected;
            _currentDownloadDir = _downloader.ComputeDownloadDir(_gameData, destPath);

            var runSteamless  = _steamlessCheck.IsChecked == true;
            var registerSteam = _registerSteamCheck.IsChecked == true;

            _lastDestPath = destPath;
            _postSteamlessBtn.Visibility = Visibility.Collapsed;
            _postRegisterBtn.Visibility = Visibility.Collapsed;

            SetBusy(true, "Allocating disk space — this may take a while for large games...");
            _progressBar.IsIndeterminate = true;
            _progressBar.Value = 0;
            _speedLabel.Text = "";
            _speedLabel.Visibility = Visibility.Visible;
            _deleteBtn.IsEnabled = true;
            _downloadStartTime = DateTime.UtcNow;
            _etaLabel.Visibility = Visibility.Visible;
            StartSpeedMonitor();

            _workerThread = new Thread(() =>
            {
                try
                {
                    var username = _settings.SteamUsername;
                    if (!string.IsNullOrWhiteSpace(username))
                        AppendLog("Using Steam account: " + username);

                    _downloader.Run(_gameData, destPath,
                        onLog:         line   => AppendLog(line),
                        onProgress:    pct    => Dispatch(() =>
                        {
                            if (_progressBar.IsIndeterminate)
                            {
                                _progressBar.IsIndeterminate = false;
                                _statusLabel.Text = "Downloading...";
                            }
                            _progressBar.Value = pct;
                        }),
                        maxDownloads:  _settings.MaxDownloads,
                        onStatus:      status => Dispatch(() => _statusLabel.Text = status),
                        steamUsername: string.IsNullOrWhiteSpace(username) ? null : username);

                    if (runSteamless)
                    {
                        AppendLog("--- Running Steamless ---");
                        var installDir = Path.Combine(destPath, "steamapps", "common",
                            AcfWriter.GetInstallFolderName(_gameData));
                        new SteamlessRunner(_downloader.DotnetPath).Run(installDir, line => AppendLog(line));
                    }

                    if (registerSteam)
                    {
                        AppendLog("--- Writing Steam ACF ---");
                        AcfWriter.Write(_gameData, destPath, line => AppendLog(line));
                    }

                    if (_installedGamesManager != null)
                    {
                        try
                        {
                            var installDir = AcfWriter.GetInstallFolderName(_gameData);
                            var installPath = Path.Combine(destPath, "steamapps", "common", installDir);

                            long sizeOnDisk = 0;
                            if (Directory.Exists(installPath))
                            {
                                try { sizeOnDisk = GetDirectorySize(installPath); }
                                catch { /* best effort */ }
                            }

                            var installedEntry = new InstalledGame
                            {
                                AppId            = _gameData.AppId,
                                GameName         = _gameData.GameName,
                                InstallPath      = installPath,
                                LibraryPath      = destPath,
                                InstallDir       = installDir,
                                InstalledDate    = DateTime.UtcNow,
                                SizeOnDisk       = sizeOnDisk,
                                SelectedDepots   = new List<string>(_gameData.SelectedDepots),
                                ManifestGIDs     = _gameData.Manifests
                                    .Where(kv => _gameData.SelectedDepots.Contains(kv.Key))
                                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                                PlayniteGameId   = _game?.Id ?? Guid.Empty,
                                DrmStripped      = runSteamless,
                                RegisteredWithSteam = registerSteam
                            };
                            _installedGamesManager.Save(installedEntry);
                            AppendLog("Game saved to installed library.");

                            // Register with Playnite when installed from library mode (no game context)
                            if (_game == null && _api != null)
                            {
                                try
                                {
                                    var newGame = new Game(_gameData.GameName)
                                    {
                                        GameId           = _gameData.AppId,
                                        PluginId         = SteamPluginGuid,
                                        InstallDirectory = installedEntry.InstallPath,
                                        IsInstalled      = true
                                    };

                                    Dispatch(() => _api.Database.Games.Add(newGame));
                                    AppendLog("Added to Playnite library.");

                                    var igdb = new IgdbClient(_settings.IgdbClientId, _settings.IgdbClientSecret);

                                    if (igdb.HasCredentials)
                                    {
                                        bool wantsMetadata = false;
                                        Dispatch(() =>
                                        {
                                            wantsMetadata = MessageBox.Show(
                                                "\"" + _gameData.GameName + "\" was added to your Playnite library.\n\n" +
                                                "Search IGDB for metadata (description, genres, cover art)?",
                                                "Download Metadata",
                                                MessageBoxButton.YesNo,
                                                MessageBoxImage.Question) == MessageBoxResult.Yes;
                                        });

                                        if (wantsMetadata)
                                        {
                                            IgdbGameResult igdbSelection = null;
                                            Dispatch(() =>
                                            {
                                                igdbSelection = IgdbMetadataPickerDialog.ShowPicker(
                                                    Window.GetWindow(this), _gameData.GameName, igdb, _api);
                                            });

                                            if (igdbSelection != null)
                                            {
                                                AppendLog("Fetching metadata for: " + igdbSelection.Name + "...");
                                                var metadata = igdb.GetMetadata(igdbSelection.Id);
                                                if (metadata != null)
                                                {
                                                    Dispatch(() => ApplyIgdbMetadata(newGame, metadata, igdb));
                                                    AppendLog("Metadata applied.");
                                                }
                                            }
                                            else
                                            {
                                                AppendLog("Metadata search cancelled.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // No IGDB credentials — silently grab Steam CDN cover
                                        var coverPath = CoverDownloader.Download(_gameData.GameName, _gameData.AppId, null);
                                        if (coverPath != null)
                                        {
                                            Dispatch(() =>
                                            {
                                                try
                                                {
                                                    var fileId = _api.Database.AddFile(coverPath, newGame.Id);
                                                    newGame.CoverImage = fileId;
                                                    _api.Database.Games.Update(newGame);
                                                }
                                                catch (Exception exCover)
                                                {
                                                    logger.Warn("Could not store Steam CDN cover: " + exCover.Message);
                                                }
                                            });
                                            try { File.Delete(coverPath); } catch { }
                                            AppendLog("Cover art stored from Steam CDN.");
                                        }
                                    }

                                    installedEntry.PlayniteGameId = newGame.Id;
                                    _installedGamesManager.Save(installedEntry);
                                }
                                catch (Exception exLib)
                                {
                                    AppendLog("WARNING: Could not add to Playnite library: " + exLib.Message);
                                }
                            }
                        }
                        catch (Exception exSave)
                        {
                            AppendLog("WARNING: Could not save to installed games: " + exSave.Message);
                        }
                    }

                    AppendLog("=== All done! ===");
                    var elapsed = DateTime.UtcNow - _downloadStartTime;
                    AppendLog("Download completed in " + FormatElapsedTime(elapsed));

                    AppendLog("--- Checking for Steam DRM ---");
                    var checkDir = Path.Combine(destPath, "steamapps", "common",
                        AcfWriter.GetInstallFolderName(_gameData));
                    if (Directory.Exists(checkDir))
                    {
                        var drmResult = SteamDrmChecker.Check(checkDir);
                        if (drmResult.HasDrm)
                        {
                            AppendLog("Steam DRM detected in " + drmResult.DrmProtectedFiles.Count + " file(s). Use 'Run Steamless' to remove.");
                            if (drmResult.NoDrmFiles.Count > 0)
                                AppendLog(drmResult.NoDrmFiles.Count + " file(s) without DRM — Goldberg emulator recommended.");
                        }
                        else
                        {
                            AppendLog("No Steam DRM detected. Goldberg Steam emulator is needed to run without Steam.");
                        }
                    }

                    Dispatch(() =>
                    {
                        _progressBar.Value = 100;
                        _speedLabel.Text = "";
                        _speedLabel.Visibility = Visibility.Collapsed;
                        _etaLabel.Text = "";
                        _etaLabel.Visibility = Visibility.Collapsed;
                        _postSteamlessBtn.Visibility = Visibility.Visible;
                        _postRegisterBtn.Visibility = Visibility.Visible;
                    });
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR: " + ex.Message);
                    logger.Error("Download failed: " + ex);
                }
                finally
                {
                    Dispatch(() => SetBusy(false, ""));
                }
            });
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            _downloader.Stop();
            AppendLog("Stop requested.");
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDownloadDir) || !Directory.Exists(_currentDownloadDir))
            {
                AppendLog("No download folder found to delete.");
                return;
            }

            var result = MessageBox.Show(
                "Delete the following folder?\n\n" + _currentDownloadDir,
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            if (_downloading)
            {
                _downloader.Stop();
                _workerThread?.Join(3000);
            }

            try
            {
                Directory.Delete(_currentDownloadDir, true);
                AppendLog("Deleted: " + _currentDownloadDir);
                _currentDownloadDir = null;
                Dispatch(() =>
                {
                    _deleteBtn.IsEnabled = false;
                    _progressBar.Value = 0;
                    _speedLabel.Text = "";
                    _speedLabel.Visibility = Visibility.Collapsed;
                    _etaLabel.Text = "";
                    _etaLabel.Visibility = Visibility.Collapsed;
                    _postSteamlessBtn.Visibility = Visibility.Collapsed;
                    _postRegisterBtn.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                AppendLog("ERROR deleting folder: " + ex.Message);
                logger.Error("Delete failed: " + ex);
            }
        }

        private void OnPostSteamlessClicked(object sender, RoutedEventArgs e)
        {
            if (_gameData == null || string.IsNullOrEmpty(_lastDestPath)) return;
            var installDir = Path.Combine(_lastDestPath, "steamapps", "common",
                AcfWriter.GetInstallFolderName(_gameData));
            _postSteamlessBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AppendLog("--- Running Steamless ---");
                    new SteamlessRunner(_downloader.DotnetPath).Run(installDir, line => AppendLog(line));
                    AppendLog("Steamless finished.");
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR running Steamless: " + ex.Message);
                }
                finally
                {
                    Dispatch(() => _postSteamlessBtn.IsEnabled = true);
                }
            });
        }

        private void OnPostRegisterClicked(object sender, RoutedEventArgs e)
        {
            if (_gameData == null || string.IsNullOrEmpty(_lastDestPath)) return;
            _postRegisterBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AppendLog("--- Writing Steam ACF ---");
                    AcfWriter.Write(_gameData, _lastDestPath, line => AppendLog(line));
                    AppendLog("ACF manifest written.");
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR writing ACF: " + ex.Message);
                }
                finally
                {
                    Dispatch(() => _postRegisterBtn.IsEnabled = true);
                }
            });
        }

        // ── Installed game detection ──────────────────────────────────────────────

        private void CheckIfInstalled()
        {
            _installedGame = _installedGamesManager.FindByPlayniteId(_game.Id);

            if (_installedGame == null && _game.PluginId == SteamPluginGuid)
                _installedGame = _installedGamesManager.FindByAppId(_game.GameId);

            if (_installedGame != null)
            {
                Dispatch(() =>
                {
                    _resolveStatusLabel.Visibility = Visibility.Collapsed;
                    _foundPanel.Visibility = Visibility.Collapsed;
                    _searchPanel.Visibility = Visibility.Collapsed;
                    _installedPanel.Visibility = Visibility.Visible;
                    _downloadBtn.IsEnabled = false;
                    _downloadBtn.Content = "Already Installed";
                    _resolvedAppId = _installedGame.AppId;
                });
            }
        }

        private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
        {
            if (_installedGame != null && Directory.Exists(_installedGame.InstallPath))
                System.Diagnostics.Process.Start("explorer.exe", _installedGame.InstallPath);
            else
                AppendLog("Install folder not found.");
        }

        private void OnReinstallClicked(object sender, RoutedEventArgs e)
        {
            _installedGame = null;
            _installedPanel.Visibility = Visibility.Collapsed;
            _downloadBtn.Content = "Download Selected Depots";
            _downloadBtn.IsEnabled = false;
            _resolveStatusLabel.Visibility = _game != null ? Visibility.Visible : Visibility.Collapsed;

            if (_game != null)
                ThreadPool.QueueUserWorkItem(_ => ResolveAppId());
        }

        private void OnUninstallClicked(object sender, RoutedEventArgs e)
        {
            if (_installedGame == null) return;

            var result = MessageBox.Show(
                string.Format("Uninstall \"{0}\"?\n\nThis will delete:\n{1}\n\nSave files in Documents/My Games and AppData will be preserved.",
                    _installedGame.GameName, _installedGame.InstallPath),
                "Uninstall Game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                PreserveSaveFiles(_installedGame);

                if (Directory.Exists(_installedGame.InstallPath))
                    Directory.Delete(_installedGame.InstallPath, true);

                if (!string.IsNullOrEmpty(_installedGame.LibraryPath))
                {
                    var acfPath = Path.Combine(_installedGame.LibraryPath, "steamapps",
                        "appmanifest_" + _installedGame.AppId + ".acf");
                    if (File.Exists(acfPath))
                        File.Delete(acfPath);
                }

                _installedGamesManager.Remove(_installedGame.AppId);
                AppendLog("Uninstalled: " + _installedGame.GameName);

                if (_api != null && _installedGame.PlayniteGameId != Guid.Empty)
                {
                    var removeFromPlaynite = _api.Dialogs.ShowMessage(
                        "Remove \"" + _installedGame.GameName + "\" from Playnite library as well?",
                        "Remove from Library",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (removeFromPlaynite == MessageBoxResult.Yes)
                        _api.Database.Games.Remove(_installedGame.PlayniteGameId);
                }

                _installedGame = null;
                _installedPanel.Visibility = Visibility.Collapsed;
                _downloadBtn.Content = "Download Selected Depots";
                _downloadBtn.IsEnabled = false;
                _resolveStatusLabel.Visibility = _game != null ? Visibility.Visible : Visibility.Collapsed;

                if (_game != null)
                    ThreadPool.QueueUserWorkItem(_ => ResolveAppId());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during uninstall: " + ex.Message, "Uninstall Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Steam authentication ──────────────────────────────────────────────────

        private void OnAuthenticateClicked(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                AppendLog("Enter your Steam username in the field first.");
                return;
            }

            var lightFg = Brushes.WhiteSmoke;

            var pwdWindow = _api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });
            pwdWindow.Owner = _api.Dialogs.GetCurrentAppWindow();
            pwdWindow.Title = "Steam Password";
            pwdWindow.Width = 360;
            pwdWindow.Height = 140;
            pwdWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var stack = new StackPanel { Margin = new Thickness(16) };

            stack.Children.Add(new TextBlock
            {
                Text = "Password for " + username + ":",
                Foreground = lightFg,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var pwdBox = new PasswordBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = lightFg,
                CaretBrush = lightFg
            };

            var okBtn = new Button
            {
                Content = "Open Auth Window",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(12, 4, 12, 4)
            };
            okBtn.Click += (s, e) => pwdWindow.DialogResult = true;
            stack.Children.Add(pwdBox);
            stack.Children.Add(okBtn);
            pwdWindow.Content = stack;
            pwdBox.Focus();

            if (pwdWindow.ShowDialog() != true) return;

            var password = pwdBox.Password;
            if (string.IsNullOrEmpty(password)) return;

            AppendLog("--- Steam Authentication ---");
            AppendLog("A console window will open. Complete any Steam Guard prompt there, then close it.");

            ThreadPool.QueueUserWorkItem(_ =>
                _downloader.Authenticate(username, password, line => AppendLog(line)));
        }

        // ── IGDB metadata ─────────────────────────────────────────────────────────

        private void ApplyIgdbMetadata(Game game, IgdbMetadata metadata, IgdbClient igdb)
        {
            if (!string.IsNullOrEmpty(metadata.Name))
                game.Name = metadata.Name;

            if (!string.IsNullOrEmpty(metadata.Summary))
                game.Description = metadata.Summary;

            if (metadata.Genres.Count > 0)
                game.GenreIds = metadata.Genres.ConvertAll(g => _api.Database.Genres.Add(g).Id);

            if (metadata.Developers.Count > 0)
                game.DeveloperIds = metadata.Developers.ConvertAll(d => _api.Database.Companies.Add(d).Id);

            if (metadata.Publishers.Count > 0)
                game.PublisherIds = metadata.Publishers.ConvertAll(p => _api.Database.Companies.Add(p).Id);

            if (metadata.Tags.Count > 0)
                game.TagIds = metadata.Tags.ConvertAll(t => _api.Database.Tags.Add(t).Id);

            if (metadata.ReleaseYear.HasValue)
                game.ReleaseDate = new ReleaseDate(metadata.ReleaseYear.Value);

            if (!string.IsNullOrEmpty(metadata.CoverImageId))
            {
                try
                {
                    var coverPath = igdb.DownloadCoverByImageId(metadata.CoverImageId);
                    if (coverPath != null)
                    {
                        var fileId = _api.Database.AddFile(coverPath, game.Id);
                        game.CoverImage = fileId;
                        try { File.Delete(coverPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("Could not store IGDB cover: " + ex.Message);
                }
            }

            if (metadata.Artworks.Count > 0)
            {
                try
                {
                    var selectedArtId = IgdbBackgroundPickerDialog.ShowPicker(
                        Window.GetWindow(this), metadata.Artworks, igdb, _api);

                    if (!string.IsNullOrEmpty(selectedArtId))
                    {
                        var bgPath = igdb.DownloadArtworkByImageId(selectedArtId);
                        if (bgPath != null)
                        {
                            var fileId = _api.Database.AddFile(bgPath, game.Id);
                            game.BackgroundImage = fileId;
                            try { File.Delete(bgPath); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("Could not store IGDB background: " + ex.Message);
                }
            }

            _api.Database.Games.Update(game);
        }

        // ── Load from ZIP ─────────────────────────────────────────────────────────

        private void OnLoadFromZipClicked(Button btn)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Manifest ZIP",
                Filter = "ZIP files (*.zip)|*.zip",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            var zipPath = dialog.FileName;
            btn.IsEnabled = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var data = new ZipProcessor().Process(zipPath);
                    Dispatch(() =>
                    {
                        LoadFromZip(data);
                        btn.IsEnabled = true;
                    });
                }
                catch (Exception ex)
                {
                    Dispatch(() =>
                    {
                        AppendLog("ERROR loading ZIP: " + ex.Message);
                        btn.IsEnabled = true;
                    });
                }
            });
        }

        private void LoadFromZip(GameData data)
        {
            _gameData = data;
            _resolvedAppId = data.AppId;
            _gameInfoLabel.Text = data.GameName + "  (AppID: " + data.AppId + ")";
            _resolveStatusLabel.Visibility = Visibility.Collapsed;
            _foundPanel.Visibility = Visibility.Collapsed;
            _searchPanel.Visibility = Visibility.Collapsed;
            _installedPanel.Visibility = Visibility.Collapsed;
            _downloadBtn.Content = "Download Selected Depots";
            _downloadBtn.IsEnabled = false;
            PopulateDepots(data);
            AppendLog("Loaded from ZIP: " + data.GameName + " — " + data.Depots.Count + " depot(s) available.");
        }

        // ── Save file preservation ────────────────────────────────────────────────

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

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { /* skip inaccessible files */ }
            }
            return size;
        }

        // ── Speed monitor ─────────────────────────────────────────────────────────

        private void StartSpeedMonitor()
        {
            _lastBytesReceived = GetTotalBytesReceived();
            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _speedTimer.Tick += OnSpeedTick;
            _speedTimer.Start();
        }

        private void StopSpeedMonitor()
        {
            _speedTimer?.Stop();
            _speedTimer = null;
            _lastBytesReceived = -1;
            _speedSamples.Clear();
            _speedLabel.Text = "";
            _speedLabel.Visibility = Visibility.Collapsed;
            _etaLabel.Text = "";
            _etaLabel.Visibility = Visibility.Collapsed;
        }

        private void OnSpeedTick(object sender, EventArgs e)
        {
            var current = GetTotalBytesReceived();
            if (_lastBytesReceived >= 0)
            {
                _speedSamples.Enqueue(current - _lastBytesReceived);
                while (_speedSamples.Count > SpeedSampleWindow)
                    _speedSamples.Dequeue();

                long sum = 0;
                foreach (var s in _speedSamples) sum += s;
                _speedLabel.Text = FormatSpeed(sum / _speedSamples.Count);

                if (_progressBar.Value > 1 && !_progressBar.IsIndeterminate)
                {
                    var elapsed = DateTime.UtcNow - _downloadStartTime;
                    var progress = _progressBar.Value;
                    var remaining = TimeSpan.FromTicks((long)(elapsed.Ticks * (100 - progress) / progress));
                    _etaLabel.Text = FormatTimeRemaining(remaining);
                }
            }
            _lastBytesReceived = current;
        }

        private static long GetTotalBytesReceived()
        {
            long total = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        total += ni.GetIPv4Statistics().BytesReceived;
                }
            }
            catch { }
            return total;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void SetBusy(bool busy, string status)
        {
            _downloading = busy;
            if (!busy) { _progressBar.IsIndeterminate = false; StopSpeedMonitor(); }
            var hasId = !string.IsNullOrWhiteSpace(_resolvedAppId);
            _fetchBtn.IsEnabled       = !busy && hasId;
            _fetchBtnSearch.IsEnabled = !busy && hasId;
            _downloadBtn.IsEnabled    = !busy && _gameData != null && _gameData.Depots.Count > 0;
            _stopBtn.IsEnabled        = busy;
            if (!string.IsNullOrEmpty(status))
                _statusLabel.Text = status;
        }

        private void AppendLog(string line)
        {
            if (Dispatcher.CheckAccess())
            {
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.ScrollToEnd();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _logBox.AppendText(line + Environment.NewLine);
                    _logBox.ScrollToEnd();
                }));
            }
        }

        private void Dispatch(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }

        private static int OsSortKey(string os)
        {
            if (os == null) return 3;
            switch (os.ToLowerInvariant())
            {
                case "windows": return 1;
                case "linux":   return 2;
                default:        return 3;
            }
        }

        private static string FormatSpeed(long bytesPerSecond)
        {
            if (bytesPerSecond < 0) return "";
            if (bytesPerSecond >= 1_048_576)
                return (bytesPerSecond / 1_048_576.0).ToString("F1") + " MB/s";
            if (bytesPerSecond >= 1024)
                return (bytesPerSecond / 1024.0).ToString("F1") + " KB/s";
            return bytesPerSecond + " B/s";
        }

        private static string FormatTimeRemaining(TimeSpan remaining)
        {
            if (remaining.TotalSeconds < 1) return "ETA: --";
            if (remaining.TotalHours >= 1)
                return string.Format("ETA: {0}h {1}m", (int)remaining.TotalHours, remaining.Minutes);
            if (remaining.TotalMinutes >= 1)
                return string.Format("ETA: {0}m {1}s", (int)remaining.TotalMinutes, remaining.Seconds);
            return string.Format("ETA: {0}s", (int)remaining.TotalSeconds);
        }

        private static string FormatElapsedTime(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return string.Format("{0}h {1}m {2}s", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            if (elapsed.TotalMinutes >= 1)
                return string.Format("{0}m {1}s", (int)elapsed.TotalMinutes, elapsed.Seconds);
            return string.Format("{0}s", (int)elapsed.TotalSeconds);
        }
    }
}
