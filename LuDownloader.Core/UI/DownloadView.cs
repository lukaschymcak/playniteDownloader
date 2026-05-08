using Microsoft.Win32;
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
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();
        private static readonly Guid SteamPluginGuid = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        // ── Services ─────────────────────────────────────────────────────────────
        private readonly AppSettings _settings;
        private readonly IDialogService _dialogService;
        private readonly IAppHost _appHost;
        private readonly MorrenusClient _client;
        private readonly DepotDownloaderRunner _downloader;

        // ── Initial game context (for AppId resolution) ──────────────────────────
        private readonly string _initialGameName;
        private readonly string _initialGameId;
        private readonly Guid _initialGamePluginId;
        private readonly Guid _initialPlayniteGameId;
        private readonly List<string> _initialGameLinkUrls;
        private readonly bool _hasInitialGame;

        // ── State ────────────────────────────────────────────────────────────────
        private GameData _gameData;
        private Thread _workerThread;
        private bool _downloading;
        private string _resolvedAppId;
        private string _currentDownloadDir;
        private readonly string _manifestCacheRoot;
        private int _manifestCacheApplyGen;
        private string _cacheReadFailedAppId;

        // ── Network speed monitor ─────────────────────────────────────────────────
        private DispatcherTimer _speedTimer;
        private long _lastBytesReceived = -1;
        private readonly Queue<long> _speedSamples = new Queue<long>();
        private const int SpeedSampleWindow = 5;
        private readonly Queue<(DateTime Time, int Progress)> _progressSamples
            = new Queue<(DateTime, int)>();
        private const int ProgressSampleWindow = 10;

        // ── ETA / elapsed tracking ───────────────────────────────────────────────
        private DateTime _downloadStartTime;
        private TextBlock _etaLabel;

        // ── Post-download actions ─────────────────────────────────────────────────
        private Button _postSteamlessBtn;
        private Button _postRegisterBtn;
        private Button _postMarkInstalledBtn;
        private string _lastDestPath;
        private string _lastManifestZipPath;
        private readonly SteamIntegrationService _steamIntegration;

        // ── Installed game tracking ───────────────────────────────────────────────
        private readonly InstalledGamesManager _installedGamesManager;
        private InstalledGame _installedGame;
        private readonly UpdateChecker _updateChecker;
        private string _headerImageUrl;

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
        private TextBlock _cachedManifestLabel;

        // ── UI: Depots ────────────────────────────────────────────────────────────
        private ScrollViewer _depotScroll;
        private StackPanel _depotList;

        // ── UI: Options ───────────────────────────────────────────────────────────
        private string _selectedLibraryPath;
        private TextBlock _selectedLibLabel;
        private Button _goldbergBtn;

        // ── UI: Progress / log ────────────────────────────────────────────────────
        private Button _downloadBtn;
        private Button _stopBtn;
        private Button _deleteBtn;
        private ProgressBar _progressBar;
        private TextBlock _speedLabel;
        private TextBox _logBox;

        /// <summary>
        /// Constructor used when launching with a known game context (e.g. a host library entry).
        /// </summary>
        /// <param name="gameName">Display name of the game; used for resolution and search defaults.</param>
        /// <param name="gameId">Host-supplied identifier. When <paramref name="pluginId"/> equals the Steam plugin GUID, this is treated as a Steam AppId.</param>
        /// <param name="pluginId">Host plugin GUID; pass <see cref="Guid.Empty"/> when not applicable.</param>
        /// <param name="playniteGameId">Optional host library entry GUID for cross-references; pass <see cref="Guid.Empty"/> when none.</param>
        /// <param name="gameLinkUrls">Optional URLs to scan for a Steam store link (used to derive AppId when not Steam-plugin).</param>
        /// <param name="initialData">Pre-loaded manifest data (from "Install from ZIP"). When provided, depots are populated immediately.</param>
        public DownloadView(
            string gameName,
            string gameId,
            Guid pluginId,
            Guid playniteGameId,
            IEnumerable<string> gameLinkUrls,
            AppSettings settings,
            InstalledGamesManager installedGamesManager,
            IDialogService dialogService,
            IAppHost appHost,
            UpdateChecker updateChecker,
            string manifestCacheRoot,
            GameData initialData = null)
        {
            _initialGameName = gameName;
            _initialGameId = gameId;
            _initialGamePluginId = pluginId;
            _initialPlayniteGameId = playniteGameId;
            _initialGameLinkUrls = gameLinkUrls != null ? new List<string>(gameLinkUrls) : new List<string>();
            _hasInitialGame = !string.IsNullOrWhiteSpace(gameName) || !string.IsNullOrWhiteSpace(gameId);

            _settings = settings;
            _dialogService = dialogService;
            _appHost = appHost;
            _manifestCacheRoot = manifestCacheRoot;
            _client = new MorrenusClient(() => _settings.ApiKey);
            _downloader = new DepotDownloaderRunner();
            _steamIntegration = new SteamIntegrationService();
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
            else if (_hasInitialGame)
            {
                _gameInfoLabel.Text = _initialGameName ?? string.Empty;

                if (_installedGamesManager != null)
                    CheckIfInstalled();

                if (_installedGame == null)
                    ThreadPool.QueueUserWorkItem(_ => ResolveAppId());
            }
        }

        /// <summary>
        /// Convenience constructor for launching without any game context (e.g. Install-from-ZIP entry).
        /// </summary>
        public DownloadView(
            AppSettings settings,
            InstalledGamesManager installedGamesManager,
            IDialogService dialogService,
            IAppHost appHost,
            UpdateChecker updateChecker,
            string manifestCacheRoot,
            GameData initialData = null)
            : this(null, null, Guid.Empty, Guid.Empty, null,
                   settings, installedGamesManager, dialogService, appHost, updateChecker, manifestCacheRoot, initialData)
        {
        }

        /// <summary>
        /// Constructor used when launching from a Steam search result.
        /// Skips game resolution and goes directly to the "found" state with the given appId.
        /// </summary>
        public DownloadView(
            string appId,
            string name,
            AppSettings settings,
            InstalledGamesManager installedGamesManager,
            IDialogService dialogService,
            IAppHost appHost,
            UpdateChecker updateChecker,
            string manifestCacheRoot,
            string headerImageUrl = null)
        {
            _hasInitialGame = false;
            _initialGameLinkUrls = new List<string>();
            _settings = settings;
            _dialogService = dialogService;
            _appHost = appHost;
            _manifestCacheRoot = manifestCacheRoot;
            _client   = new MorrenusClient(() => _settings.ApiKey);
            _downloader = new DepotDownloaderRunner();
            _steamIntegration = new SteamIntegrationService();
            _installedGamesManager = installedGamesManager;
            _updateChecker = updateChecker;
            _headerImageUrl = headerImageUrl;

            Content = BuildLayout();

            ThreadPool.QueueUserWorkItem(_ => RefreshApiStatus());

            // Skip resolution — go directly to "found" state
            _resolvedAppId = appId;
            _gameInfoLabel.Text = name;
            ShowFoundPanel(appId, "\u2713 " + name + " (" + appId + ")");
            ApplyCachedManifestIfPresent();
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
                Visibility = _hasInitialGame ? Visibility.Visible : Visibility.Collapsed
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

            _cachedManifestLabel = new TextBlock
            {
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.LightSteelBlue,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12
            };
            panel.Children.Add(_cachedManifestLabel);

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
                Text = _hasInitialGame ? (_initialGameName ?? "") : ""
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
                var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libs, _dialogService);
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
            };

            authBtn.Click += (s, e) => OnAuthenticateClicked(usernameBox.Text.Trim());

            steamRow.Children.Add(steamLabel);
            steamRow.Children.Add(authBtn);
            steamRow.Children.Add(usernameBox);
            panel.Children.Add(steamRow);

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
                Visibility = Visibility.Visible,
                ToolTip = "Strip Steam DRM from downloaded executables"
            };
            _postSteamlessBtn.Click += OnPostSteamlessClicked;
            DockPanel.SetDock(_postSteamlessBtn, Dock.Left);

            _postRegisterBtn = new Button
            {
                Content = "Add to Steam",
                Padding = new Thickness(12, 6, 12, 6),
                Visibility = Visibility.Visible,
                ToolTip = "Copy Morrenus .lua file to Steam\\config\\lua and restart Steam"
            };
            _postRegisterBtn.Click += OnPostRegisterClicked;
            DockPanel.SetDock(_postRegisterBtn, Dock.Left);

            _postMarkInstalledBtn = new Button
            {
                Content = "Mark Installed in Steam",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Visible,
                ToolTip = "Write appmanifest .acf to show Installed in Steam"
            };
            _postMarkInstalledBtn.Click += OnPostMarkInstalledClicked;
            DockPanel.SetDock(_postMarkInstalledBtn, Dock.Left);

            _goldbergBtn = new Button
            {
                Content = "Apply Goldberg",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Apply Goldberg Steam emulator to the downloaded game"
            };
            _goldbergBtn.Click += OnGoldbergClicked;
            DockPanel.SetDock(_goldbergBtn, Dock.Left);

            postRow.Children.Add(_postSteamlessBtn);
            postRow.Children.Add(_postRegisterBtn);
            postRow.Children.Add(_postMarkInstalledBtn);
            postRow.Children.Add(_goldbergBtn);
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
            if (_initialGamePluginId == SteamPluginGuid && !string.IsNullOrWhiteSpace(_initialGameId))
            {
                SetConfident(_initialGameId, "✓  " + (_initialGameName ?? string.Empty) + "  (" + _initialGameId + ")");
                return;
            }

            // Step 1b: Steam store URL in game links
            var steamId = TryExtractSteamIdFromLinks();
            if (steamId != null)
            {
                SetConfident(steamId, "✓  " + (_initialGameName ?? string.Empty) + "  (" + steamId + ")");
                return;
            }

            // Step 2: search by name
            try
            {
                var results = _client.SearchGames(_initialGameName ?? string.Empty);
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
            ApplyCachedManifestIfPresent();
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
                ApplyCachedManifestIfPresent();
            }
        }

        private string TryExtractSteamIdFromLinks()
        {
            if (_initialGameLinkUrls == null) return null;
            foreach (var url in _initialGameLinkUrls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                var m = Regex.Match(url, @"store\.steampowered\.com/app/(\d+)");
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
            _cachedManifestLabel.Visibility = Visibility.Collapsed;

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
                        ApplyCachedManifestIfPresent();
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
                ApplyCachedManifestIfPresent();
            }
        }

        /// <summary>
        /// When a saved Morrenus ZIP exists, show it and disable Fetch (greyed); still load depots from disk so reinstall works without the API.
        /// </summary>
        private void ApplyCachedManifestIfPresent()
        {
            if (string.IsNullOrEmpty(_manifestCacheRoot) || string.IsNullOrWhiteSpace(_resolvedAppId))
                return;

            var appId = _resolvedAppId.Trim();
            _cacheReadFailedAppId = null;
            var root = _manifestCacheRoot;
            int gen = Interlocked.Increment(ref _manifestCacheApplyGen);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!ManifestCache.TryGetCachedZipPath(root, appId, out var zipPath))
                {
                    Dispatch(() =>
                    {
                        if (gen != _manifestCacheApplyGen || _resolvedAppId != appId) return;
                        _cachedManifestLabel.Visibility = Visibility.Collapsed;
                        RefreshFetchButtonsForCacheAndBusy();
                    });
                    return;
                }

                Dispatch(() =>
                {
                    if (gen != _manifestCacheApplyGen || _resolvedAppId != appId) return;
                    _cachedManifestLabel.Visibility = Visibility.Visible;
                    _cachedManifestLabel.Text = _installedGame != null
                        ? "Saved manifest on disk. Use Fetch from Morrenus to refresh the depot list."
                        : "Saved manifest on disk for this game. Fetch from Morrenus is disabled.";
                    if (_installedGame == null)
                    {
                        _fetchBtn.IsEnabled = false;
                        _fetchBtnSearch.IsEnabled = false;
                    }
                    else
                        RefreshFetchButtonsForCacheAndBusy();
                });

                try
                {
                    var data = new ZipProcessor().Process(zipPath);
                    Dispatch(() =>
                    {
                        if (gen != _manifestCacheApplyGen || _resolvedAppId != appId) return;
                        _gameData = data;
                        _lastManifestZipPath = zipPath;
                        _gameInfoLabel.Text = data.GameName + "  (AppID: " + data.AppId + ")";
                        PopulateDepots(data, GetInstalledDepotIdsForPopulate());
                        _downloadBtn.IsEnabled = !_downloading && _gameData != null && _gameData.Depots.Count > 0;
                    });
                    AppendLog("Loaded saved manifest from disk.");
                }
                catch (Exception ex)
                {
                    logger.Error("Cached manifest load failed: " + ex);
                    _cacheReadFailedAppId = appId;
                    try { ManifestCache.DeleteCached(root, appId); } catch { }
                    Dispatch(() =>
                    {
                        if (gen != _manifestCacheApplyGen || _resolvedAppId != appId) return;
                        _cachedManifestLabel.Visibility = Visibility.Visible;
                        _cachedManifestLabel.Text =
                            "Saved manifest file was invalid/locked and has been removed. Fetch from Morrenus is enabled. Details: " + ex.Message;
                        RefreshFetchButtonsForCacheAndBusy();
                    });
                }
            });
        }

        private void RefreshFetchButtonsForCacheAndBusy()
        {
            var hasId = !string.IsNullOrWhiteSpace(_resolvedAppId);
            var blocked = CacheBlocksMorrenusFetch();
            _fetchBtn.IsEnabled = !_downloading && hasId && !blocked;
            _fetchBtnSearch.IsEnabled = !_downloading && hasId && !blocked;
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
                    string zipPath;
                    if (!string.IsNullOrEmpty(_manifestCacheRoot) &&
                        ManifestCache.SanitizeAppIdForFileName(appId) != null)
                    {
                        var partPath = ManifestCache.GetPartPath(_manifestCacheRoot, appId);
                        ManifestCache.TryDeletePartFile(partPath);
                        _client.DownloadManifest(appId,
                            new Progress<int>(pct => Dispatch(() => _progressBar.Value = pct)),
                            partPath);
                        ManifestCache.CommitPartToZip(_manifestCacheRoot, appId);
                        zipPath = ManifestCache.GetCachedZipPath(_manifestCacheRoot, appId);
                    }
                    else
                    {
                        zipPath = _client.DownloadManifest(appId,
                            new Progress<int>(pct => Dispatch(() => _progressBar.Value = pct)));
                    }

                    AppendLog("Processing ZIP...");
                    var data = new ZipProcessor().Process(zipPath);
                    _gameData = data;
                    _lastManifestZipPath = zipPath;
                    if (!string.IsNullOrEmpty(_manifestCacheRoot))
                        ManifestCache.WriteMeta(_manifestCacheRoot, data);

                    Dispatch(() =>
                    {
                        PopulateDepots(data, GetInstalledDepotIdsForPopulate());
                        _cachedManifestLabel.Visibility = Visibility.Visible;
                        _cachedManifestLabel.Text = _installedGame != null
                            ? "Saved manifest on disk. Use Fetch from Morrenus to refresh the depot list."
                            : "Saved manifest on disk for this game. Fetch from Morrenus is disabled.";
                        if (_installedGame == null)
                        {
                            _fetchBtn.IsEnabled = false;
                            _fetchBtnSearch.IsEnabled = false;
                        }
                        else
                            RefreshFetchButtonsForCacheAndBusy();
                        _downloadBtn.IsEnabled = !_downloading && _gameData != null && _gameData.Depots.Count > 0;
                    });
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

        /// <summary>
        /// Builds the depot checklist. For a plugin-installed game, depot ids in <paramref name="installedDepotIds"/>
        /// render as read-only rows so they are not offered as download targets; other depots get checkboxes.
        /// Fresh-install flow: all depots are checkboxes; Windows-tagged depots checked by default.
        /// </summary>
        /// <param name="installedDepotIds">
        /// When <see cref="_installedGame"/> is set and this is non-empty, matching depots show as installed (non-CheckBox).
        /// Ignored when not in add-on context. Callers use <see cref="GetInstalledDepotIdsForPopulate"/>.
        /// </param>
        private void PopulateDepots(GameData data, ICollection<string> installedDepotIds = null)
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

            HashSet<string> installedSet = null;
            if (_installedGame != null && installedDepotIds != null && installedDepotIds.Count > 0)
                installedSet = new HashSet<string>(installedDepotIds, StringComparer.OrdinalIgnoreCase);

            if (_installedGame != null)
            {
                _depotList.Children.Add(new TextBlock
                {
                    Text = "Depots already in your install are listed below as read-only. Check additional depots to download them; only new selections are fetched.",
                    Foreground = Brushes.LightGray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 4, 4, 8)
                });
            }

            var sorted = data.Depots
                .OrderBy(kv => OsSortKey(kv.Value.OsList))
                .ThenBy(kv => kv.Value.Description)
                .ToList();

            foreach (var kv in sorted)
            {
                var depotId = kv.Key;
                var info = kv.Value;
                var windowsDefault = info.OsList == null
                    || info.OsList.Equals("windows", StringComparison.OrdinalIgnoreCase);
                var sizeText = info.Size > 0 ? "  [" + SteamLibraryHelper.FormatSize(info.Size) + "]" : "";
                var line = string.Format("[{0}] {1}{2}  (id: {3})",
                    (info.OsList ?? "?").ToUpper(), info.Description, sizeText, depotId);

                // Read-only row: not a CheckBox so OnDownloadClicked never treats it as a download target.
                if (installedSet != null && installedSet.Contains(depotId))
                {
                    _depotList.Children.Add(new TextBlock
                    {
                        Text = "[INSTALLED]  " + line,
                        Foreground = Brushes.LightSteelBlue,
                        Margin = new Thickness(4, 2, 4, 2),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    continue;
                }

                var cb = new CheckBox
                {
                    Tag = depotId,
                    Margin = new Thickness(4, 2, 4, 2),
                    IsChecked = windowsDefault
                };
                cb.Content = line;
                _depotList.Children.Add(cb);
            }

            _downloadBtn.IsEnabled = true;
        }

        // ── Download ─────────────────────────────────────────────────────────────

        private void OnDownloadClicked(object sender, RoutedEventArgs e)
        {
            if (_gameData == null || _downloading) return;

            var priorInstalled = _installedGame;

            string destPath = null;
            if (_selectedLibraryPath != null)
                destPath = _selectedLibraryPath;
            else if (priorInstalled != null && !string.IsNullOrWhiteSpace(priorInstalled.LibraryPath))
            {
                var lib = priorInstalled.LibraryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(lib))
                {
                    destPath = lib;
                    _selectedLibraryPath = destPath;
                    _selectedLibLabel.Text = destPath;
                    _selectedLibLabel.Foreground = Brushes.White;
                }
            }

            if (destPath == null)
            {
                var libraries = SteamLibraryHelper.GetSteamLibraries();
                if (libraries.Count == 0)
                {
                    AppendLog("ERROR: No Steam libraries detected and no library selected.");
                    return;
                }
                var ownerWindow = Window.GetWindow(this);
                var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libraries, _dialogService);
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

            List<string> depotsForRunner;
            if (priorInstalled != null)
            {
                var prev = new HashSet<string>(
                    priorInstalled.SelectedDepots ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var alreadyInstalledSelected = selected.Where(id => prev.Contains(id)).ToList();
                if (alreadyInstalledSelected.Count > 0)
                    logger.Info("DownloadView: checkboxes included depot ids already on install (unexpected): "
                        + string.Join(", ", alreadyInstalledSelected));
                depotsForRunner = selected.Where(id => !prev.Contains(id)).ToList();
                if (depotsForRunner.Count == 0)
                {
                    AppendLog("No new depots to download — all selected depots are already in this install. Check additional depots to add content.");
                    return;
                }
                AppendLog("Add-on: downloading new depot(s): " + string.Join(", ", depotsForRunner));
            }
            else
            {
                depotsForRunner = selected;
                _gameData.SelectedDepots = selected;
            }

            _currentDownloadDir = _downloader.ComputeDownloadDir(_gameData, destPath);

            _lastDestPath = destPath;
            _goldbergBtn.Visibility = Visibility.Collapsed;

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

                    var runData = CloneGameDataWithDepots(_gameData, depotsForRunner);
                    _downloader.Run(runData, destPath,
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

                    if (_installedGamesManager != null)
                    {
                        try
                        {
                            if (priorInstalled != null)
                            {
                                var mergedList = MergeDepotIdLists(priorInstalled.SelectedDepots, selected);
                                _gameData.SelectedDepots = new List<string>(mergedList);

                                var installDir = AcfWriter.GetInstallFolderName(_gameData);
                                var installPath = Path.Combine(destPath, "steamapps", "common", installDir);

                                long sizeOnDisk = 0;
                                if (Directory.Exists(installPath))
                                {
                                    try { sizeOnDisk = GetDirectorySize(installPath); }
                                    catch { /* best effort */ }
                                }

                                var manifestGids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var depotId in mergedList)
                                {
                                    if (_gameData.Manifests != null && _gameData.Manifests.TryGetValue(depotId, out var gid)
                                        && !string.IsNullOrEmpty(gid))
                                        manifestGids[depotId] = gid;
                                    else if (priorInstalled.ManifestGIDs != null
                                        && priorInstalled.ManifestGIDs.TryGetValue(depotId, out var oldGid))
                                        manifestGids[depotId] = oldGid;
                                }

                                var mergedEntry = new InstalledGame
                                {
                                    AppId                 = _gameData.AppId,
                                    GameName              = _gameData.GameName,
                                    InstallPath           = installPath,
                                    LibraryPath           = destPath,
                                    InstallDir            = installDir,
                                    InstalledDate         = priorInstalled.InstalledDate,
                                    SizeOnDisk            = sizeOnDisk,
                                    SelectedDepots        = mergedList,
                                    ManifestGIDs          = manifestGids,
                                    PlayniteGameId        = priorInstalled.PlayniteGameId != Guid.Empty
                                        ? priorInstalled.PlayniteGameId
                                        : _initialPlayniteGameId,
                                    DrmStripped           = priorInstalled.DrmStripped,
                                    RegisteredWithSteam   = priorInstalled.RegisteredWithSteam,
                                    GseSavesCopied        = priorInstalled.GseSavesCopied,
                                    HeaderImageUrl        = _headerImageUrl ?? priorInstalled.HeaderImageUrl,
                                    SteamBuildId          = string.IsNullOrWhiteSpace(_gameData.BuildId) ? priorInstalled.SteamBuildId : _gameData.BuildId,
                                    ManifestZipPath       = string.IsNullOrWhiteSpace(_lastManifestZipPath) ? priorInstalled.ManifestZipPath : _lastManifestZipPath
                                };
                                _installedGamesManager.Save(mergedEntry);
                                AppendLog("Install record updated (add-on depots merged).");
                                Dispatch(() =>
                                {
                                    _installedGame = mergedEntry;
                                    PopulateDepots(_gameData, GetInstalledDepotIdsForPopulate());
                                });
                                _updateChecker?.RunAsync();
                            }
                            else
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
                                    PlayniteGameId   = _initialPlayniteGameId,
                                    DrmStripped      = false,
                                    RegisteredWithSteam = false,
                                    HeaderImageUrl   = _headerImageUrl,
                                    SteamBuildId     = _gameData.BuildId,
                                    ManifestZipPath  = _lastManifestZipPath
                                };
                                _installedGamesManager.Save(installedEntry);
                                AppendLog("Game saved to installed library.");
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
                        _goldbergBtn.Visibility = Visibility.Visible;
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
                    _goldbergBtn.Visibility = Visibility.Collapsed;
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
            var installDir = ResolveInstallDirForActions();
            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            {
                AppendLog("Steamless: install folder not found. Download or install first.");
                return;
            }

            _postSteamlessBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AppendLog("--- Running Steamless ---");
                    new SteamlessRunner(_downloader.DotnetPath).Run(installDir, line => AppendLog(line));
                    AppendLog("Steamless finished.");

                    if (_installedGame != null && _installedGamesManager != null)
                    {
                        _installedGame.DrmStripped = true;
                        _installedGamesManager.Save(_installedGame);
                    }
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
            _postRegisterBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AppendLog("--- Add to Steam (.lua) ---");
                    var sourceLua = ResolveSingleLuaSourcePath();
                    if (string.IsNullOrEmpty(sourceLua) || !File.Exists(sourceLua))
                        throw new InvalidOperationException("No .lua file found in the current Morrenus manifest context.");

                    var steamPath = _steamIntegration.GetSteamPath();
                    var targetLua = _steamIntegration.CopyLuaToSteamConfig(sourceLua, steamPath, true);
                    AppendLog("Copied .lua to: " + targetLua);

                    var shouldMarkInstalled = false;
                    Dispatch(() =>
                    {
                        shouldMarkInstalled = _dialogService.ShowMessage(
                            "Lua copied.\n\nAlso mark this game as Installed in Steam (write appmanifest .acf)?",
                            "Add to Steam",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes;
                    });

                    if (shouldMarkInstalled)
                    {
                        var libraryRoot = ResolveLibraryRootForActions();
                        var data = BuildGameDataForActions();
                        if (string.IsNullOrWhiteSpace(libraryRoot) || data == null)
                            throw new InvalidOperationException("Cannot write ACF: missing game/library context.");

                        _steamIntegration.WriteAcfForInstall(data, libraryRoot, line => AppendLog(line));
                        AppendLog("ACF manifest written.");
                    }

                    if (_installedGame != null && _installedGamesManager != null)
                    {
                        _installedGame.RegisteredWithSteam = true;
                        _installedGamesManager.Save(_installedGame);
                    }

                    var shouldRestart = false;
                    Dispatch(() =>
                    {
                        shouldRestart = _dialogService.ShowMessage(
                            shouldMarkInstalled
                                ? "Lua copied and ACF written.\n\nRestart Steam now?"
                                : "Lua file copied to Steam config.\n\nRestart Steam now?",
                            "Add to Steam",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes;
                    });

                    if (!shouldRestart)
                    {
                        AppendLog("Steam restart skipped by user.");
                        return;
                    }

                    AppendLog("Restarting Steam...");
                    _steamIntegration.RestartSteam(steamPath);
                    AppendLog("Steam restarted.");
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR Add to Steam: " + ex.Message);
                }
                finally
                {
                    Dispatch(() => _postRegisterBtn.IsEnabled = true);
                }
            });
        }

        private void OnPostMarkInstalledClicked(object sender, RoutedEventArgs e)
        {
            var libraryRoot = ResolveLibraryRootForActions();
            var data = BuildGameDataForActions();
            if (string.IsNullOrWhiteSpace(libraryRoot) || data == null)
            {
                AppendLog("Mark Installed in Steam: missing game/library context.");
                return;
            }

            _postMarkInstalledBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    AppendLog("--- Mark Installed in Steam (ACF) ---");
                    _steamIntegration.WriteAcfForInstall(data, libraryRoot, line => AppendLog(line));
                    AppendLog("ACF manifest written.");

                    if (_installedGame != null && _installedGamesManager != null)
                    {
                        _installedGame.RegisteredWithSteam = true;
                        _installedGamesManager.Save(_installedGame);
                    }

                    var shouldRestart = false;
                    Dispatch(() =>
                    {
                        shouldRestart = _dialogService.ShowMessage(
                            "ACF written.\n\nRestart Steam now?",
                            "Mark Installed in Steam",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes;
                    });

                    if (!shouldRestart)
                    {
                        AppendLog("Steam restart skipped by user.");
                        return;
                    }

                    var steamPath = _steamIntegration.GetSteamPath();
                    AppendLog("Restarting Steam...");
                    _steamIntegration.RestartSteam(steamPath);
                    AppendLog("Steam restarted.");
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR Mark Installed in Steam: " + ex.Message);
                }
                finally
                {
                    Dispatch(() => _postMarkInstalledBtn.IsEnabled = true);
                }
            });
        }

        private string ResolveInstallDirForActions()
        {
            if (_installedGame != null && !string.IsNullOrWhiteSpace(_installedGame.InstallPath))
                return _installedGame.InstallPath;
            if (!string.IsNullOrWhiteSpace(_lastDestPath) && _gameData != null)
                return Path.Combine(_lastDestPath, "steamapps", "common", AcfWriter.GetInstallFolderName(_gameData));
            return null;
        }

        private string ResolveLibraryRootForActions()
        {
            if (!string.IsNullOrWhiteSpace(_lastDestPath))
                return _lastDestPath;
            if (_installedGame != null && !string.IsNullOrWhiteSpace(_installedGame.LibraryPath))
                return _installedGame.LibraryPath;
            return _selectedLibraryPath;
        }

        private GameData BuildGameDataForActions()
        {
            if (_gameData != null && !string.IsNullOrWhiteSpace(_gameData.AppId))
                return _gameData;
            if (_installedGame == null || string.IsNullOrWhiteSpace(_installedGame.AppId))
                return null;

            var gd = new GameData
            {
                AppId = _installedGame.AppId,
                GameName = string.IsNullOrWhiteSpace(_installedGame.GameName) ? ("App_" + _installedGame.AppId) : _installedGame.GameName,
                InstallDir = _installedGame.InstallDir,
                BuildId = string.IsNullOrWhiteSpace(_installedGame.SteamBuildId) ? "0" : _installedGame.SteamBuildId,
                SelectedDepots = new List<string>(_installedGame.SelectedDepots ?? new List<string>()),
                Manifests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Depots = new Dictionary<string, DepotInfo>(),
                Dlcs = new Dictionary<string, string>()
            };

            if (_installedGame.ManifestGIDs != null)
            {
                foreach (var kv in _installedGame.ManifestGIDs)
                    gd.Manifests[kv.Key] = kv.Value;
            }

            return gd;
        }

        private string ResolveSingleLuaSourcePath()
        {
            var zipPath = ResolveManifestZipPathForActions();
            if (string.IsNullOrWhiteSpace(zipPath))
                return null;
            return _steamIntegration.ExtractSingleLua(zipPath);
        }

        private string ResolveManifestZipPathForActions()
        {
            var appId = _gameData != null ? _gameData.AppId : (_installedGame != null ? _installedGame.AppId : null);
            return _steamIntegration.ResolveManifestZip(appId, _lastManifestZipPath, _manifestCacheRoot);
        }

        private void OnGoldbergClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDownloadDir) || string.IsNullOrEmpty(_resolvedAppId)) return;
            if (string.IsNullOrEmpty(_settings.GoldbergFilesPath))
            {
                AppendLog("ERROR: Goldberg files path not configured. Open Settings.");
                return;
            }

            var detectedArch = GoldbergRunner.DetectArch(_currentDownloadDir);
            var appOutputDir = Path.Combine(
                _settings.GoldbergFilesPath, "generate_emu_config", "_OUTPUT", _resolvedAppId.Trim());
            var options = GoldbergOptionsDialog.ShowPicker(Window.GetWindow(this), _dialogService, detectedArch, appOutputDir);
            if (options == null)
            {
                AppendLog("Goldberg setup cancelled.");
                return;
            }

            _goldbergBtn.IsEnabled = false;
            var appId    = _resolvedAppId;
            var gameDir  = _currentDownloadDir;
            var opts     = options;
            var settings = _settings;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var runner = new GoldbergRunner(settings.GoldbergFilesPath);
                    var outputPath = runner.Run(gameDir, appId, opts, settings, line => AppendLog(line));
                    if (string.IsNullOrEmpty(outputPath))
                        AppendLog("ERROR (Goldberg): setup did not complete (see messages above).");
                    else if (!opts.CopyFiles)
                        AppendLog("Goldberg output path: " + outputPath);
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR (Goldberg): " + ex.Message);
                }
                finally
                {
                    Dispatch(() => _goldbergBtn.IsEnabled = true);
                }
            });
        }

        // ── Installed game detection ──────────────────────────────────────────────

        private void CheckIfInstalled()
        {
            if (_initialPlayniteGameId != Guid.Empty)
                _installedGame = _installedGamesManager.FindByPlayniteId(_initialPlayniteGameId);

            if (_installedGame == null
                && _initialGamePluginId == SteamPluginGuid
                && !string.IsNullOrWhiteSpace(_initialGameId))
            {
                _installedGame = _installedGamesManager.FindByAppId(_initialGameId);
            }

            if (_installedGame != null)
            {
                Dispatch(() =>
                {
                    _resolveStatusLabel.Visibility = Visibility.Collapsed;
                    _foundPanel.Visibility = Visibility.Visible;
                    _foundLabel.Text = "\u2713 " + _installedGame.GameName + " (" + _installedGame.AppId + ")";
                    _searchPanel.Visibility = Visibility.Collapsed;
                    _installedPanel.Visibility = Visibility.Visible;
                    _downloadBtn.Content = "Download selected depots (add-on)";
                    _downloadBtn.IsEnabled = false;
                    _resolvedAppId = _installedGame.AppId;
                    ApplyPrefillLibraryFromInstalled();
                    RefreshFetchButtonsForCacheAndBusy();
                    ApplyCachedManifestIfPresent();
                });
            }
        }

        /// <summary>
        /// When adding depots to an existing install, default the Steam library root from the saved record
        /// (DepotDownloader expects library root, not .../common/GameName).
        /// </summary>
        private void ApplyPrefillLibraryFromInstalled()
        {
            if (_installedGame == null) return;
            var lib = _installedGame.LibraryPath;
            if (string.IsNullOrWhiteSpace(lib)) return;
            lib = lib.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(lib)) return;
            _selectedLibraryPath = lib;
            if (_selectedLibLabel != null)
            {
                _selectedLibLabel.Text = lib;
                _selectedLibLabel.Foreground = Brushes.White;
            }
        }

        /// <summary>Depot ids already saved on the install record — used to show read-only rows in add-on mode.</summary>
        private ICollection<string> GetInstalledDepotIdsForPopulate()
        {
            if (_installedGame?.SelectedDepots == null || _installedGame.SelectedDepots.Count == 0)
                return null;
            return _installedGame.SelectedDepots;
        }

        /// <summary>Morrenus fetch is blocked by disk cache only for non-installed flows; installed users need refresh for new depots.</summary>
        private bool CacheBlocksMorrenusFetch()
        {
            if (string.IsNullOrWhiteSpace(_resolvedAppId)) return false;
            if (_installedGame != null) return false;
            if (string.Equals(_cacheReadFailedAppId, _resolvedAppId.Trim(), StringComparison.Ordinal))
                return false;
            return ManifestCache.TryGetCachedZipPath(_manifestCacheRoot, _resolvedAppId, out _);
        }

        private static GameData CloneGameDataWithDepots(GameData src, List<string> depotIdsForRun)
        {
            return new GameData
            {
                AppId          = src.AppId,
                GameName       = src.GameName,
                InstallDir     = src.InstallDir,
                BuildId        = src.BuildId,
                Depots         = src.Depots,
                Manifests      = src.Manifests,
                Dlcs           = src.Dlcs,
                SelectedDepots = new List<string>(depotIdsForRun)
            };
        }

        private static List<string> MergeDepotIdLists(IEnumerable<string> a, IEnumerable<string> b)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (a != null)
                foreach (var x in a)
                    if (!string.IsNullOrEmpty(x)) set.Add(x);
            if (b != null)
                foreach (var x in b)
                    if (!string.IsNullOrEmpty(x)) set.Add(x);
            return set.ToList();
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
            _resolveStatusLabel.Visibility = _hasInitialGame ? Visibility.Visible : Visibility.Collapsed;

            if (_hasInitialGame)
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

                if (_installedGame.PlayniteGameId != Guid.Empty)
                {
                    var removeFromHost = _dialogService.ShowMessage(
                        "Remove \"" + _installedGame.GameName + "\" from library as well?",
                        "Remove from Library",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (removeFromHost == MessageBoxResult.Yes)
                        _appHost?.RemoveFromHostLibrary(_installedGame.PlayniteGameId.ToString());
                }

                _installedGame = null;
                _installedPanel.Visibility = Visibility.Collapsed;
                _downloadBtn.Content = "Download Selected Depots";
                _downloadBtn.IsEnabled = false;
                _resolveStatusLabel.Visibility = _hasInitialGame ? Visibility.Visible : Visibility.Collapsed;

                if (_hasInitialGame)
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

            var pwdWindow = _dialogService.CreateWindow("Steam Password", stack, _dialogService.GetMainWindow());
            pwdWindow.Width = 360;
            pwdWindow.Height = 140;
            pwdWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var okBtn = new Button
            {
                Content = "Open Auth Window",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(12, 4, 12, 4)
            };
            okBtn.Click += (s, e) => pwdWindow.DialogResult = true;
            stack.Children.Add(pwdBox);
            stack.Children.Add(okBtn);
            pwdBox.Focus();

            if (pwdWindow.ShowDialog() != true) return;

            var password = pwdBox.Password;
            if (string.IsNullOrEmpty(password)) return;

            AppendLog("--- Steam Authentication ---");
            AppendLog("A console window will open. Complete any Steam Guard prompt there, then close it.");

            ThreadPool.QueueUserWorkItem(_ =>
                _downloader.Authenticate(username, password, line => AppendLog(line)));
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
                        _lastManifestZipPath = zipPath;
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
            _progressSamples.Clear();
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

                int currentPct = (int)_progressBar.Value;
                _progressSamples.Enqueue((DateTime.UtcNow, currentPct));
                while (_progressSamples.Count > ProgressSampleWindow)
                    _progressSamples.Dequeue();

                // Rolling window rate: avoids anchoring on slow startup phases (pre-alloc, validation).
                if (currentPct > 1 && !_progressBar.IsIndeterminate && _progressSamples.Count >= 2)
                {
                    var oldest = _progressSamples.Peek();
                    double windowSeconds = (DateTime.UtcNow - oldest.Time).TotalSeconds;
                    double progressGain = currentPct - oldest.Progress;
                    if (windowSeconds > 0 && progressGain > 0)
                    {
                        double ratePerSec = progressGain / windowSeconds;
                        double etaSec = (100 - currentPct) / ratePerSec;
                        _etaLabel.Text = FormatTimeRemaining(TimeSpan.FromSeconds(etaSec));
                    }
                    else
                        _etaLabel.Text = "ETA: --";
                }
                else
                    _etaLabel.Text = "ETA: --";
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
            catch (Exception ex)
            {
                logger.Debug("GetTotalBytesReceived: " + ex.Message);
            }
            return total;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void SetBusy(bool busy, string status)
        {
            _downloading = busy;
            if (!busy) { _progressBar.IsIndeterminate = false; StopSpeedMonitor(); }
            var hasId = !string.IsNullOrWhiteSpace(_resolvedAppId);
            var blocked = CacheBlocksMorrenusFetch();
            _fetchBtn.IsEnabled       = !busy && hasId && !blocked;
            _fetchBtnSearch.IsEnabled = !busy && hasId && !blocked;
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
