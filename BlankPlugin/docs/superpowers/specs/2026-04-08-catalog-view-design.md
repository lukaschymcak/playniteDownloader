# Design: Morrenus Catalog View

**Date:** 2026-04-08  
**Status:** Approved

## Problem

Users have no way to browse the full list of available games from inside the plugin. They must know a game name to search for it, which creates friction for discovery. The Morrenus API exposes `/api/v1/library` at no cost that returns the complete catalog.

## Goal

Add a Catalog tab to the existing BlankPlugin window so users can browse all available games, see which ones are already installed, and start an install in one click — with a clear notice that installing costs daily API credits.

## Architecture Overview

Four changes, no existing files restructured:

1. **`Api/MorrenusClient.cs`** — add `GetLibrary()` method  
2. **`UI/DownloadView.cs`** — add a second constructor for opening with a known AppId  
3. **New `UI/CatalogView.cs`** — the catalog tab content  
4. **New `UI/MainTabView.cs`** — tab container replacing bare `LibraryView` in `OpenPluginWindow`  
5. **`BlankPlugin.cs`** — one-line change: `OpenPluginWindow(null)` uses `MainTabView` instead of `LibraryView`

---

## 1. `MorrenusClient.GetLibrary()`

```csharp
public List<MorrenusSearchResult> GetLibrary()
```

- Calls `GET /api/v1/library` with the Bearer auth header
- Parses the response the same way `SearchGames` does (array of `{game_id, game_name}` objects, or a `results` wrapper — handle both)
- Returns `List<MorrenusSearchResult>` on success, throws on HTTP error
- No auth required per the user ("no cost library call"), but send the key anyway for consistency

---

## 2. `DownloadView` — new constructor

Add a second constructor overload that accepts `string appId, string gameName` instead of a Playnite `Game`:

```csharp
public DownloadView(
    string appId,
    string gameName,
    BlankPluginSettings settings,
    InstalledGamesManager installedGamesManager,
    IPlayniteAPI api,
    UpdateChecker updateChecker)
```

Behavior:
- Sets `_resolvedAppId = appId`
- After layout builds: sets `_gameInfoLabel.Text = gameName + "  (AppID: " + appId + ")"`, hides the search panel and resolve status label
- Immediately calls `CheckIfInstalled()` and, if not already installed, kicks off manifest fetch automatically (same code path as clicking the Fetch button)
- The user lands directly on the depot selection screen once the manifest arrives — no manual steps needed

---

## 3. `UI/CatalogView.cs` (new file)

### Layout (top to bottom)

```
[ Credits notice banner ]
[ Search/filter box ]
[ Scrollable WrapPanel of cover cards ]
[ "N games in catalog" summary label ]
```

### Credits notice banner

A muted info bar at the top (yellow/amber tint):

> "Installing a game fetches its manifest and counts as one API credit toward your daily limit."

Shown always — not dismissible.

### Cover cards

- Width: 130px, fixed. Height: auto (image + name + button).
- Steam header image: `https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg` (same URL pattern as `LibraryView`, `DecodePixelWidth = 130`)
- Game name: `FontSize = 11`, `TextTrimming = CharacterEllipsis`, max 2 lines
- Install button (green tint, full card width) OR "Installed" badge (grey, no click) depending on `InstalledGamesManager`

### Load sequence

1. On construction: show "Loading catalog..." spinner text
2. `ThreadPool.QueueUserWorkItem` → `_client.GetLibrary()`
3. On success: cross-reference with `_installedGamesManager.GetAll()` by AppId, build cards, `Dispatch()` to UI thread
4. On failure: show error message with a Retry button

### Filter

Client-side only — no new API calls. `TextBox.TextChanged` filters `_allResults` in memory and repopulates the WrapPanel. Case-insensitive substring match on game name.

### Install click

```csharp
var window = _api.Dialogs.CreateWindow(new WindowCreationOptions { ... });
window.Title = "Install — " + result.GameName;
window.Width = 700;
window.Height = 600;
window.Owner = _api.Dialogs.GetCurrentAppWindow();
window.Content = new DownloadView(result.GameId, result.GameName, _settings, _installedGamesManager, _api, _updateChecker);
window.ShowDialog();
```

After `ShowDialog()` returns, refresh the "Installed" state of that card (the game may now be installed).

---

## 4. `UI/MainTabView.cs` (new file)

A `UserControl` containing a `TabControl` with two `TabItem`s:

| Tab | Content | Created |
|-----|---------|---------|
| Library | `new LibraryView(...)` | Eagerly (on construction) |
| Catalog | `new CatalogView(...)` | Lazily (on first `SelectionChanged` to that tab) |

Lazy creation avoids the network call until the user actually opens the Catalog tab.

---

## 5. `BlankPlugin.cs` change

In `OpenPluginWindow(Game game)`, the `else` branch changes from:

```csharp
window.Content = new LibraryView(Settings, InstalledGames, PlayniteApi, _updateChecker);
```

to:

```csharp
window.Content = new MainTabView(Settings, InstalledGames, PlayniteApi, _updateChecker);
```

No other changes to `BlankPlugin.cs`.

---

## Files to create / modify

| File | Change |
|------|--------|
| `BlankPlugin/source/Api/MorrenusClient.cs` | Add `GetLibrary()` |
| `BlankPlugin/source/UI/DownloadView.cs` | Add `(string appId, string gameName, ...)` constructor |
| `BlankPlugin/source/UI/CatalogView.cs` | New file |
| `BlankPlugin/source/UI/MainTabView.cs` | New file |
| `BlankPlugin/source/BlankPlugin.cs` | 1-line change in `OpenPluginWindow` |

---

## Verification

1. Build succeeds with no errors.
2. Click sidebar button with no game selected → window opens with Library and Catalog tabs.
3. Library tab: behaves exactly as before.
4. Catalog tab: shows "Loading catalog...", then a grid of cover cards.
5. Credits notice banner is visible at the top of the Catalog tab.
6. Filter box: typing narrows the grid in real time.
7. Already-installed games show grey "Installed" badge, not Install button.
8. Clicking Install on an uninstalled game opens a DownloadView window pre-loaded for that AppId (no search step needed).
9. After install completes and dialog closes, that game's card shows "Installed".
10. API failure: error message + Retry button shown instead of grid.
