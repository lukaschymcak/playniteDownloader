# Playnite Downloader Plugin — CLAUDE.md

## Project Overview

A Playnite Generic Plugin (C# / WPF / .NET 6) that lets users search for game downloads from community repack sources (hydralinks.cloud-compatible JSON), resolve links via Real-Debrid or direct HTTP, download, extract, and automatically register the game as installed in Playnite.

Spec: `docs/superpowers/specs/2026-04-06-playnite-downloader-plugin-design.md`
Plan: `docs/superpowers/plans/2026-04-06-playnite-downloader-plugin.md`

---

## Tech Stack

- C# / .NET 6 / WPF (Windows only — `net6.0-windows`)
- xUnit + Moq for tests
- SharpCompress — archive extraction (zip, rar, 7z)
- FuzzySharp — fuzzy title matching
- Newtonsoft.Json — JSON serialisation (Playnite standard)
- PlayniteSDK 6.x — Playnite plugin API

---

## Architecture

Layered, modular. Each layer has one responsibility and communicates through interfaces.

```
PlayniteDownloaderPlugin (GenericPlugin)
├── UI Layer          — WPF controls (SidePanel, SearchDialog)
├── Source Layer      — ISourceProvider, JsonSourceProvider, SourceManager
├── Download Layer    — IDownloader, RealDebridClient, HttpDownloader, DownloaderFactory
├── Pipeline Layer    — DownloadQueue, ExtractionService, InstallResolver
└── Integration Layer — PlayniteIntegration (marks game as installed)
```

Business logic lives in pure C# classes. WPF code-behind is minimal — only wiring.

---

## Project Structure

```
PlayniteDownloaderPlugin/
├── Models/           — data classes (no logic)
├── Source/           — source search
├── Download/         — HTTP + Real-Debrid downloading
├── Pipeline/         — queue, extraction, exe detection
├── Integration/      — Playnite game record updates
└── UI/               — ViewModels + XAML

PlayniteDownloaderPlugin.Tests/
├── Source/
├── Download/
├── Pipeline/
└── Integration/
```

---

## Coding Standards

### Simplicity over cleverness

Write code that a beginner can read from top to bottom and understand. If a shorter version requires mental effort to decode, use the longer version. Long but readable is always better than short but confusing.

### No comments

Do not write comments in code. If a piece of code needs a comment to explain what it does, rewrite it so it is self-explanatory. Use clear method names and variable names instead.

### DRY — Don't Repeat Yourself

If you write the same logic in two places, extract it into a method or class. If two classes share structure, consider a base class or shared helper. Do not copy and paste code.

### Never use `dynamic`

`dynamic` bypasses the compiler and makes code impossible to follow. Never use it. Always use a concrete, named type.

### Avoid `object`

`object` loses all type information and forces the reader to guess what is inside. Avoid it. Define a proper class or interface instead. If you must hold mixed types, use a well-designed base class or generic.

### No `as` casting without a null check immediately after

If you use `as` to cast, always check the result for null on the very next line. Silently swallowing a failed cast causes bugs that are hard to trace. Prefer a direct cast `(Type)value` when you are certain of the type, so a wrong cast throws immediately and visibly.

### Explicit types over `var`

Use explicit types in declarations when the type is not immediately obvious from the right-hand side. `var result = GetDownloadResult()` hides information. `DownloadResult result = GetDownloadResult()` is clear.

`var` is acceptable only when the type is trivially obvious, for example:
```csharp
var entries = new List<QueueEntry>();
```

### Naming

- Classes, methods, properties: PascalCase
- Local variables, parameters: camelCase
- Private fields: `_camelCase`
- Boolean names should read as a question: `isInstalled`, `hasApiToken`, `canResume`

### One thing per method

Each method should do one thing and have a name that describes exactly that thing. If a method is doing two things, split it into two methods.

### Small classes

Each class has a single responsibility. If a class is growing large, look for a part that can be extracted into its own class with its own name.

### Never fire-and-forget a Task

Do not discard a Task with `_ = SomeAsync()`. If the task fails, the exception is silently swallowed and the caller never finds out.

If a method needs to kick off async work, return `Task` so the caller can await it:

```csharp
// Wrong
public void Resume() => _ = StartAsync(_url, _path, CancellationToken.None);

// Right
public async Task ResumeAsync() => await StartAsync(_url, _path, CancellationToken.None);
```

If the interface forces `void` (e.g. an event handler), use `async void` only as a last resort and ensure all exceptions are caught inside it.

### Use a CancellationTokenSource to stop async work

If a class starts async work that must be stoppable from the outside, give it an internal `CancellationTokenSource`. Callers cancel it; the async loop stops at the next await.

Do not use a boolean flag (`_isPaused`) as the sole cancellation mechanism. A flag is only checked at loop boundaries — it cannot interrupt a blocking `await stream.ReadAsync(...)` mid-read.

```csharp
// Wrong — flag not checked inside the read, stream keeps going
public void Cancel() { _isPaused = true; }

// Right — CTS cancels the token used by ReadAsync immediately
public void Cancel()
{
    _isPaused = true;
    _cts.Cancel();
}
```

Reset the CTS on each new start/resume so past cancellations do not affect new work:

```csharp
_cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
await DownloadWithRetryAsync(url, savePath, _cts.Token);
```

### Delete files only after their streams are closed

On Windows, you cannot delete a file that is still held open by a `FileStream`. If you call `File.Delete` while the file is being written to, you get an `IOException`.

Defer deletion to after the `using` block that owns the stream exits — for example, in a `catch` block that fires after the stream disposes:

```csharp
// Wrong — fileStream is still open when Cancel() tries to delete
public void Cancel(bool deleteFile)
{
    _cts.Cancel();
    File.Delete(filePath); // throws on Windows if download is active
}

// Right — delete inside the catch, after ExecuteDownloadAsync's using blocks unwind
catch (OperationCanceledException) when (_isCancelled)
{
    if (_deleteFileOnCancel) DeleteCurrentFile(); // file is closed here
    _status = DownloaderStatus.Error;
    FireProgress();
    return;
}
```

The exception to this rule: if the download is already stopped (paused, complete, or never started) the file is not open and can be deleted immediately.

### Narrow exception catches to the expected type

A bare `catch { }` or `catch (Exception) { }` masks programming errors and framework exceptions (`OutOfMemoryException`, `StackOverflowException`) that should never be swallowed.

Catch only the specific exception you expect:

```csharp
// Wrong
catch { return false; }

// Right
catch (UriFormatException) { return false; }
```

### Keep HttpClient long-lived and shared

`HttpClient` is designed to be reused. Creating a new instance per request exhausts the system's socket pool (connection limit per host) and bypasses connection keep-alive.

Create one `HttpClient` per logical endpoint (or per application lifetime) and reuse it:

```csharp
// Wrong — new client every call
public static async Task ResolveAsync(UserConfig config)
{
    RealDebridClient client = new RealDebridClient(config.Token); // creates new HttpClient inside
}

// Right — shared client passed in or held as a static/injected field
private static readonly HttpClient _sharedHttp = new HttpClient();

public static async Task ResolveAsync(UserConfig config)
{
    RealDebridClient client = new RealDebridClient(config.Token, _sharedHttp);
}
```

### Do not set HttpClient.BaseAddress if you use absolute URLs

`HttpClient.BaseAddress` is only used when you pass a relative URI to `GetAsync`/`PostAsync`. If every call already uses a full absolute URL, setting `BaseAddress` does nothing but mislead the reader into thinking relative paths are in use.

Either remove `BaseAddress` and keep absolute URLs, or keep `BaseAddress` and switch all calls to relative paths. Do not mix.

### Make persistence methods private

A method like `Persist()` that writes internal state to disk is an implementation detail. Making it `public` lets callers invoke it outside of a lock, creating a window where state can change between the mutation and the write (TOCTOU race).

Keep persistence private and call it inside the same `lock` block as the mutation that triggered it:

```csharp
// Wrong — Persist called outside lock, another thread can mutate between
public void Enqueue(QueueEntry entry)
{
    lock (_lock) { _entries.Add(entry); }
    Persist(); // race: another thread could Cancel() here
}

// Right — mutation and persistence in the same lock
public void Enqueue(QueueEntry entry)
{
    lock (_lock)
    {
        _entries.Add(entry);
        Persist();
    }
}

private void Persist() { ... }
```

### Blocking code in an async method must use Task.Run

Marking a method `async` does not make it non-blocking. If the method body contains blocking calls (file I/O, CPU work, third-party sync APIs) and only ends with `await Task.CompletedTask`, the method blocks the calling thread for its entire duration.

Wrap the blocking work in `Task.Run` so it executes on a thread-pool thread and the caller's thread is freed:

```csharp
// Wrong — looks async, actually blocks the calling thread
public async Task ExtractAsync(string path, CancellationToken ct)
{
    foreach (var entry in archive.Entries)
        entry.WriteToDirectory(outputPath); // blocking
    await Task.CompletedTask; // no-op
}

// Right — blocking work runs off the calling thread
public async Task ExtractAsync(string path, CancellationToken ct)
{
    await Task.Run(() =>
    {
        foreach (IArchiveEntry entry in archive.Entries)
            entry.WriteToDirectory(outputPath); // blocking, but on thread-pool
    }, ct);
}
```

### Interface and implementation nullability must match

If an interface declares an event or member as non-nullable, a consumer coding to the interface will not add a null check. If the implementation is nullable, a null dereference will occur at runtime.

Keep nullability identical between the interface declaration and every implementation:

```csharp
// Wrong — interface says non-nullable, implementation is nullable
public interface IDownloader { event Action<DownloadProgress> ProgressChanged; }
public class HttpDownloader  { event Action<DownloadProgress>? ProgressChanged; } // nullable

// Right — both nullable
public interface IDownloader { event Action<DownloadProgress>? ProgressChanged; }
public class HttpDownloader  { event Action<DownloadProgress>? ProgressChanged; }
```

### Remove unused model properties and dead enums

Unused properties in model classes and unused enum types mislead readers into thinking they are used somewhere. If a property is deserialized from JSON but never read, or an enum was planned but never referenced, delete them.

Unused code is not "harmless extra" — it is a question mark that future developers will waste time investigating.

---

## Persistence Files

| File | Location | Purpose |
|------|----------|---------|
| `builtin-sources.json` | Plugin install dir (read-only) | Curated source URLs shipped with plugin |
| `config.json` | `GetPluginUserDataPath()` | RD token, paths |
| `sources.json` | `GetPluginUserDataPath()` | Active source list |
| `queue.json` | `GetPluginUserDataPath()` | Persisted download queue |

---

## Key Behaviours

- One active download at a time (sequential queue)
- Magnets and hosted file links require Real-Debrid; show a clear error if RD is disabled
- Multi-part repacks: download all parts sequentially, then extract from the first part
- Resume support: `Range: bytes=N-` header; partial files are never deleted on pause
- Fuzzy matching: use FuzzySharp to rank search results by title similarity
- Source fetch: send a browser-like `User-Agent` header (required for Cloudflare-protected sources)
- Extraction entry point priority: `*.part1.rar` → `*.rar` (with sibling `*.r00`) → `*.zip` / `*.7z` / `*.tar`

---

## Testing

- Tests live in `PlayniteDownloaderPlugin.Tests/`
- Use xUnit for test structure and Moq for mocking interfaces
- Business logic classes should be independently testable without Playnite
- Do not test WPF UI code directly; keep ViewModels pure so they can be unit tested

---

## Out of Scope (v1)

- Parallel downloads
- Downloads surviving Playnite process exit
- Torbox, AllDebrid, Premiumize
- Auto-updating built-in source list
- GOG / Epic / other store install flows
