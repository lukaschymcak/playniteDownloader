# Pre-ship audit — BlankPlugin + ManifestChecker

**Date:** 2026-04-13  
**Scope:** [AGENTS.md](../AGENTS.md), [.cursor/rules/engineering-principles.mdc](../.cursor/rules/engineering-principles.mdc), architect audit plan (read-only review).  
**Automated gate:** `dotnet build BlankPlugin/source/BlankPlugin.sln -c Debug` — **succeeded, 0 errors** (ManifestChecker: 3x CS8602 warnings). Release build: **succeeded, 0 errors**.  
**Artifacts:** `BlankPlugin/source/bin/Debug/net462/deps/ManifestChecker.exe` **present** after build.  
**Packaging:** `%LOCALAPPDATA%\Playnite\Toolbox.exe` **exists** on audit machine — `PackPlugin` target is plausible; full `.pext` pack not executed in this pass.

---

## Blockers

| ID | Finding | Evidence |
|----|---------|----------|
| **B1** | **`GoldbergArchDialog` always uses raw WPF `Window`, not `PlayniteApi.Dialogs.CreateWindow`** — violates AGENTS.md / Playnite theming and host-window rules. | [GoldbergArchDialog.cs](../BlankPlugin/source/UI/GoldbergArchDialog.cs) lines 81-94: `ShowPicker` constructs `new Window`. Call sites: [DownloadView.cs](../BlankPlugin/source/UI/DownloadView.cs) ~1298, [BlankPlugin.cs](../BlankPlugin/source/BlankPlugin.cs) ~208. |
| **B2** | **Steam password written to a `.bat` file and passed on DepotDownloader command line** — credential exposure on disk and in process listing; high severity for any shared machine or forensics. | [DepotDownloaderRunner.cs](../BlankPlugin/source/Pipeline/DepotDownloaderRunner.cs) `Authenticate`: `File.WriteAllText(batPath, ... -password {3} ...)` lines 148-165; batch executed via `cmd.exe /C` (lines 170-174). |

---

## Should-fix

| ID | Finding | Evidence |
|----|---------|----------|
| **S1** | **Fallback `new Window` when `IPlayniteAPI` is null** in several pickers — same AGENTS violation as B1 if that path is ever hit from Playnite (e.g. tests or refactors). Prefer always passing `_api` or failing fast. | [SteamLibraryPickerDialog.cs](../BlankPlugin/source/UI/SteamLibraryPickerDialog.cs) 157-164; [IgdbMetadataPickerDialog.cs](../BlankPlugin/source/UI/IgdbMetadataPickerDialog.cs) 297-304; [IgdbBackgroundPickerDialog.cs](../BlankPlugin/source/UI/IgdbBackgroundPickerDialog.cs) (same pattern ~224). |
| **S2** | **`UpdateChecker.Cancel()` does not stop an in-flight `ManifestChecker.exe`** — `CancellationToken` is not passed into `ManifestCheckerRunner.Run`; shutdown can wait up to **30s** per stuck run. | [UpdateChecker.cs](../BlankPlugin/source/Pipeline/UpdateChecker.cs) `Cancel` only cancels token; [ManifestCheckerRunner.cs](../BlankPlugin/source/Pipeline/ManifestCheckerRunner.cs) `Run` has no `CancellationToken`, uses `WaitForExit(30_000)`. |
| **S3** | **`BlankPluginSettings`: `BeginEdit` / `CancelEdit` are no-ops** — edits apply live to the same object; **Cancel in Playnite settings does not roll back** UI changes already written to properties. | [BlankPluginSettings.cs](../BlankPlugin/source/Settings/BlankPluginSettings.cs) lines 102-103. |
| **S4** | **`Generate_emu_config` invoked with unquoted `appId` in `ProcessStartInfo.Arguments`** — unusual AppIds or future argument-injection mistakes could break or confuse the child process. | [GoldbergRunner.cs](../BlankPlugin/source/Pipeline/GoldbergRunner.cs) line 191: `"-acw " + appId` (should validate numeric AppId and/or quote). |
| **S5** | **Sync-over-async `.Result` in HTTP clients** — safe when called only from ThreadPool/worker threads; **any future call from UI thread risks deadlock**. Document contract or migrate to async. | [MorrenusClient.cs](../BlankPlugin/source/Api/MorrenusClient.cs), [SteamApiClient.cs](../BlankPlugin/source/Api/SteamApiClient.cs), [IgdbClient.cs](../BlankPlugin/source/Api/IgdbClient.cs), [CoverDownloader.cs](../BlankPlugin/source/Pipeline/CoverDownloader.cs), [ManifestCheckerRunner.cs](../BlankPlugin/source/Pipeline/ManifestCheckerRunner.cs) lines 75-76. |
| **S6** | **Empty / minimal `catch` blocks** — acceptable for best-effort cleanup; weak for debugging. Prefer `logger` at `Debug`/`Trace` where hot. | Examples: [MorrenusClient.cs](../BlankPlugin/source/Api/MorrenusClient.cs) ~206 (parse fallback); [LibraryView.cs](../BlankPlugin/source/UI/LibraryView.cs) ~684-686, ~705; [ManifestCheckerRunner.cs](../BlankPlugin/source/Pipeline/ManifestCheckerRunner.cs) ~86; [DownloadView.cs](../BlankPlugin/source/UI/DownloadView.cs) ~1724 (`GetTotalBytesReceived`). |
| **S7** | **ManifestChecker nullable warnings (CS8602)** — possible null deref at runtime under odd Steam callbacks. | [ManifestChecker/Program.cs](../ManifestChecker/Program.cs) lines 43, 113, 172. |

### Task 2 checkpoint — `Games.Update` / `BufferedUpdate`

- **Playnite `Game` mutations reviewed:** [DownloadView.cs](../BlankPlugin/source/UI/DownloadView.cs) `ApplyIgdbMetadata` mutates `game` then **`_api.Database.Games.Update(game)`** at line **1561**. Steam CDN-only path: sets `newGame.CoverImage` then **`Update(newGame)`** at **1110**. **`Games.Add`** for library-only install at **1055**.
- **No other `Games.Update` / `BufferedUpdate` callsites** in repo grep — **no missed update** found for traced IGDB/metadata paths. [LibraryView.cs](../BlankPlugin/source/UI/LibraryView.cs) uses `Games.Get` read-only and `Games.Remove` (no update needed).

---

## Nice-to-have

| ID | Finding | Evidence |
|----|---------|----------|
| **N1** | **`DownloadView.cs` is a god-class** (~1.8k+ lines) — SRP / maintainability; split by workflow (resolve / fetch / download / post-process) in a future refactor branch. | [DownloadView.cs](../BlankPlugin/source/UI/DownloadView.cs) |
| **N2** | **Product naming:** UI strings / sidebar still **"BlankPlugin"** while docs use **LuDownloader**. | [BlankPlugin.cs](../BlankPlugin/source/BlankPlugin.cs) sidebar, menus; [extension.yaml](../BlankPlugin/source/extension.yaml) `Name: BlankPlugin`. |
| **N3** | **Repo hygiene:** `bin/` / `obj` / build outputs should be **gitignored** — avoids noise and accidental binary commits. | Git status historically showed many `bin`/`obj` files. |
| **N4** | **`MessageBox` / `System.Windows.MessageBox` instead of `PlayniteApi.Dialogs`** in places — inconsistent with Playnite UX patterns. | e.g. [DownloadView.cs](../BlankPlugin/source/UI/DownloadView.cs) metadata prompt ~1065; [LibraryView.cs](../BlankPlugin/source/UI/LibraryView.cs) uninstall ~716. |

---

## Threading risk table (Task 3 checkpoint)

| Area | Caller thread | `.Result` / blocking HTTP? | Risk |
|------|----------------|----------------------------|------|
| `MorrenusClient` | `DownloadView` worker `Thread` / `ThreadPool`, `UpdateWindow` ThreadPool, `LibraryView` ThreadPool | Yes | **Low** if contract preserved |
| `SteamApiClient` | `SearchView` `ThreadPool.QueueUserWorkItem` | Yes | **Low** |
| `IgdbClient` | `DownloadView` worker after picker; metadata/download paths | Yes | **Low** |
| `CoverDownloader` | Invoked from background install path | Yes | **Low** |
| `ManifestCheckerRunner.Run` | `UpdateChecker` to `Task.Run` | Yes (`stdoutTask.Result`) | **Low** (background) |
| `RefreshApiStatus` | `DownloadView` / `LibraryView` ThreadPool | Uses Morrenus `.Result` | **Low** |

**UpdateChecker:** `RunAsync` uses `_runLock` try-wait-0 to avoid overlap; **`Cancel()`** only affects token for notification loop — **does not kill ManifestChecker process** (see S2).

---

## Security / subprocess notes (Task 4)

| Component | Negative scenario | Expected behavior |
|-----------|-------------------|-------------------|
| `DepotDownloaderRunner` | Missing `dotnet` / DLL | `IsReady` false; user message via `onLog` |
| `DepotDownloaderRunner.Authenticate` | User cancels / Steam Guard fails | Console window; user-driven; bat deleted in `TryDelete` |
| `SteamlessRunner` | No EXEs / DRM free | Logs warning or "No Steam DRM" |
| `GoldbergRunner` | Missing `generate_emu_config.exe` | `IsReady` false; ERROR log |
| `ManifestCheckerRunner` | Missing exe | `(null, "ManifestChecker.exe not found...")` |
| `ManifestCheckerRunner` | Timeout 30s | Kill process; error string |
| `ManifestChecker` CLI | Invalid AppId | JSON `{"error":"Invalid AppID: ..."}` stderr, exit 1 |

**ManifestChecker args:** `string.Join(" ", appIds)` — AppIds are expected numeric; validate no whitespace in each id to avoid accidental extra argv tokens.

---

## Layering and engineering principles (Task 5)

- **HTTP:** No `HttpClient` / `GetAsync` in `UI/*.cs` grep — **good**; network isolated to `Api/` and `Pipeline/CoverDownloader.cs`.
- **`Debug.WriteLine`:** **No matches** in `BlankPlugin/source` — good vs engineering-principles.
- **Grep housekeeping (checkpoint 5):**
  - `new Window` in `BlankPlugin/source`: **4 files** (Goldberg + 3 fallbacks) — triaged above.
  - `catch` with empty or minimal body: **multiple** — triaged S6.
  - `Debug.WriteLine`: **0** hits.

---

## Manual smoke checklist (optional)

1. Install/reload `.pext` in Playnite.  
2. Open sidebar to main plugin window.  
3. Search tab: Steam API search.  
4. Download flow: Morrenus fetch and depot download.  
5. Settings: save/cancel behavior (note S3).

---

## Summary counts

- **Blockers:** 2  
- **Should-fix:** 7  
- **Nice-to-have:** 4  
