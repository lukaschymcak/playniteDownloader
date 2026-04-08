# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Communication style

Talk like caveman. Short. Direct. Save tokens. No big words. No long explain. Just do thing.

## What this project is

A **Playnite GenericPlugin** that downloads Steam games via the Morrenus manifest API and DepotDownloader. It also detects and tracks Steam library installations for context-aware in-library actions.

## Building

Open `BlankPlugin/source/BlankPlugin.sln` in **Visual Studio 2022** and build with `Ctrl+Shift+B`.

After a successful build, the `PackPlugin` MSBuild target automatically runs:
```
%LOCALAPPDATA%\Playnite\Toolbox.exe pack <OutDir> BlankPlugin/source/bin/
```
This produces the installable `.pext` file in `BlankPlugin/source/bin/`.

There are no automated tests — verification is done by loading the `.pext` into Playnite.

**Requirements:**
- .NET Framework 4.6.2 SDK (target framework `net462`)
- Playnite installed at `%LOCALAPPDATA%\Playnite\` (for Toolbox.exe and Newtonsoft.Json)
- .NET 9 runtime on PATH (for DepotDownloader at runtime)

## Installing for development

Double-click the `.pext`, or copy the build output folder to:
```
%APPDATA%\Playnite\Extensions\lukas_BlankPlugin_Plugin_1_0_0\
```

Logs appear in `%APPDATA%\Playnite\playnite.log`.

## Architecture

```
BlankPlugin.cs              ← Plugin entry point; wires sidebar items, game context menus, settings
Api/MorrenusClient.cs       ← HTTP client for manifest.morrenus.xyz/api/v1 (search, download ZIP)
Models/GameData.cs          ← Parsed manifest data (AppId, depots, manifests, keys)
Models/InstalledGame.cs     ← Persisted record of a plugin-installed game
Pipeline/
  ZipProcessor.cs           ← Extracts manifest ZIP → individual .manifest files
  AcfWriter.cs              ← Writes Steam .acf appmanifest so Steam recognizes the install
  DepotDownloaderRunner.cs  ← Shells out to DepotDownloader.dll via dotnet; streams progress
  SteamDrmChecker.cs        ← Detects Steam DRM on EXEs
  SteamlessRunner.cs        ← Shells out to Steamless.CLI.dll to strip DRM
  InstalledGamesManager.cs  ← Reads/writes installed_games.json in plugin data dir
  SteamLibraryHelper.cs     ← Parses libraryfolders.vdf to find Steam library paths
Settings/
  BlankPluginSettings.cs    ← ISettings: API key, Steam username, install path, max downloads
  BlankPluginSettingsView.cs← Settings UI (code-behind UserControl, no XAML)
UI/
  GameWindow.cs             ← Main download workflow window (search → select depots → download)
  InstalledGamesPanel.cs    ← Sidebar panel listing plugin-installed games
  SteamLibraryPickerDialog.cs ← Dialog for picking a Steam library folder as install destination
```

### Key data flows

**Download flow:** User searches in `GameWindow` → `MorrenusClient.SearchGames()` → select result → `MorrenusClient.DownloadManifest()` → `ZipProcessor` extracts depot manifests → user selects depots → `DepotDownloaderRunner.Run()` per depot → `AcfWriter` writes `.acf` → `InstalledGamesManager.Save()`.

**Steam library detection:** `SteamLibraryHelper` reads `libraryfolders.vdf` to enumerate Steam library roots. `InstalledGamesManager` scans for existing installs. `BlankPlugin.GetGameMenuItems()` cross-references Playnite game GUIDs and Steam AppIds against `InstalledGamesManager` to show context-aware menu items (Open Folder, Uninstall, Strip DRM).

### Playnite-specific rules

- Always use `PlayniteApi.Dialogs.CreateWindow()` — never `new Window()`.
- `Newtonsoft.Json` is referenced from Playnite's install dir with `<Private>false</Private>` so our own copy is never shipped.
- All binary dependencies (`DepotDownloader.dll`, `SteamKit2.dll`, `Steamless.CLI.dll`, etc.) live in `BlankPlugin/source/deps/` and are copied to output by the `.csproj`.
- Always call `PlayniteApi.Database.Games.Update(game)` after modifying a game object.
- Use `PlayniteApi.Database.BufferedUpdate()` for bulk game modifications.

### Playnite SDK surface area

`PlayniteApi` (the `IPlayniteAPI` instance from the base class) provides:

| `PlayniteApi.` | Purpose |
|---|---|
| `Database.Games` | Full game library — enumerate, get, update |
| `Database.Tags` | Create/find tags (`Tags.Add("name")` returns existing if name matches) |
| `Database.Platforms` / `Sources` | Platform and source records |
| `Dialogs` | `CreateWindow()`, file pickers, `GetCurrentAppWindow()` |
| `Notifications` | Push notification messages |
| `MainView` | Main window state |

### extension.yaml and plugin identity

The manifest at `BlankPlugin/source/extension.yaml` must stay in sync with the plugin class:

- `Id:` — unique string in format `author_PluginName_Plugin`; registered independently from the C# `Guid`
- `Module:` — must match `AssemblyName` in the `.csproj`
- `Type: GenericPlugin` — other options: `LibraryPlugin`, `MetadataPlugin`, `GameController`

The C# `Guid` in the plugin class (`Guid.Parse(...)`) and the YAML `Id` are registered independently by Playnite — keep them consistent but they don't need to be the same string format.

### Sidebar items

Two patterns for `SidebarItem`:
- `Type = SiderbarItemType.Button` + `Activated = () => ...` — opens a window on click
- `Type = SiderbarItemType.View` + `Opened = () => new MyPanel()` — embeds a `UserControl` in the sidebar panel

### Menu items

- `GetGameMenuItems` — right-click context menu on games; use `MenuSection = "BlankPlugin"` to group under a submenu
- `GetMainMenuItems` — Extensions menu; prefix `MenuSection` with `@` to place under the Extensions top-level menu

### Settings pattern

Settings classes implement `ISettings` and `ObservableObject`. The three lifecycle methods are:
- `BeginEdit()` — called when the settings window opens (snapshot current values if cancel support is needed)
- `CancelEdit()` — called on Cancel
- `EndEdit()` — called on OK; call `plugin.SavePluginSettings(this)` here

Load saved settings in the constructor with `plugin.LoadPluginSettings<T>()`.

### Game lifecycle events

Override in the plugin class as needed:
```
OnGameInstalled / OnGameUninstalled
OnGameStarting / OnGameStarted / OnGameStopped
OnLibraryUpdated   ← fires after Playnite refreshes the game database
```

## Memory rules

- **Never modify existing code** without first asking and creating a backup. Only add new code.
