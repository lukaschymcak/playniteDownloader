# LuDownloader Electron

Standalone Electron + React rewrite of the LuDownloader app.

## Design Source

The renderer is based on `../Windows app..zip` / `../handoff_extract/handoff`:

- `prototype/screens.jsx` for view structure
- `prototype/index.html` for CSS/layout
- `prototype/icons.jsx` for icon paths
- `DESIGN-TOKENS.md` for colors, spacing, typography, and status semantics
- `screenshots/*.png` for visual acceptance

## Commands

```powershell
npm install
npm run dev
npm run build
npm run package
```

## Runtime Data

The Electron app uses the same standalone data root as the WPF app:

```text
%APPDATA%\LuDownloader
```

Existing `settings.json`, `data/installed_games.json`, `data/library_games.json`, `manifest_cache`, and `ludownloader.log` remain compatible.
