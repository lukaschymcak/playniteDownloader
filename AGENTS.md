# AGENTS.md — LuDownloader (Playnite extension)

Guidance for humans and coding agents working in this repository. Read this before non-trivial changes.

---

## 1. Project context

**LuDownloader** is a **Playnite GenericPlugin** (C#, WPF) that:

- Searches and downloads Steam depot content using the **Morrenus** manifest API and **DepotDownloader**.
- Writes Steam-style **`.acf`** app manifests so installs look correct to the Steam client.
- Tracks **plugin-installed games** and **Steam library roots** for context-aware actions (open folder, uninstall, DRM tools, etc.).
- Optionally enriches metadata (IGDB) and checks for **manifest / update** drift via a small **ManifestChecker** helper executable that talks to Steam.

End users install a **`.pext`** produced by the build; logs go to Playnite’s log file.

---

## 2. Tech stack

| Area | Version / note |
|------|----------------|
| **Plugin (main)** | C# 7+ on **.NET Framework 4.6.2** (`net462`) |
| **Playnite SDK** | **6.2.0** (NuGet: `PlayniteSDK`) |
| **UI** | **WPF** (`PresentationFramework`, code-built views; no XAML in plugin UI) |
| **JSON (plugin)** | **Newtonsoft.Json** loaded from Playnite install — **do not ship a private copy** (`HintPath` + `<Private>false</Private>`) |
| **ManifestChecker** | **.NET 9** console app (`net9.0`), **SteamKit2** `3.*`, **Newtonsoft.Json** `13.*`, published **single-file** for `win-x64`, copied into plugin `deps/` at build time |
| **Other NuGet (plugin)** | `System.ValueTuple` **4.5.0** |
| **Runtime expectations** | **.NET 9** on PATH for DepotDownloader / ManifestChecker; **Playnite** installed under `%LOCALAPPDATA%\Playnite\` (Toolbox + shared Newtonsoft) |

Solution entry: `BlankPlugin/source/BlankPlugin.sln` (includes `ManifestChecker`).

---

## 3. Architecture and where files belong

High-level layout:

```
BlankPlugin/source/
  BlankPlugin.cs           ← Plugin entry: menus, sidebar, lifecycle, wiring
  extension.yaml           ← Manifest: Id, Module, Type, Version — must match packaging expectations
  Api/                     ← HTTP/API clients only (Morrenus, Steam web, IGDB, …)
  Models/                  ← DTOs and persisted records (manifest data, installed game, …)
  Pipeline/                ← Download/install/update workflows, shell-outs, file I/O helpers
  Settings/                ← ISettings model + settings UserControl
  UI/                      ← Windows, dialogs, main views (Playnite-hosted WPF)
  deps/                    ← Binary dependencies (DepotDownloader, Steamless, …); ManifestChecker.exe lands here after publish+copy
  Properties/              ← Assembly metadata

ManifestChecker/           ← Separate .NET 9 project; output published and copied into BlankPlugin output deps/
```

**Routing rules:**

- **New REST/API integration** → `Api/` (one client per external service when practical).
- **Domain types and serialization shapes** → `Models/`.
- **Long-running steps** (ZIP extract, DepotDownloader, `.acf`, Steam paths, update checks) → `Pipeline/`.
- **User-facing surfaces** → `UI/`; **plugin settings** → `Settings/`.
- **Do not** scatter HTTP calls inside `UI/` — call into `Api/` or `Pipeline/` from UI/event handlers.

**Data flow (download):** UI search → `MorrenusClient` → manifest ZIP → `ZipProcessor` → depot selection → `DepotDownloaderRunner` → `AcfWriter` → persist via managers.

**Update checking:** `UpdateChecker` + `ManifestCheckerRunner` runs `deps/ManifestChecker.exe` (built from `ManifestChecker/`).

---

## 4. Coding conventions

- **Match existing style** in the file you edit (naming, `logger` usage, async patterns).
- **Playnite windows:** always create UI hosts via `PlayniteApi.Dialogs.CreateWindow()` — never `new Window()` for Playnite-owned UI.
- **Database mutations:** after changing a `Game`, call `PlayniteApi.Database.Games.Update(game)`; use `BufferedUpdate()` for bulk changes.
- **Settings:** implement `ISettings` with `BeginEdit` / `CancelEdit` / `EndEdit`; persist in `EndEdit` via `plugin.SavePluginSettings(this)`; load in constructor with `plugin.LoadPluginSettings<T>()`.
- **Binary deps:** keep third-party binaries under `BlankPlugin/source/deps/` and listed in `.csproj` with `CopyToOutputDirectory` as needed; don’t duplicate large DLLs in random folders.
- **ManifestChecker:** keep it as a small CLI with **stdout/stderr JSON or clear errors**; the plugin only shells out — avoid coupling SteamKit logic into the net462 plugin.
- **Threading:** don’t block the Playnite UI thread on network or subprocess I/O; follow patterns already used in `Pipeline/` and `UI/`.

---

## 5. Strict prohibitions

- **NEVER** reference or copy **Newtonsoft.Json** into the plugin output as a private dependency — use Playnite’s DLL only.
- **NEVER** instantiate top-level WPF `Window` for plugin UI without Playnite’s factory — use `PlayniteApi.Dialogs.CreateWindow()`.
- **NEVER** commit real **API keys**, **passwords**, or **session tokens** — settings are user-local; use placeholders in docs and samples.
- **NEVER** change `extension.yaml`’s **`Id`**, **`Module`**, or **`Type`** casually — they define extension identity and load behavior; keep **`Module`** aligned with `AssemblyName`.
- **NEVER** replace the **ManifestChecker** build/copy pipeline without ensuring **`ManifestChecker.exe`** in **`deps/`** is still a **published** single-file (or equivalent) that actually runs — copying only a small bootstrapper EXE without its DLL breaks at runtime.
- **NEVER** assume **automated tests** exist — verification is build + manual load in Playnite unless you add tests.

---

## 6. Testing and verification

There is **no** `npm test` or bundled unit-test project today. After code changes, agents should:

1. **Rebuild the solution** (from repo root or `BlankPlugin/source/`):

   ```bash
   dotnet build BlankPlugin/source/BlankPlugin.sln -c Debug
   ```

   Use `-c Release` for release-style verification.

2. **Confirm outputs:**
   - Plugin assembly: `BlankPlugin/source/bin/<Configuration>/net462/BlankPlugin.dll`
   - Packaged extension: `BlankPlugin/source/bin/lukas_BlankPlugin_Plugin_1_0_0.pext` (after successful `PackPlugin` / Toolbox step)
   - `deps/ManifestChecker.exe` present under the same `net462` output folder after build.

3. **Manual smoke test (recommended):** install or reload the `.pext` in Playnite and exercise the changed path; check `%APPDATA%\Playnite\playnite.log` for errors.

**Environment:** `PackPlugin` invokes `%LOCALAPPDATA%\Playnite\Toolbox.exe` — a local Playnite install is required for packaging to succeed.

---

## 7. Quick checklist before merging non-trivial work

- [ ] Build succeeds (`dotnet build` on solution).
- [ ] `extension.yaml` still consistent with plugin type and assembly.
- [ ] No accidental private copy of Newtonsoft.Json.
- [ ] New UI uses Playnite dialog/window APIs.
- [ ] User-facing strings and behavior match the feature; no secrets in repo.

---

## 8. Example stub (reference shape)

This section mirrors the kind of file many teams call `AGENTS.md` — short and scannable:

```markdown
## Project
Playnite plugin: Morrenus downloads + DepotDownloader + Steam library helpers.

## Stack
net462 + PlayniteSDK 6.2.0; ManifestChecker net9.0 + SteamKit2.

## Put code in
Api/ HTTP, Pipeline/ workflows, UI/ views, Models/ data.

## Rules
Playnite dialogs only; Playnite’s Newtonsoft.Json only; update Games through the database API.

## Never
Ship Json.NET; break ManifestChecker publish-to-deps; commit secrets.

## Verify
dotnet build BlankPlugin/source/BlankPlugin.sln -c Debug
```

For this repo, use **sections 1–7** above as the source of truth; section 8 is only a minimal alternate layout.
