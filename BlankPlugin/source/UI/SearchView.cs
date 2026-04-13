using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlankPlugin
{
    public class SearchView : UserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BlankPluginSettings   _settings;
        private readonly IPlayniteAPI          _api;
        private readonly BlankPlugin           _plugin;
        private readonly LibraryGamesManager   _libraryGames;
        private readonly SteamApiClient        _steamClient;

        // UI: Search
        private TextBox   _searchBox;
        private Button    _searchBtn;
        private CheckBox  _dlcCheckbox;
        private TextBlock _statusLabel;

        // UI: Results
        private StackPanel _resultsList;
        private Button     _loadMoreBtn;

        // Pagination state — UI thread only
        private string _lastTerm;
        private string _lastTypeFilter;
        private int    _currentOffset;
        private int    _totalResults;

        private const int PageSize = 10;

        // ── Cache ────────────────────────────────────────────────────────────────
        // All 6 fields guarded by _cacheLock.
        // Written from ThreadPool threads; read on UI thread (snapshot under lock, then release).
        private readonly object            _cacheLock       = new object();
        private List<SteamGameDetails>     _cachedGames     = null;  // grows with each Load More page
        private List<SteamGameDetails>     _cachedDlc       = null;
        private string                     _cachedTerm      = null;  // staleness guard for PrefetchDlc
        private bool                       _dlcPrefetchDone = false;
        private bool                       _dlcPrefetchBusy = false;
        private string                     _dlcCacheLabel   = null;  // "N DLC for [game]"

        public SearchView(BlankPluginSettings settings, IPlayniteAPI api, BlankPlugin plugin, LibraryGamesManager libraryGames)
        {
            _settings     = settings;
            _api          = api;
            _plugin       = plugin;
            _libraryGames = libraryGames;
            _steamClient  = new SteamApiClient(() => _settings.SteamWebApiKey);

            Content = BuildLayout();
        }

        // ── Layout ───────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── Row 0: search bar ─────────────────────────────────────────────────
            var searchRow = new DockPanel { Margin = new Thickness(8, 8, 8, 6) };

            _dlcCheckbox = new CheckBox
            {
                Content           = "DLC",
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0)
            };
            _dlcCheckbox.Checked   += OnDlcCheckboxChanged;
            _dlcCheckbox.Unchecked += OnDlcCheckboxChanged;
            DockPanel.SetDock(_dlcCheckbox, Dock.Right);
            searchRow.Children.Add(_dlcCheckbox);

            _searchBtn = new Button { Content = "Search", Width = 70, Margin = new Thickness(6, 0, 0, 0) };
            _searchBtn.Click += OnSearchClicked;
            DockPanel.SetDock(_searchBtn, Dock.Right);
            searchRow.Children.Add(_searchBtn);

            _searchBox = new TextBox { VerticalAlignment = VerticalAlignment.Center };
            _searchBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnSearchClicked(s, e); };
            searchRow.Children.Add(_searchBox);

            Grid.SetRow(searchRow, 0);
            root.Children.Add(searchRow);

            // ── Row 1: results ────────────────────────────────────────────────────
            var resultsArea = BuildResultsArea();
            Grid.SetRow(resultsArea, 1);
            root.Children.Add(resultsArea);

            return root;
        }

        private UIElement BuildResultsArea()
        {
            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _statusLabel = new TextBlock
            {
                Margin     = new Thickness(8, 4, 8, 4),
                FontSize   = 11,
                Opacity    = 0.7,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_statusLabel, 0);
            outer.Children.Add(_statusLabel);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _resultsList = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
            scroll.Content = _resultsList;
            Grid.SetRow(scroll, 1);
            outer.Children.Add(scroll);

            return outer;
        }

        // ── Search ───────────────────────────────────────────────────────────────

        private void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            var term = _searchBox.Text.Trim();
            if (string.IsNullOrEmpty(term)) return;

            if (string.IsNullOrWhiteSpace(_settings.SteamWebApiKey))
            {
                _statusLabel.Text       = "Steam Web API key not set. Go to Settings \u2192 Steam Web API Key.";
                _statusLabel.Visibility = Visibility.Visible;
                return;
            }

            var typeFilter = _dlcCheckbox.IsChecked == true ? "dlc" : "game";

            // Invalidate caches on every new search
            lock (_cacheLock)
            {
                _cachedTerm      = null;
                _cachedGames     = null;
                _cachedDlc       = null;
                _dlcPrefetchDone = false;
                _dlcPrefetchBusy = false;
                _dlcCacheLabel   = null;
            }

            _lastTerm       = term;
            _lastTypeFilter = typeFilter;
            _currentOffset  = 0;
            _totalResults   = 0;

            _searchBtn.IsEnabled = false;
            _resultsList.Children.Clear();
            _statusLabel.Text       = "Searching...";
            _statusLabel.Visibility = Visibility.Visible;

            ThreadPool.QueueUserWorkItem(_ => RunSearch(term, typeFilter, 0));
        }

        private void OnLoadMoreClicked(object sender, RoutedEventArgs e)
        {
            var term       = _lastTerm;
            var typeFilter = _lastTypeFilter;
            var offset     = _currentOffset;

            _loadMoreBtn.IsEnabled  = false;
            _statusLabel.Text       = "Loading more...";
            _statusLabel.Visibility = Visibility.Visible;

            ThreadPool.QueueUserWorkItem(_ => RunSearch(term, typeFilter, offset));
        }

        private void OnDlcCheckboxChanged(object sender, RoutedEventArgs e)
        {
            bool isDlc = _dlcCheckbox.IsChecked == true;

            if (isDlc)
            {
                bool                   prefetchDone, prefetchBusy;
                List<SteamGameDetails> cachedDlc;

                lock (_cacheLock)
                {
                    prefetchDone = _dlcPrefetchDone;
                    prefetchBusy = _dlcPrefetchBusy;
                    cachedDlc    = _cachedDlc;
                }

                if (prefetchDone)
                {
                    ShowCachedDlc();
                }
                else if (prefetchBusy)
                {
                    _statusLabel.Text       = "Loading DLC...";
                    _statusLabel.Visibility = Visibility.Visible;
                    // PrefetchDlc will call OnDlcPrefetchComplete when ready
                }
                else
                {
                    _statusLabel.Text       = "Run a search first.";
                    _statusLabel.Visibility = Visibility.Visible;
                }
            }
            else
            {
                List<SteamGameDetails> cachedGames;
                lock (_cacheLock) { cachedGames = _cachedGames; }

                if (cachedGames != null && cachedGames.Count > 0)
                    ShowCachedGames();
                else
                {
                    _statusLabel.Text       = "Search again for game results.";
                    _statusLabel.Visibility = Visibility.Visible;
                    _resultsList.Children.Clear();
                }
            }
        }

        private void RunSearch(string term, string typeFilter, int offset)
        {
            try
            {
                if (typeFilter == "dlc")
                    RunDlcSearch(term);
                else
                    RunGameSearch(term, offset);
            }
            catch (Exception ex)
            {
                logger.Error("Search failed: " + ex.Message);
                Dispatcher.Invoke(() =>
                {
                    _statusLabel.Text       = "Search failed: " + ex.Message;
                    _statusLabel.Visibility = Visibility.Visible;
                    _searchBtn.IsEnabled    = true;
                });
            }
        }

        private void RunGameSearch(string term, int offset)
        {
            var searchResult = _steamClient.SearchGames(term, offset, PageSize);

            if (searchResult.Items.Count == 0 && offset == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    _statusLabel.Text       = "No results found.";
                    _statusLabel.Visibility = Visibility.Visible;
                    _searchBtn.IsEnabled    = true;
                });
                return;
            }

            var details = FetchDetailsParallel(searchResult.Items);
            var valid   = details.Where(d => d != null && d.Type == "game").ToList();

            bool shouldPrefetch = false;

            Dispatcher.Invoke(() =>
            {
                if (offset == 0) _resultsList.Children.Clear();

                foreach (var game in valid)
                    _resultsList.Children.Add(BuildGameCard(game));

                _currentOffset += searchResult.Items.Count;
                _totalResults   = searchResult.Total;
                UpdateStatusAndLoadMore(valid.Count);
                _searchBtn.IsEnabled = true;

                // Accumulate into game cache and trigger DLC prefetch on first page
                lock (_cacheLock)
                {
                    if (_cachedGames == null) _cachedGames = new List<SteamGameDetails>();
                    _cachedGames.AddRange(valid);

                    if (offset == 0 && !_dlcPrefetchBusy && !_dlcPrefetchDone)
                    {
                        _dlcPrefetchBusy = true;
                        _cachedTerm      = term;
                        shouldPrefetch   = true;
                    }
                }
            });

            if (shouldPrefetch)
                ThreadPool.QueueUserWorkItem(_ => PrefetchDlc(term));
        }

        private void RunDlcSearch(string term)
        {
            var searchResult = _steamClient.SearchGames(term, 0, 5);

            if (searchResult.Items.Count == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    _statusLabel.Text       = "No results found.";
                    _statusLabel.Visibility = Visibility.Visible;
                    _searchBtn.IsEnabled    = true;
                });
                return;
            }

            SteamGameDetails baseGame = null;
            foreach (var candidate in searchResult.Items)
            {
                var d = _steamClient.GetGameDetails(candidate.AppId);
                if (d == null || d.Type != "game") continue;
                if (d.DlcIds != null && d.DlcIds.Count > 0) { baseGame = d; break; }
                if (baseGame == null) baseGame = d;
            }

            if (baseGame == null)
            {
                Dispatcher.Invoke(() =>
                {
                    _statusLabel.Text       = "No results found.";
                    _statusLabel.Visibility = Visibility.Visible;
                    _searchBtn.IsEnabled    = true;
                });
                return;
            }

            if (baseGame.DlcIds == null || baseGame.DlcIds.Count == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    _statusLabel.Text       = "No DLC found for " + baseGame.Name + ".";
                    _statusLabel.Visibility = Visibility.Visible;
                    _searchBtn.IsEnabled    = true;
                });
                return;
            }

            var dlcItems   = baseGame.DlcIds.Select(id => new SteamSearchItem { AppId = id, Name = "" }).ToList();
            var dlcDetails = FetchDetailsParallel(dlcItems);
            var valid      = dlcDetails.Where(d => d != null).ToList();

            var baseName = baseGame.Name;
            var label    = valid.Count == 0
                ? "No DLC details could be loaded for " + baseName + "."
                : valid.Count + " DLC for " + baseName;

            lock (_cacheLock)
            {
                _cachedDlc       = valid.Count > 0 ? valid : null;
                _dlcCacheLabel   = label;
                _dlcPrefetchDone = true;
                _dlcPrefetchBusy = false;
                _cachedTerm      = term;
            }

            Dispatcher.Invoke(() =>
            {
                _resultsList.Children.Clear();
                foreach (var dlc in valid)
                    _resultsList.Children.Add(BuildGameCard(dlc));

                _statusLabel.Text       = label;
                _statusLabel.Visibility = Visibility.Visible;
                _searchBtn.IsEnabled    = true;
            });
        }

        // ── DLC Prefetch (silent background fetch after game search) ─────────────

        private void PrefetchDlc(string term)
        {
            try
            {
                var searchResult = _steamClient.SearchGames(term, 0, 5);

                if (searchResult.Items.Count == 0)
                {
                    lock (_cacheLock)
                    {
                        _cachedDlc       = null;
                        _dlcCacheLabel   = "No DLC found.";
                        _dlcPrefetchDone = true;
                        _dlcPrefetchBusy = false;
                    }
                    Dispatcher.Invoke(OnDlcPrefetchComplete);
                    return;
                }

                SteamGameDetails baseGame = null;
                foreach (var candidate in searchResult.Items)
                {
                    lock (_cacheLock) { if (_cachedTerm != term) return; }  // abort if user searched again

                    var d = _steamClient.GetGameDetails(candidate.AppId);
                    if (d == null || d.Type != "game") continue;
                    if (d.DlcIds != null && d.DlcIds.Count > 0) { baseGame = d; break; }
                    if (baseGame == null) baseGame = d;
                }

                lock (_cacheLock) { if (_cachedTerm != term) return; }

                if (baseGame == null || baseGame.DlcIds == null || baseGame.DlcIds.Count == 0)
                {
                    var msg = baseGame != null
                        ? "No DLC found for " + baseGame.Name + "."
                        : "No DLC found.";
                    lock (_cacheLock)
                    {
                        _cachedDlc       = null;
                        _dlcCacheLabel   = msg;
                        _dlcPrefetchDone = true;
                        _dlcPrefetchBusy = false;
                    }
                    Dispatcher.Invoke(OnDlcPrefetchComplete);
                    return;
                }

                var dlcItems   = baseGame.DlcIds.Select(id => new SteamSearchItem { AppId = id, Name = "" }).ToList();
                var dlcDetails = FetchDetailsParallel(dlcItems);

                lock (_cacheLock) { if (_cachedTerm != term) return; }

                var valid    = dlcDetails.Where(d => d != null).ToList();
                var baseName = baseGame.Name;
                var label    = valid.Count == 0
                    ? "No DLC details could be loaded for " + baseName + "."
                    : valid.Count + " DLC for " + baseName;

                lock (_cacheLock)
                {
                    _cachedDlc       = valid.Count > 0 ? valid : null;
                    _dlcCacheLabel   = label;
                    _dlcPrefetchDone = true;
                    _dlcPrefetchBusy = false;
                }
                Dispatcher.Invoke(OnDlcPrefetchComplete);
            }
            catch (Exception ex)
            {
                logger.Error("PrefetchDlc failed: " + ex.Message);
                lock (_cacheLock)
                {
                    _dlcCacheLabel   = "DLC load failed: " + ex.Message;
                    _dlcPrefetchDone = true;
                    _dlcPrefetchBusy = false;
                }
                Dispatcher.Invoke(OnDlcPrefetchComplete);
            }
        }

        // Called on UI thread — only switches view if user currently has DLC mode on
        private void OnDlcPrefetchComplete()
        {
            if (_dlcCheckbox.IsChecked != true) return;
            ShowCachedDlc();
        }

        // ── Cache Display ────────────────────────────────────────────────────────

        private void ShowCachedGames()
        {
            List<SteamGameDetails> snapshot;
            lock (_cacheLock) { snapshot = _cachedGames; }

            _resultsList.Children.Clear();

            if (snapshot == null || snapshot.Count == 0)
            {
                _statusLabel.Text       = "Search again for game results.";
                _statusLabel.Visibility = Visibility.Visible;
                return;
            }

            foreach (var game in snapshot)
                _resultsList.Children.Add(BuildGameCard(game));

            // Restore Load More if there are unfetched pages
            if (_totalResults > _currentOffset)
            {
                _statusLabel.Text       = "Showing " + snapshot.Count + " of " + _totalResults;
                _statusLabel.Visibility = Visibility.Visible;

                _loadMoreBtn = new Button
                {
                    Content = "Load More (" + (_totalResults - _currentOffset) + " remaining)",
                    Margin  = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(16, 6, 16, 6),
                    Tag     = "loadmore"
                };
                _loadMoreBtn.Click += OnLoadMoreClicked;
                _resultsList.Children.Add(_loadMoreBtn);
            }
            else
            {
                _statusLabel.Text       = snapshot.Count + " result" + (snapshot.Count == 1 ? "" : "s");
                _statusLabel.Visibility = Visibility.Visible;
            }
        }

        private void ShowCachedDlc()
        {
            List<SteamGameDetails> snapshot;
            string label;
            lock (_cacheLock) { snapshot = _cachedDlc; label = _dlcCacheLabel; }

            _resultsList.Children.Clear();

            if (snapshot == null || snapshot.Count == 0)
            {
                _statusLabel.Text       = label ?? "No DLC found.";
                _statusLabel.Visibility = Visibility.Visible;
                return;
            }

            foreach (var dlc in snapshot)
                _resultsList.Children.Add(BuildGameCard(dlc));

            _statusLabel.Text       = label ?? snapshot.Count + " DLC";
            _statusLabel.Visibility = Visibility.Visible;
        }

        // ── Status / Load More ───────────────────────────────────────────────────

        private void UpdateStatusAndLoadMore(int shownCount)
        {
            for (int i = _resultsList.Children.Count - 1; i >= 0; i--)
            {
                if (_resultsList.Children[i] is Button btn && btn.Tag as string == "loadmore")
                {
                    _resultsList.Children.RemoveAt(i);
                    break;
                }
            }

            var loaded  = _resultsList.Children.OfType<Border>().Count();
            var hasMore = _totalResults > _currentOffset;

            if (shownCount == 0 && loaded == 0)
            {
                _statusLabel.Text       = "No results found.";
                _statusLabel.Visibility = Visibility.Visible;
                return;
            }

            if (hasMore)
            {
                _statusLabel.Text       = "Showing " + loaded + " of " + _totalResults;
                _statusLabel.Visibility = Visibility.Visible;

                _loadMoreBtn = new Button
                {
                    Content = "Load More (" + (_totalResults - _currentOffset) + " remaining)",
                    Margin  = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(16, 6, 16, 6),
                    Tag     = "loadmore"
                };
                _loadMoreBtn.Click += OnLoadMoreClicked;
                _resultsList.Children.Add(_loadMoreBtn);
            }
            else
            {
                _statusLabel.Text       = loaded + " result" + (loaded == 1 ? "" : "s");
                _statusLabel.Visibility = Visibility.Visible;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private SteamGameDetails[] FetchDetailsParallel(List<SteamSearchItem> items)
        {
            var details = new SteamGameDetails[items.Count];
            var threads = new List<Thread>();
            for (int i = 0; i < items.Count; i++)
            {
                int    idx = i;
                string id  = items[i].AppId;
                var t = new Thread(() =>
                {
                    try   { details[idx] = _steamClient.GetGameDetails(id); }
                    catch { details[idx] = null; }
                });
                t.Start();
                threads.Add(t);
            }
            foreach (var t in threads) t.Join();
            return details;
        }

        // ── Game Card ────────────────────────────────────────────────────────────

        private UIElement BuildGameCard(SteamGameDetails game)
        {
            var card = new Border
            {
                Margin       = new Thickness(0, 0, 0, 8),
                Padding      = new Thickness(10),
                CornerRadius = new CornerRadius(4),
                Background   = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44))
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ── Column 0: header image ────────────────────────────────────────────
            var imgContainer = new Border
            {
                Width      = 184,
                Height     = 86,
                Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5a)),
                Margin     = new Thickness(0, 0, 10, 0)
            };

            var imgUrl = string.IsNullOrEmpty(game.HeaderImageUrl)
                ? "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + game.AppId + "/header.jpg"
                : game.HeaderImageUrl;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(imgUrl);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                imgContainer.Child = new Image
                {
                    Source              = bmp,
                    Stretch             = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                };
            }
            catch (Exception ex)
            {
                logger.Warn("Failed to load image for " + game.AppId + ": " + ex.Message);
            }

            Grid.SetColumn(imgContainer, 0);
            grid.Children.Add(imgContainer);

            // ── Column 1: info ────────────────────────────────────────────────────
            var info = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };

            info.Children.Add(new TextBlock
            {
                Text         = game.Name,
                FontWeight   = FontWeights.Bold,
                FontSize     = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (game.ReleaseYear > 0)
                info.Children.Add(new TextBlock
                {
                    Text     = game.ReleaseYear.ToString(),
                    FontSize = 11,
                    Opacity  = 0.7,
                    Margin   = new Thickness(0, 1, 0, 0)
                });

            if (!string.IsNullOrEmpty(game.ShortDescription))
                info.Children.Add(new TextBlock
                {
                    Text         = game.ShortDescription,
                    FontSize     = 11,
                    Opacity      = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight    = 48,
                    TextTrimming = TextTrimming.WordEllipsis
                });

            if (game.DlcCount > 0)
                info.Children.Add(new TextBlock
                {
                    Text       = "\u25cf " + game.DlcCount + " DLC" + (game.DlcCount == 1 ? "" : "s"),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xb4, 0xfa)),
                    Margin     = new Thickness(0, 2, 0, 0)
                });

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // ── Column 2: buttons ─────────────────────────────────────────────────
            var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var inLibrary = _libraryGames != null && _libraryGames.Contains(game.AppId);
            var ig = _plugin?.InstalledGames?.FindByAppId(game.AppId);
            var installed = ig != null && Directory.Exists(ig.InstallPath);

            var addBtn = new Button
            {
                Content = inLibrary || installed ? "In library" : "+",
                Margin = new Thickness(0, 0, 0, 3),
                Width   = 72,
                ToolTip = inLibrary || installed ? "Already in plugin Library tab" : "Add to Library (without downloading)",
                IsEnabled = !inLibrary && !installed
            };
            addBtn.Click += (s, e) =>
            {
                if (_libraryGames == null) return;
                _libraryGames.AddOrUpdate(game.AppId, game.Name);
                addBtn.Content   = "In library";
                addBtn.IsEnabled = false;
                addBtn.ToolTip   = "Already in plugin Library tab";
            };
            buttons.Children.Add(addBtn);

            var downloadBtn = new Button { Content = "Download", Margin = new Thickness(0, 0, 0, 3), Width = 72 };
            downloadBtn.Click += (s, e) => _plugin.OpenDownloadForAppId(game.AppId, game.Name);
            buttons.Children.Add(downloadBtn);

            var steamDbBtn = new Button { Content = "SteamDB", Margin = new Thickness(0, 0, 0, 3), Width = 72 };
            steamDbBtn.Click += (s, e) => Process.Start("https://steamdb.info/app/" + game.AppId + "/");
            buttons.Children.Add(steamDbBtn);

            var storeBtn = new Button { Content = "Store", Width = 72 };
            storeBtn.Click += (s, e) => Process.Start("https://store.steampowered.com/app/" + game.AppId + "/");
            buttons.Children.Add(storeBtn);

            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            card.Child = grid;
            return card;
        }
    }
}
