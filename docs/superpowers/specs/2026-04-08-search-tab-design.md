# Search Tab — Detailed Implementation Spec

**Date:** 2026-04-08  
**Status:** Approved  
**Target reader:** AI implementer with moderate C# / WPF knowledge

---

## 1. Context

The sidebar button currently opens either `LibraryView` (installed games) or `DownloadView`
(download a game) depending on whether a Playnite game is selected. There is no way to search
the Steam catalog from inside the plugin. This spec adds a **Search tab** to the existing window
so users can find games, apply genre/year filters, and kick off a download.

---

## 2. Files to Create

| File | Description |
|------|-------------|
| `UI/PluginMainView.cs` | New top-level `UserControl` with a `TabControl` (Tab 0 = Library, Tab 1 = Search) |
| `UI/SearchView.cs` | Full search tab UI: search bar, filter sidebar, result cards |
| `Api/SteamApiClient.cs` | HTTP client for Steam Web API and Steam Store endpoints |

---

## 3. Files to Modify

**Before touching any existing file, create a `.bak` copy next to it.**

| File | What changes |
|------|-------------|
| `BlankPlugin.cs` | `OpenPluginWindow(Game)` always opens `PluginMainView` now; add `OpenDownloadForAppId(string appId, string name)` |
| `Settings/BlankPluginSettings.cs` | Add `SteamWebApiKey` string property (same pattern as `ApiKey`) |
| `Settings/BlankPluginSettingsView.cs` | Add a labeled input row for `SteamWebApiKey` (same pattern as existing key fields) |
| `UI/DownloadView.cs` | Add a second constructor overload that takes `(string appId, string name, ...)` instead of `(Game game, ...)` |

---

## 4. Data Models (add to `Api/SteamApiClient.cs`)

```csharp
// Returned by IStoreQueryService/Query — one entry per appid
public class SteamSearchItem
{
    public string AppId   { get; set; }   // e.g. "1245620"
    public string Name    { get; set; }   // e.g. "Elden Ring"
}

// Returned by store.steampowered.com/api/appdetails
public class SteamGameDetails
{
    public string AppId            { get; set; }
    public string Name             { get; set; }
    public string ShortDescription { get; set; }
    public string HeaderImageUrl   { get; set; }
    public string ReleaseDate      { get; set; }  // raw string, e.g. "25 Feb, 2022"
    public int    ReleaseYear      { get; set; }  // parsed from ReleaseDate
    public List<string> Genres     { get; set; }  // e.g. ["Action", "RPG"]
    public int    DlcCount         { get; set; }  // length of dlc[] array
}

// One entry from IStoreBrowseService/GetStoreCategories
public class SteamGenreTag
{
    public int    TagId { get; set; }
    public string Name  { get; set; }
}
```

---

## 5. SteamApiClient

**File:** `Api/SteamApiClient.cs`  
**Namespace:** `BlankPlugin`

Pattern is identical to `MorrenusClient`: one `HttpClient` field, constructor takes a key getter
`Func<string>`, all methods are synchronous (called from a `ThreadPool` thread).

```csharp
public class SteamApiClient
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private const string ApiBase   = "https://api.steampowered.com";
    private const string StoreBase = "https://store.steampowered.com";

    private readonly HttpClient   _http;
    private readonly Func<string> _getKey;

    public SteamApiClient(Func<string> getKey)
    {
        _getKey = getKey;
        // Same SSL and TLS settings as MorrenusClient
        ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }
    // ... methods below
}
```

### 5.1 GetGenreTags()

Fetches genre/tag categories for the filter sidebar. Called once on first `SearchView` open;
the result is cached in `SearchView._genreTags`.

```
GET https://api.steampowered.com/IStoreBrowseService/GetStoreCategories/v1/
    ?key={key}&language=english
```

Response JSON path to the list: `response.categories[]`  
Each category has `category_description` (string) and `tagid` (int).  
Only keep entries where `category_description` is not null/empty.

```csharp
public List<SteamGenreTag> GetGenreTags()
{
    var url = ApiBase + "/IStoreBrowseService/GetStoreCategories/v1/?key="
              + Uri.EscapeDataString(_getKey()) + "&language=english";
    var body = _http.GetAsync(url).Result.Content.ReadAsStringAsync().Result;
    var root = JObject.Parse(body);
    var cats = root["response"]?["categories"] as JArray;
    var result = new List<SteamGenreTag>();
    if (cats == null) return result;
    foreach (var c in cats)
    {
        var name = c.Value<string>("category_description");
        if (string.IsNullOrWhiteSpace(name)) continue;
        result.Add(new SteamGenreTag
        {
            TagId = c.Value<int>("tagid"),
            Name  = name
        });
    }
    return result;
}
```

### 5.2 SearchGames()

Calls `IStoreQueryService/Query/v1/` via GET with an `input_json` parameter.  
Returns up to 20 appids + names. Games only (no DLC, software, etc.).

```
GET https://api.steampowered.com/IStoreQueryService/Query/v1/
    ?key={key}&input_json={url-encoded JSON}
```

The JSON to URL-encode and pass as `input_json`:

```json
{
  "query_name": "search",
  "start": 0,
  "count": 20,
  "context": {
    "language": "english",
    "country_code": "US",
    "steam_realm": 1
  },
  "filters": {
    "type_filters": {
      "include_games": true,
      "include_dlc": false,
      "include_software": false,
      "include_videos": false,
      "include_hardware": false
    },
    "tagids_must_match": [
      { "tagids": [TAG_ID_1, TAG_ID_2] }
    ]
  },
  "search_term": "QUERY_HERE",
  "data_request": {
    "include_basic_info": true
  }
}
```

> **Note on `tagids_must_match`:** Omit the entire `tagids_must_match` key if no genres are
> selected. If genres are selected, include one object in the array containing all selected
> tag IDs — this means "match any of these tags" (OR logic within the array).

> **Note on date filtering:** Date range is applied **client-side** after fetching `appdetails`,
> not in the Query request. This avoids undocumented date filter parameter names.

Response parsing: `response.store_items[]`, each item has `id` (appid as int) and `name` (string).

```csharp
public List<SteamSearchItem> SearchGames(string searchTerm, List<int> tagIds)
{
    var filters = new JObject
    {
        ["type_filters"] = new JObject
        {
            ["include_games"]    = true,
            ["include_dlc"]      = false,
            ["include_software"] = false,
            ["include_videos"]   = false,
            ["include_hardware"] = false
        }
    };

    if (tagIds != null && tagIds.Count > 0)
    {
        filters["tagids_must_match"] = new JArray
        {
            new JObject { ["tagids"] = new JArray(tagIds.Cast<object>().ToArray()) }
        };
    }

    var inputJson = new JObject
    {
        ["query_name"]   = "search",
        ["start"]        = 0,
        ["count"]        = 20,
        ["search_term"]  = searchTerm,
        ["context"]      = new JObject
        {
            ["language"]     = "english",
            ["country_code"] = "US",
            ["steam_realm"]  = 1
        },
        ["filters"]      = filters,
        ["data_request"] = new JObject { ["include_basic_info"] = true }
    };

    var url = ApiBase + "/IStoreQueryService/Query/v1/?key="
              + Uri.EscapeDataString(_getKey())
              + "&input_json=" + Uri.EscapeDataString(inputJson.ToString(Formatting.None));

    var response = _http.GetAsync(url).Result;
    var body     = response.Content.ReadAsStringAsync().Result;
    var root     = JObject.Parse(body);
    var items    = root["response"]?["store_items"] as JArray;
    var result   = new List<SteamSearchItem>();
    if (items == null) return result;

    foreach (var item in items)
    {
        var appid = item.Value<int?>("id") ?? item.Value<int?>("appid");
        var name  = item.Value<string>("name");
        if (appid == null || string.IsNullOrEmpty(name)) continue;
        result.Add(new SteamSearchItem { AppId = appid.ToString(), Name = name });
    }
    return result;
}
```

### 5.3 GetGameDetails()

Fetches full metadata for a single appid from the public Steam Store API (no key required).

```
GET https://store.steampowered.com/api/appdetails
    ?appids={appid}&filters=basic,genres,release_date,short_description,dlc
```

Response JSON path: `{appid}.success` must be `true`; data is under `{appid}.data`.

```csharp
public SteamGameDetails GetGameDetails(string appId)
{
    var url  = StoreBase + "/api/appdetails?appids=" + appId
               + "&filters=basic,genres,release_date,short_description,dlc";
    var body = _http.GetAsync(url).Result.Content.ReadAsStringAsync().Result;
    var root = JObject.Parse(body);
    var node = root[appId];
    if (node == null || !node.Value<bool>("success")) return null;

    var data   = node["data"] as JObject;
    if (data == null) return null;

    // Parse release year from string like "25 Feb, 2022" or "Feb 2022"
    var relStr = data["release_date"]?.Value<string>("date") ?? string.Empty;
    int.TryParse(relStr.Length >= 4 ? relStr.Substring(relStr.Length - 4) : "0", out int year);

    // Genres: array of { id, description }
    var genreNames = new List<string>();
    if (data["genres"] is JArray genres)
        foreach (var g in genres)
            genreNames.Add(g.Value<string>("description"));

    // DLC: array of appids
    var dlcCount = data["dlc"] is JArray dlc ? dlc.Count : 0;

    return new SteamGameDetails
    {
        AppId            = appId,
        Name             = data.Value<string>("name"),
        ShortDescription = data.Value<string>("short_description"),
        HeaderImageUrl   = data.Value<string>("header_image"),
        ReleaseDate      = relStr,
        ReleaseYear      = year,
        Genres           = genreNames,
        DlcCount         = dlcCount
    };
}
```

---

## 6. Settings Changes

### `BlankPluginSettings.cs`

Add one property using exactly the same pattern as `ApiKey`:

```csharp
private string _steamWebApiKey = string.Empty;
public string SteamWebApiKey
{
    get => _steamWebApiKey;
    set => SetValue(ref _steamWebApiKey, value ?? string.Empty);
}
```

In the constructor where saved settings are loaded, add:
```csharp
SteamWebApiKey = saved.SteamWebApiKey ?? string.Empty;
```

### `BlankPluginSettingsView.cs`

Add a new row in `BuildLayout()` with a label "Steam Web API Key:" and a `TextBox` bound to
`settings.SteamWebApiKey`. Follow exactly the same pattern used for the existing API key row.

---

## 7. PluginMainView

**File:** `UI/PluginMainView.cs`

```csharp
public class PluginMainView : UserControl
{
    public PluginMainView(
        BlankPluginSettings settings,
        InstalledGamesManager installedGames,
        IPlayniteAPI api,
        UpdateChecker updateChecker,
        BlankPlugin plugin)
    {
        var tabs = new TabControl();

        // Tab 0: Library (existing view, untouched)
        var libraryTab = new TabItem
        {
            Header  = "Library",
            Content = new LibraryView(settings, installedGames, api, updateChecker)
        };

        // Tab 1: Search (new)
        var searchTab = new TabItem
        {
            Header  = "Search",
            Content = new SearchView(settings, api, plugin)
        };

        tabs.Items.Add(libraryTab);
        tabs.Items.Add(searchTab);

        Content = tabs;
    }
}
```

---

## 8. BlankPlugin.cs Changes

### 8.1 Modify `OpenPluginWindow(Game game)`

Replace the `if (game != null) ... else ...` branching so the window **always** opens
`PluginMainView`. The `game` parameter is kept so the title can be set, but `DownloadView` is
no longer opened directly from here — it is only opened from the Download button in `SearchView`
or from the right-click context menu on an installed game.

```csharp
private void OpenPluginWindow(Game game)
{
    var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = true,
        ShowCloseButton    = true
    });
    window.Width  = 700;
    window.Height = 600;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner   = PlayniteApi.Dialogs.GetCurrentAppWindow();
    window.Title   = "BlankPlugin";
    window.Content = new PluginMainView(Settings, InstalledGames, PlayniteApi, _updateChecker, this);
    window.ShowDialog();
    _lastSelectedGame = null;
}
```

> **Note:** The game-specific `DownloadView` path (right-click "Open in BlankPlugin") still works
> because `GetGameMenuItems` calls `OpenPluginWindow(_lastSelectedGame)` which now always opens
> `PluginMainView`. If the old behavior of opening `DownloadView` directly for a right-clicked game
> is desired, that is a separate decision — for now the menu item opens the full window.

### 8.2 Add `OpenDownloadForAppId()`

This new public method is called by the Download button in `SearchView`.

```csharp
public void OpenDownloadForAppId(string appId, string name)
{
    var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = true,
        ShowCloseButton    = true
    });
    window.Width  = 700;
    window.Height = 600;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner   = PlayniteApi.Dialogs.GetCurrentAppWindow();
    window.Title   = "BlankPlugin — " + name;
    window.Content = new DownloadView(appId, name, Settings, InstalledGames, PlayniteApi, _updateChecker);
    window.ShowDialog();
}
```

---

## 9. DownloadView — New Constructor Overload

Add a **second constructor** that accepts `(string appId, string name, ...)` and goes straight to
the "found" panel state. **Do not change the existing constructor.**

```csharp
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
    // _game stays null — not a Playnite game object
    _game    = null;
    _settings = settings;
    _api      = api;
    _client   = new MorrenusClient(() => _settings.ApiKey);
    _downloader = new DepotDownloaderRunner();
    _installedGamesManager = installedGamesManager;
    _updateChecker = updateChecker;

    Content = BuildLayout();
    _downloadPathBox.Text = _settings.DownloadPath;

    ThreadPool.QueueUserWorkItem(_ => RefreshApiStatus());

    // Skip resolution — go directly to "found" state
    _resolvedAppId = appId;
    _gameInfoLabel.Text = name;
    ShowFoundPanel(appId, name);
}
```

`ShowFoundPanel(string appId, string name)` must be **extracted** from the existing resolve logic.
Look for the code in `ResolveAppId()` that sets `_resolvedAppId`, updates `_foundLabel`, makes
`_foundPanel` visible, and collapses `_resolveStatusLabel` and `_searchPanel`. Extract that
Dispatcher.Invoke block into a private method:

```csharp
private void ShowFoundPanel(string appId, string name)
{
    Dispatcher.Invoke(() =>
    {
        _resolvedAppId               = appId;
        _foundLabel.Text             = "✓ " + name + " (" + appId + ")";
        _resolveStatusLabel.Visibility = Visibility.Collapsed;
        _searchPanel.Visibility       = Visibility.Collapsed;
        _foundPanel.Visibility        = Visibility.Visible;
        _fetchBtn.IsEnabled           = true;
    });
}
```

Then call `ShowFoundPanel` from the existing resolve path wherever it currently sets those
visibility/text values inline.

---

## 10. SearchView

**File:** `UI/SearchView.cs`  
**Namespace:** `BlankPlugin`  
**Base class:** `UserControl`

### 10.1 Fields

```csharp
public class SearchView : UserControl
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private readonly BlankPluginSettings _settings;
    private readonly IPlayniteAPI        _api;
    private readonly BlankPlugin         _plugin;
    private readonly SteamApiClient      _steamClient;

    // Genre tags loaded on first open
    private List<SteamGenreTag> _genreTags;
    private bool _genreTagsLoaded = false;

    // UI: Filter sidebar
    private StackPanel _genreCheckboxPanel;  // populated after genre fetch
    private TextBox    _fromYearBox;
    private TextBox    _toYearBox;

    // UI: Search
    private TextBox    _searchBox;
    private Button     _searchBtn;
    private TextBlock  _statusLabel;         // shows "Searching...", error messages

    // UI: Results
    private StackPanel _resultsList;         // cards appended here
```

### 10.2 Constructor

```csharp
public SearchView(BlankPluginSettings settings, IPlayniteAPI api, BlankPlugin plugin)
{
    _settings    = settings;
    _api         = api;
    _plugin      = plugin;
    _steamClient = new SteamApiClient(() => _settings.SteamWebApiKey);

    Content = BuildLayout();

    // Load genre tags async on first open
    ThreadPool.QueueUserWorkItem(_ => LoadGenreTags());
}
```

### 10.3 BuildLayout()

Outer `Grid` with 2 rows:
- Row 0 (`Auto`): search bar (TextBox + Search button)
- Row 1 (`*`): horizontal split — filter sidebar (fixed width `140`) + results scroll

```csharp
private UIElement BuildLayout()
{
    var root = new Grid();
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });              // 0 search bar
    root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1 main area

    // ── Row 0: search bar ────────────────────────────────────────────────────
    var searchRow = new DockPanel { Margin = new Thickness(8, 8, 8, 6) };

    _searchBtn = new Button { Content = "Search", Width = 70, Margin = new Thickness(6, 0, 0, 0) };
    _searchBtn.Click += OnSearchClicked;
    DockPanel.SetDock(_searchBtn, Dock.Right);
    searchRow.Children.Add(_searchBtn);

    _searchBox = new TextBox { VerticalAlignment = VerticalAlignment.Center };
    _searchBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnSearchClicked(s, e); };
    searchRow.Children.Add(_searchBox);  // fills remaining space (last child in DockPanel)

    Grid.SetRow(searchRow, 0);
    root.Children.Add(searchRow);

    // ── Row 1: sidebar + results ─────────────────────────────────────────────
    var mainArea = new Grid();
    mainArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // sidebar
    mainArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // results

    // Sidebar
    var sidebar = BuildFilterSidebar();
    Grid.SetColumn(sidebar, 0);
    mainArea.Children.Add(sidebar);

    // Results
    var resultsArea = BuildResultsArea();
    Grid.SetColumn(resultsArea, 1);
    mainArea.Children.Add(resultsArea);

    Grid.SetRow(mainArea, 1);
    root.Children.Add(mainArea);

    return root;
}
```

### 10.4 BuildFilterSidebar()

```csharp
private UIElement BuildFilterSidebar()
{
    var sidebar = new ScrollViewer
    {
        VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        BorderThickness = new Thickness(0, 0, 1, 0),  // right border separating sidebar from results
        BorderBrush     = SystemColors.ControlDarkBrush
    };

    var panel = new StackPanel { Margin = new Thickness(8, 8, 4, 8) };

    // Genre section header
    panel.Children.Add(new TextBlock
    {
        Text       = "GENRE",
        FontSize   = 10,
        Opacity    = 0.6,
        Margin     = new Thickness(0, 0, 0, 4)
    });

    // Placeholder text while genre tags load
    _genreCheckboxPanel = new StackPanel();
    _genreCheckboxPanel.Children.Add(new TextBlock
    {
        Text    = "Loading...",
        Opacity = 0.5,
        FontSize = 11
    });
    panel.Children.Add(_genreCheckboxPanel);

    // Year range section header
    panel.Children.Add(new TextBlock
    {
        Text    = "RELEASE YEAR",
        FontSize = 10,
        Opacity  = 0.6,
        Margin  = new Thickness(0, 12, 0, 4)
    });

    panel.Children.Add(new TextBlock { Text = "From", FontSize = 11, Opacity = 0.7 });
    _fromYearBox = new TextBox { Margin = new Thickness(0, 2, 0, 6), MaxLength = 4 };
    panel.Children.Add(_fromYearBox);

    panel.Children.Add(new TextBlock { Text = "To", FontSize = 11, Opacity = 0.7 });
    _toYearBox = new TextBox { Margin = new Thickness(0, 2, 0, 0), MaxLength = 4 };
    panel.Children.Add(_toYearBox);

    sidebar.Content = panel;
    return sidebar;
}
```

### 10.5 BuildResultsArea()

```csharp
private UIElement BuildResultsArea()
{
    var outer = new Grid();
    outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status label
    outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // scroll

    _statusLabel = new TextBlock
    {
        Margin   = new Thickness(8, 4, 8, 4),
        FontSize = 11,
        Opacity  = 0.7,
        Visibility = Visibility.Collapsed
    };
    Grid.SetRow(_statusLabel, 0);
    outer.Children.Add(_statusLabel);

    var scroll = new ScrollViewer
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    _resultsList = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
    scroll.Content = _resultsList;
    Grid.SetRow(scroll, 1);
    outer.Children.Add(scroll);

    return outer;
}
```

### 10.6 LoadGenreTags()

Called once from a `ThreadPool` thread.

```csharp
private void LoadGenreTags()
{
    if (_genreTagsLoaded) return;
    try
    {
        _genreTags       = _steamClient.GetGenreTags();
        _genreTagsLoaded = true;
        Dispatcher.Invoke(PopulateGenreCheckboxes);
    }
    catch (Exception ex)
    {
        logger.Warn("GetGenreTags failed: " + ex.Message);
        Dispatcher.Invoke(() =>
        {
            _genreCheckboxPanel.Children.Clear();
            _genreCheckboxPanel.Children.Add(new TextBlock
            {
                Text    = "Failed to load genres",
                Opacity = 0.5,
                FontSize = 11
            });
        });
    }
}
```

### 10.7 PopulateGenreCheckboxes()

Called on the UI thread after genre tags load.

```csharp
private void PopulateGenreCheckboxes()
{
    _genreCheckboxPanel.Children.Clear();
    if (_genreTags == null) return;
    foreach (var tag in _genreTags.OrderBy(t => t.Name))
    {
        var cb = new CheckBox
        {
            Content = tag.Name,
            Tag     = tag.TagId,   // store tagId in Tag property for retrieval later
            FontSize = 11,
            Margin  = new Thickness(0, 1, 0, 1)
        };
        _genreCheckboxPanel.Children.Add(cb);
    }
}
```

### 10.8 OnSearchClicked()

```csharp
private void OnSearchClicked(object sender, RoutedEventArgs e)
{
    var term = _searchBox.Text.Trim();
    if (string.IsNullOrEmpty(term)) return;

    // Collect selected tag IDs
    var selectedTagIds = new List<int>();
    foreach (CheckBox cb in _genreCheckboxPanel.Children.OfType<CheckBox>())
        if (cb.IsChecked == true)
            selectedTagIds.Add((int)cb.Tag);

    // Parse optional year filters
    int.TryParse(_fromYearBox.Text.Trim(), out int fromYear);
    int.TryParse(_toYearBox.Text.Trim(), out int toYear);

    // Show searching state
    _searchBtn.IsEnabled = false;
    _resultsList.Children.Clear();
    _statusLabel.Text       = "Searching...";
    _statusLabel.Visibility = Visibility.Visible;

    ThreadPool.QueueUserWorkItem(_ => RunSearch(term, selectedTagIds, fromYear, toYear));
}
```

### 10.9 RunSearch()

Runs on a `ThreadPool` thread. Calls `SearchGames`, then `GetGameDetails` for each result in
parallel, applies date filter, renders cards.

```csharp
private void RunSearch(string term, List<int> tagIds, int fromYear, int toYear)
{
    try
    {
        var searchItems = _steamClient.SearchGames(term, tagIds);

        if (searchItems.Count == 0)
        {
            Dispatcher.Invoke(() =>
            {
                _statusLabel.Text = "No results found.";
                _searchBtn.IsEnabled = true;
            });
            return;
        }

        // Fetch details for all results in parallel
        var details = new SteamGameDetails[searchItems.Count];
        var threads = new List<Thread>();
        for (int i = 0; i < searchItems.Count; i++)
        {
            int idx    = i;
            string id  = searchItems[i].AppId;
            var t = new Thread(() =>
            {
                try   { details[idx] = _steamClient.GetGameDetails(id); }
                catch { details[idx] = null; }
            });
            t.Start();
            threads.Add(t);
        }
        foreach (var t in threads) t.Join();

        // Apply client-side year filter
        var filtered = details
            .Where(d => d != null)
            .Where(d => fromYear == 0 || d.ReleaseYear == 0 || d.ReleaseYear >= fromYear)
            .Where(d => toYear   == 0 || d.ReleaseYear == 0 || d.ReleaseYear <= toYear)
            .ToList();

        Dispatcher.Invoke(() =>
        {
            _resultsList.Children.Clear();
            if (filtered.Count == 0)
            {
                _statusLabel.Text = "No results after filtering.";
            }
            else
            {
                _statusLabel.Visibility = Visibility.Collapsed;
                foreach (var game in filtered)
                    _resultsList.Children.Add(BuildGameCard(game));
            }
            _searchBtn.IsEnabled = true;
        });
    }
    catch (Exception ex)
    {
        logger.Error("Search failed: " + ex.Message);
        Dispatcher.Invoke(() =>
        {
            _statusLabel.Text    = "Search failed: " + ex.Message;
            _searchBtn.IsEnabled = true;
        });
    }
}
```

### 10.10 BuildGameCard()

Returns a single game card `UIElement`. Horizontal layout: header image | info | buttons.

```csharp
private UIElement BuildGameCard(SteamGameDetails game)
{
    var card = new Border
    {
        Margin          = new Thickness(0, 0, 0, 6),
        Padding         = new Thickness(8),
        CornerRadius    = new CornerRadius(4),
        Background      = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44))
    };

    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });               // image
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // info
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                  // buttons

    // ── Column 0: header image ───────────────────────────────────────────────
    var imgContainer = new Border
    {
        Width      = 92,
        Height     = 43,
        Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5a)),
        Margin     = new Thickness(0, 0, 8, 0)
    };
    var img = new Image
    {
        Stretch             = Stretch.UniformToFill,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment   = VerticalAlignment.Center
    };
    imgContainer.Child = img;
    Grid.SetColumn(imgContainer, 0);
    grid.Children.Add(imgContainer);

    // Load image async (BitmapImage with BeginInit/EndInit on UI thread, download on thread pool)
    var imgUrl = string.IsNullOrEmpty(game.HeaderImageUrl)
        ? "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + game.AppId + "/header.jpg"
        : game.HeaderImageUrl;
    ThreadPool.QueueUserWorkItem(_ =>
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource        = new Uri(imgUrl);
            bmp.CacheOption      = BitmapCacheOption.OnLoad;
            bmp.CreateOptions    = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();  // REQUIRED: makes BitmapImage cross-thread safe
            Dispatcher.Invoke(() => img.Source = bmp);
        }
        catch { /* image load failure is silently ignored */ }
    });

    // ── Column 1: info ────────────────────────────────────────────────────────
    var info = new StackPanel { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    info.Children.Add(new TextBlock
    {
        Text       = game.Name,
        FontWeight = FontWeights.Bold,
        FontSize   = 13,
        TextTrimming = TextTrimming.CharacterEllipsis
    });

    // Genres + year on one line, e.g. "Action • RPG · 2022"
    var genrePart = game.Genres.Count > 0
        ? string.Join(" • ", game.Genres) + " · " + (game.ReleaseYear > 0 ? game.ReleaseYear.ToString() : "?")
        : (game.ReleaseYear > 0 ? game.ReleaseYear.ToString() : string.Empty);

    if (!string.IsNullOrEmpty(genrePart))
        info.Children.Add(new TextBlock
        {
            Text      = genrePart,
            FontSize  = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xa6, 0xe3, 0xa1)),
            Margin    = new Thickness(0, 1, 0, 0)
        });

    if (!string.IsNullOrEmpty(game.ShortDescription))
        info.Children.Add(new TextBlock
        {
            Text         = game.ShortDescription,
            FontSize     = 11,
            Opacity      = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight    = 16  // single line
        });

    if (game.DlcCount > 0)
        info.Children.Add(new TextBlock
        {
            Text      = "● " + game.DlcCount + " DLC" + (game.DlcCount == 1 ? "" : "s"),
            FontSize  = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xb4, 0xfa)),
            Margin    = new Thickness(0, 2, 0, 0)
        });

    Grid.SetColumn(info, 1);
    grid.Children.Add(info);

    // ── Column 2: buttons ─────────────────────────────────────────────────────
    var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

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
```

---

## 11. Required `using` Directives

### `SteamApiClient.cs`
```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
```

### `SearchView.cs`
```csharp
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
```

### `PluginMainView.cs`
```csharp
using Playnite.SDK;
using System.Windows.Controls;
```

---

## 12. Missing Key Handling

If `SteamWebApiKey` is empty when `SearchView` tries to load genre tags or run a search,
`SteamApiClient` will receive an empty key. The Steam API will return a 403 or empty response.
Handle this in `SearchView` by checking `string.IsNullOrWhiteSpace(_settings.SteamWebApiKey)`
at the start of `LoadGenreTags()` and `OnSearchClicked()`. If empty, show a message in
`_statusLabel`:

```
"Steam Web API key not set. Go to Settings → Steam Web API Key."
```

---

## 13. Verification Checklist

1. Build succeeds in Visual Studio 2022 (`Ctrl+Shift+B`)
2. `.pext` installed in Playnite
3. Click sidebar button → window opens on **Library** tab (existing list visible)
4. Switch to **Search** tab → genre checkboxes populate (takes ~1s)
5. Search "elden ring" (with valid Steam Web API key) → cards appear with images, genres, description
6. Cards with DLC show "● N DLCs" label
7. Cards without DLC show no DLC label
8. Select genre checkbox "RPG" → search again → only RPG results
9. Set From year "2020" → re-search → results only from 2020 onward
10. Set To year "2019" → re-search → results only up to 2019
11. Click **SteamDB** → browser opens `https://steamdb.info/app/{id}/`
12. Click **Store** → browser opens `https://store.steampowered.com/app/{id}/`
13. Click **Download** → new window opens showing `DownloadView` with game name pre-filled and "found" panel visible (no searching, no resolving)
14. Empty API key → status label shows the missing-key message
15. Invalid search term (no results) → "No results found." message
