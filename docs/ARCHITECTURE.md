# LuDownloader Architecture Notes

LuDownloader is now organized around the standalone WPF app as the primary product.

- `LuDownloader.App` owns desktop startup, app-local settings, file logging, host services, and app lifecycle.
- `LuDownloader.Core` contains shared implementation: API clients, models, pipeline helpers, persistence managers, reusable WPF views, settings shape, logging abstractions, and host abstractions.
- `ManifestChecker` is a separate .NET 9 helper executable. The app shells out to `deps/ManifestChecker.exe` to query Steam manifest GIDs through SteamKit2.
- `deps/` at the repo root is the standalone app dependency bundle. `LuDownloader.App` copies this folder into its output as `deps/`.
- `BlankPlugin/` is legacy Playnite plugin code. It is not part of the standalone app solution or dependency path.

## Current Namespace Compromise

Many shared classes in `LuDownloader.Core` still use the `BlankPlugin` namespace. This is historical naming from the original Playnite plugin and is intentionally left in place for now to avoid mixing a broad mechanical rename with app-only cleanup.

Future namespace cleanup should be done as its own refactor:

- Move domain/API/pipeline/shared abstractions toward `LuDownloader.Core`.
- Move reusable WPF views toward `LuDownloader.UI` if a separate UI project is introduced.
- Keep standalone host classes in `LuDownloader.App`.
- Do not change persisted JSON property names during namespace cleanup.

## Build And Packaging

Use the root app-only solution as the main verification command:

```powershell
dotnet build LuDownloader.sln -c Debug
```

Expected Debug outputs:

- `LuDownloader.App/bin/x64/Debug/net9.0-windows/win-x64/LuDownloader.exe`
- `LuDownloader.App/bin/x64/Debug/net9.0-windows/win-x64/LuDownloader.Core.dll`
- `LuDownloader.App/bin/x64/Debug/net9.0-windows/win-x64/deps/ManifestChecker.exe`
- `LuDownloader.App/bin/x64/Debug/net9.0-windows/win-x64/deps/DepotDownloader.dll`

Direct app project builds are also supported:

```powershell
dotnet build LuDownloader.App/LuDownloader.App.csproj -c Debug
```

The app project copies binary dependencies from root `deps/`. It no longer references `BlankPlugin/source/deps`.

## Runtime Data

Standalone runtime data remains under:

```text
%APPDATA%\LuDownloader
```

Important files:

- `settings.json`
- `ludownloader.log`
- `data/installed_games.json`
- `data/library_games.json`
- `manifest_cache/<appid>.zip`
- `manifest_cache/<appid>.meta.json`

Do not migrate or rewrite these files during build/layout cleanup.

## Update Checks

The standalone app runs update checks silently:

- once after the main window loads
- when the Library tab `Check Updates` button is pressed
- after update/install flows request a status refresh

`UpdateChecker` raises `StatusChanged`; `LibraryView` refreshes the cards from the in-memory status cache. The standalone host sets `ShowUpdateNotifications` to `false`, so startup/button update checks do not show popup windows. Failures are logged and the UI refreshes to the best available status.

## Dependency Rules

No new dependencies are required for the current app split. Prefer BCL, existing project references, and existing abstractions (`IAppHost`, `IDialogService`, logger abstractions) before adding packages.

The legacy Playnite plugin, if maintained separately, must continue using Playnite's bundled `Newtonsoft.Json.dll`. That rule does not constrain the standalone app, which uses its own NuGet Newtonsoft.Json dependency.
