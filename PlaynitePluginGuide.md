# Playnite Plugin Development Guide

Based on analysis of **BlankPlugin** (our base) and **DuplicateHider** (a real-world reference plugin), plus observation of the **SuccessStory** installed extension.

---

## 1. Prerequisites

- **Visual Studio 2022** (or VS Code with C# extension)
- **.NET Framework 4.6.2** SDK
- **Playnite** installed at `%LOCALAPPDATA%\Playnite\`
- **Toolbox.exe** — ships with Playnite, used to pack `.pext` files

---

## 2. Project Structure

```
MyPlugin/
└── source/
    ├── MyPlugin.sln
    ├── MyPlugin.csproj
    ├── MyPlugin.cs              ← Main plugin class
    ├── extension.yaml           ← Plugin manifest
    ├── icon.png                 ← Plugin icon
    ├── Properties/
    │   └── AssemblyInfo.cs
    ├── Views/                   ← Optional: XAML windows/views
    ├── ViewModels/              ← Optional: MVVM ViewModels
    └── bin/                     ← Build output + packed .pext
```

---

## 3. The `.csproj` File (SDK-style, recommended)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <OutputType>Library</OutputType>
    <RootNamespace>MyPlugin</RootNamespace>
    <AssemblyName>MyPlugin</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

  <!-- Playnite SDK via NuGet -->
  <ItemGroup>
    <PackageReference Include="PlayniteSDK" Version="6.2.0" />
  </ItemGroup>

  <!-- WPF references (needed for any UI) -->
  <ItemGroup>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.Xaml" />
  </ItemGroup>

  <!-- Copy manifest and icon to output -->
  <ItemGroup>
    <None Include="extension.yaml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="icon.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <!-- Auto-pack after build using Toolbox.exe -->
  <Target Name="PackPlugin" AfterTargets="Build">
    <Exec Command="%25LOCALAPPDATA%25\Playnite\Toolbox.exe pack $(ProjectDir)$(OutDir) $(ProjectDir)bin" />
  </Target>

</Project>
```

> **Note:** DuplicateHider uses the old `.csproj` format (non-SDK style). The SDK-style above is simpler and is what BlankPlugin uses.

---

## 4. The `extension.yaml` Manifest

```yaml
Id: author_PluginName_Plugin      # Must be globally unique — use author prefix
Name: MyPlugin
Author: YourName
Version: 1.0.0
Module: MyPlugin.dll              # Must match AssemblyName
Type: GenericPlugin               # GenericPlugin is the most common type
Icon: icon.png
```

**Plugin types:**
- `GenericPlugin` — general purpose, most plugins use this
- `LibraryPlugin` — adds a game library source (Steam, GOG, etc.)
- `MetadataPlugin` — provides game metadata
- `GameController` — custom game launch/stop logic

**The `Id` field is critical.** It uniquely identifies your plugin to Playnite. Use the format `author_PluginName_Plugin` and generate a matching `Guid` in your C# code. They don't have to match character-for-character, but the YAML Id and the C# `Guid` are both registered independently — keep them consistent.

---

## 5. The Main Plugin Class

```csharp
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MyPlugin
{
    public class MyPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // Must match the Id in extension.yaml (as a Guid)
        public override Guid Id { get; } = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        public MyPlugin(IPlayniteAPI api) : base(api)
        {
            // HasSettings = true if you implement a settings page
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        // Called once when Playnite starts
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args) { }

        // Called once when Playnite is closing
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) { }
    }
}
```

`IPlayniteAPI` (stored as `PlayniteApi` from the base class) gives you access to everything:

| `PlayniteApi.` | What it does |
|---|---|
| `Database.Games` | Full game library (enumerate, get, update) |
| `Database.Platforms` | All platforms |
| `Database.Sources` | All library sources |
| `Database.Tags` | Create/find tags |
| `Dialogs` | Show messages, file pickers, create windows |
| `Resources` | Theme resources/styles |
| `Notifications` | Push notification messages |
| `UriHandler` | Register custom URI handlers |
| `MainView` | Access to the main Playnite window state |

---

## 6. Opening a Window

Use `PlayniteApi.Dialogs.CreateWindow()` — this creates a properly themed Playnite window:

```csharp
private void OpenMyWindow()
{
    var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
    {
        ShowMinimizeButton = false,
        ShowMaximizeButton = false,
        ShowCloseButton = true
    });

    window.Title = "My Plugin Window";
    window.Width = 800;
    window.Height = 600;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

    // Set content — can be any WPF control or a UserControl
    window.Content = new MyView();  // or inline WPF controls
    window.ShowDialog();            // blocks until closed
}
```

> **Do NOT** use `new Window()` directly — it won't inherit Playnite's theme styles.

---

## 7. Adding a Sidebar Item

Sidebar items appear in the left panel of Playnite's main window.

```csharp
public override IEnumerable<SidebarItem> GetSidebarItems()
{
    yield return new SidebarItem
    {
        Title = "My Plugin",
        Icon = new TextBlock { Text = "\uEF6B" }, // or a path/icon
        Type = SiderbarItemType.Button,           // Button or View
        Activated = () => OpenMyWindow()
    };
}
```

For a **panel view** embedded in the sidebar instead of opening a window:

```csharp
new SidebarItem
{
    Title = "My Plugin",
    Icon = "MP",
    Type = SiderbarItemType.View,
    Opened = () =>
    {
        // Return a UserControl shown in the sidebar panel
        return new MyPanelView();
    }
}
```

---

## 8. Adding Game Context Menu Items

Right-click menu on games:

```csharp
public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
{
    yield return new GameMenuItem
    {
        MenuSection = "My Plugin",           // Groups items under a submenu
        Description = "Do Something",
        Action = menuArgs =>
        {
            var games = menuArgs.Games;      // List<Game> of selected games
            foreach (var game in games)
            {
                logger.Info($"Acting on: {game.Name}");
            }
        }
    };
}
```

---

## 9. Adding Main Menu Items

Top-level Playnite menu (Extensions menu):

```csharp
public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
{
    yield return new MainMenuItem
    {
        MenuSection = "@My Plugin",       // @ prefix puts it under Extensions
        Description = "Open My Plugin",
        Action = _ => OpenMyWindow()
    };
}
```

---

## 10. Settings

### Step 1 — Settings class

```csharp
public class MyPluginSettings : ObservableObject, ISettings
{
    private readonly MyPlugin plugin;

    // Your settings properties
    public bool MySetting { get; set; } = true;
    public string MyString { get; set; } = "default";

    public MyPluginSettings() { }  // Parameterless constructor required

    public MyPluginSettings(MyPlugin plugin)
    {
        this.plugin = plugin;
        var saved = plugin.LoadPluginSettings<MyPluginSettings>();
        if (saved != null)
        {
            MySetting = saved.MySetting;
            MyString = saved.MyString;
        }
    }

    public void BeginEdit() { /* Called when settings window opens */ }
    public void CancelEdit() { /* Called when user clicks Cancel */ }

    public void EndEdit()
    {
        plugin.SavePluginSettings(this);  // Persist to disk
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        return true;  // Return false to block saving
    }
}
```

### Step 2 — Settings view (XAML UserControl)

```xml
<!-- MyPluginSettingsView.xaml -->
<UserControl x:Class="MyPlugin.MyPluginSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="10">
        <CheckBox Content="Enable my setting" IsChecked="{Binding MySetting}" />
        <TextBox Text="{Binding MyString}" />
    </StackPanel>
</UserControl>
```

### Step 3 — Wire up in the plugin class

```csharp
public class MyPlugin : GenericPlugin
{
    private MyPluginSettings settings;

    public MyPlugin(IPlayniteAPI api) : base(api)
    {
        settings = new MyPluginSettings(this);
        Properties = new GenericPluginProperties { HasSettings = true };
    }

    public override ISettings GetSettings(bool firstRunSettings) => settings;

    public override UserControl GetSettingsView(bool firstRunSettings)
        => new MyPluginSettingsView { DataContext = settings };
}
```

---

## 11. Reacting to Game Events

```csharp
// Game added to library
public override void OnGameInstalled(OnGameInstalledEventArgs args) { }
public override void OnGameUninstalled(OnGameUninstalledEventArgs args) { }
public override void OnGameStarting(OnGameStartingEventArgs args) { }
public override void OnGameStarted(OnGameStartedEventArgs args) { }
public override void OnGameStopped(OnGameStoppedEventArgs args) { }

// Library updated (games added/removed from database)
public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) { }
```

---

## 12. Accessing the Game Database

```csharp
// Iterate all games
foreach (var game in PlayniteApi.Database.Games)
{
    logger.Info(game.Name);
}

// Get a specific game by Guid
var game = PlayniteApi.Database.Games.Get(someGuid);

// Modify a game (must use BeginBufferUpdate for bulk operations)
using (PlayniteApi.Database.BufferedUpdate())
{
    foreach (var game in PlayniteApi.Database.Games)
    {
        game.Tags.Add(myTagId);
        PlayniteApi.Database.Games.Update(game);
    }
}

// Create/find a tag
var tag = PlayniteApi.Database.Tags.Add("My Tag");  // returns existing if name matches
```

---

## 13. Custom Theme-Integrated Controls (Advanced)

DuplicateHider registers custom controls that theme designers can embed in their themes:

```csharp
// In constructor:
AddCustomElementSupport(new AddCustomElementSupportArgs
{
    ElementList = new List<string> { "MyControl" },
    SourceName = "MyPlugin"
});

// Then override:
public override Control GetGameViewControl(GetGameViewControlArgs args)
{
    if (args.Name == "MyControl")
        return new MyCustomControl();
    return null;
}
```

Themes reference these as `<ContentControl x:Name="MyPlugin_MyControl" />`.

---

## 14. Building and Installing

### Build
In Visual Studio: **Build → Build Solution** (or `Ctrl+Shift+B`)

The `PackPlugin` MSBuild target runs `Toolbox.exe pack` automatically after a successful build, producing a `.pext` file in `source/bin/`.

### Install for development
Double-click the `.pext` file, or copy the build output folder directly into:
```
%APPDATA%\Playnite\Extensions\YourPluginId\
```

### Logs
Playnite writes plugin logs to:
```
%APPDATA%\Playnite\playnite.log
```

Use `LogManager.GetLogger()` in your plugin and call `logger.Info(...)`, `logger.Warn(...)`, `logger.Error(...)`.

---

## 15. Quick Checklist for a New Plugin

- [ ] Generate a new `Guid` for your plugin (`Guid.NewGuid()` or use VS Tools → Create GUID)
- [ ] Set `Id` in `extension.yaml` to a unique string (`author_Name_Plugin`)
- [ ] Set `Guid Id` in your plugin class to match uniqueness (can be different format)
- [ ] Set `Module:` in `extension.yaml` to match your `AssemblyName`
- [ ] Set `HasSettings = true` only if you implement `GetSettings()` and `GetSettingsView()`
- [ ] Never use `new Window()` — always use `PlayniteApi.Dialogs.CreateWindow()`
- [ ] Always call `PlayniteApi.Database.Games.Update(game)` after modifying a game object
- [ ] Target `net462` (Playnite's runtime is .NET Framework 4.6.2)
