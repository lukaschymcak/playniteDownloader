# Playnite Downloader Plugin Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Playnite Generic Plugin that searches repack sources, downloads via Real-Debrid or direct HTTP, extracts, and registers games as installed.

**Architecture:** Modular layered architecture — Source → Download → Pipeline → Integration → UI. Each layer exposes a clean interface and is independently testable. Business logic is pure C#; WPF only in the UI layer.

**Tech Stack:** C# / .NET 6 / WPF, xUnit + Moq for tests, SharpCompress (extraction), FuzzySharp (fuzzy matching), Newtonsoft.Json, PlayniteSDK 6.x

---

## File Map

```
PlayniteDownloaderPlugin/
├── PlayniteDownloaderPlugin.csproj       target: net6.0-windows
├── plugin.json                           Playnite plugin manifest
├── builtin-sources.json                  Curated hydralinks.cloud URLs (read-only, ships with DLL)
│
├── Models/
│   ├── Enums.cs                          DownloadStatus, DownloaderStatus, UriType
│   ├── DownloadResult.cs                 Returned by source search
│   ├── QueueEntry.cs                     Single queue item with all state
│   ├── SourceEntry.cs                    A source record in sources.json
│   └── UserConfig.cs                     config.json structure
│
├── Source/
│   ├── ISourceProvider.cs                Interface: SearchAsync
│   ├── JsonSourceProvider.cs             Fetches + parses one JSON source URL
│   └── SourceManager.cs                  Manages built-in + custom sources, drives search
│
├── Download/
│   ├── IDownloader.cs                    Interface + DownloadProgress class
│   ├── RealDebridModels.cs               RD API response POCOs
│   ├── RealDebridClient.cs               RD REST API wrapper
│   ├── HttpDownloader.cs                 Resumable HTTP downloader (implements IDownloader)
│   └── DownloaderFactory.cs              ResolveUrlsAsync — selects RD or direct
│
├── Pipeline/
│   ├── InstallResolver.cs                Scores .exe files to find game executable
│   ├── ExtractionService.cs              Extracts archives via SharpCompress
│   └── DownloadQueue.cs                  Sequential queue, persisted, drives the pipeline
│
├── Integration/
│   └── PlayniteIntegration.cs            MarkInstalled — updates Playnite game record
│
├── UI/
│   ├── SidePanelViewModel.cs             Observable state for SidePanel
│   ├── SidePanel.xaml                    WPF UserControl — queue + settings
│   ├── SidePanel.xaml.cs                 Code-behind (minimal)
│   ├── SearchDialogViewModel.cs          Observable state + commands for SearchDialog
│   ├── SearchDialog.xaml                 WPF Window — search + results
│   └── SearchDialog.xaml.cs             Code-behind (minimal)
│
└── PlayniteDownloaderPlugin.cs           GenericPlugin subclass — entry point

PlayniteDownloaderPlugin.Tests/
├── PlayniteDownloaderPlugin.Tests.csproj target: net6.0-windows
├── Source/
│   ├── JsonSourceProviderTests.cs
│   └── SourceManagerTests.cs
├── Download/
│   ├── RealDebridClientTests.cs
│   ├── HttpDownloaderTests.cs
│   └── DownloaderFactoryTests.cs
├── Pipeline/
│   ├── InstallResolverTests.cs
│   ├── ExtractionServiceTests.cs
│   └── DownloadQueueTests.cs
└── Integration/
    └── PlayniteIntegrationTests.cs
```

---

## Chunk 1: Project Scaffold & Models

### Task 1: Create solution and project files

**Files:**
- Create: `PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj`
- Create: `PlayniteDownloaderPlugin.Tests/PlayniteDownloaderPlugin.Tests.csproj`
- Create: `PlayniteDownloaderPlugin/plugin.json`
- Create: `PlayniteDownloaderPlugin/builtin-sources.json`

- [ ] **Step 1: Create the main plugin .csproj**

```xml
<!-- PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>PlayniteDownloaderPlugin</AssemblyName>
    <RootNamespace>PlayniteDownloaderPlugin</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="PlayniteSDK" Version="6.4.0" />
    <PackageReference Include="SharpCompress" Version="0.37.2" />
    <PackageReference Include="FuzzySharp" Version="2.0.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
  <ItemGroup>
    <None Update="builtin-sources.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
    <None Update="plugin.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test project .csproj**

```xml
<!-- PlayniteDownloaderPlugin.Tests/PlayniteDownloaderPlugin.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.70" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create plugin.json**

```json
{
  "Id": "a3b4c5d6-e7f8-9012-abcd-ef1234567890",
  "Name": "Playnite Downloader",
  "Author": "You",
  "Version": "0.1.0",
  "Module": "PlayniteDownloaderPlugin.dll",
  "Type": "GenericPlugin",
  "Links": []
}
```

- [ ] **Step 4: Create builtin-sources.json**

```json
[
  {
    "id": "fitgirl",
    "name": "FitGirl Repacks",
    "url": "https://hydralinks.cloud/sources/fitgirl.json",
    "enabled": true,
    "isCustom": false,
    "addedAt": "2026-04-06T00:00:00Z"
  },
  {
    "id": "dodi",
    "name": "DODI Repacks",
    "url": "https://hydralinks.cloud/sources/dodi.json",
    "enabled": true,
    "isCustom": false,
    "addedAt": "2026-04-06T00:00:00Z"
  },
  {
    "id": "xatab",
    "name": "Xatab",
    "url": "https://hydralinks.cloud/sources/xatab.json",
    "enabled": true,
    "isCustom": false,
    "addedAt": "2026-04-06T00:00:00Z"
  }
]
```

- [ ] **Step 5: Verify projects build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
dotnet build PlayniteDownloaderPlugin.Tests/PlayniteDownloaderPlugin.Tests.csproj
```
Expected: Both build with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add PlayniteDownloaderPlugin/ PlayniteDownloaderPlugin.Tests/
git commit -m "chore: scaffold solution with plugin and test projects"
```

---

### Task 2: Define all models

**Files:**
- Create: `PlayniteDownloaderPlugin/Models/Enums.cs`
- Create: `PlayniteDownloaderPlugin/Models/DownloadResult.cs`
- Create: `PlayniteDownloaderPlugin/Models/QueueEntry.cs`
- Create: `PlayniteDownloaderPlugin/Models/SourceEntry.cs`
- Create: `PlayniteDownloaderPlugin/Models/UserConfig.cs`

- [ ] **Step 1: Create Enums.cs**

```csharp
// PlayniteDownloaderPlugin/Models/Enums.cs
namespace PlayniteDownloaderPlugin.Models;

public enum DownloadStatus
{
    Waiting,
    Active,
    Paused,
    Error,
    Complete,
    Extracting
}

public enum DownloaderStatus
{
    Active,
    Paused,
    Complete,
    Error
}

public enum UriType
{
    Magnet,
    Hoster,
    DirectHttp
}
```

- [ ] **Step 2: Create DownloadResult.cs**

```csharp
// PlayniteDownloaderPlugin/Models/DownloadResult.cs
namespace PlayniteDownloaderPlugin.Models;

public class DownloadResult
{
    public string Title { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public List<string> Uris { get; set; } = new();
    public string FileSize { get; set; } = string.Empty;
    public string UploadDate { get; set; } = string.Empty;
    public float MatchScore { get; set; }
}
```

- [ ] **Step 3: Create QueueEntry.cs**

```csharp
// PlayniteDownloaderPlugin/Models/QueueEntry.cs
namespace PlayniteDownloaderPlugin.Models;

public class QueueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string OriginalUri { get; set; } = string.Empty;
    public List<string> ResolvedUrls { get; set; } = new();
    public int CurrentUrlIndex { get; set; }
    public string DownloadPath { get; set; } = string.Empty;
    public string ExtractionPath { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Waiting;
    public float Progress { get; set; }
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public float ExtractionProgress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: Create SourceEntry.cs**

```csharp
// PlayniteDownloaderPlugin/Models/SourceEntry.cs
namespace PlayniteDownloaderPlugin.Models;

public class SourceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsCustom { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 5: Create UserConfig.cs**

```csharp
// PlayniteDownloaderPlugin/Models/UserConfig.cs
namespace PlayniteDownloaderPlugin.Models;

public class UserConfig
{
    public bool RealDebridEnabled { get; set; }
    public string RealDebridApiToken { get; set; } = string.Empty;
    public string DefaultDownloadPath { get; set; } = string.Empty;
    public int MaxConcurrentDownloads { get; set; } = 1;
}
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add PlayniteDownloaderPlugin/Models/
git commit -m "feat: add all model types and enums"
```

---

## Chunk 2: Source Layer

### Task 3: ISourceProvider and JsonSourceProvider

**Files:**
- Create: `PlayniteDownloaderPlugin/Source/ISourceProvider.cs`
- Create: `PlayniteDownloaderPlugin/Source/JsonSourceProvider.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Source/JsonSourceProviderTests.cs`

- [ ] **Step 1: Create ISourceProvider.cs**

```csharp
// PlayniteDownloaderPlugin/Source/ISourceProvider.cs
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public interface ISourceProvider
{
    string Id { get; }
    string Name { get; }
    Task<List<DownloadResult>> SearchAsync(string gameName, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing tests for JsonSourceProvider**

```csharp
// PlayniteDownloaderPlugin.Tests/Source/JsonSourceProviderTests.cs
using System.Net;
using Moq;
using PlayniteDownloaderPlugin.Source;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Source;

public class JsonSourceProviderTests
{
    private static HttpClient MakeClient(string json)
    {
        var handler = new MockHttpMessageHandler(json);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResults_WhenTitleMatches()
    {
        var json = """
        {
          "name": "TestSource",
          "downloads": [
            { "title": "Cyberpunk 2077", "uris": ["magnet:?test"], "fileSize": "70 GB", "uploadDate": "2024-01-01" }
          ]
        }
        """;
        var provider = new JsonSourceProvider("src1", "TestSource",
            "http://example.com/source.json", MakeClient(json));

        var results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Cyberpunk 2077", results[0].Title);
        Assert.Equal("src1", results[0].SourceId);
        Assert.Equal("magnet:?test", results[0].Uris[0]);
        Assert.Equal("70 GB", results[0].FileSize);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenNoMatch()
    {
        var json = """
        {
          "name": "TestSource",
          "downloads": [
            { "title": "Unrelated Game", "uris": ["magnet:?x"], "fileSize": "5 GB", "uploadDate": null }
          ]
        }
        """;
        var provider = new JsonSourceProvider("src1", "TestSource",
            "http://example.com/source.json", MakeClient(json));

        var results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFuzzyMatch_ForCloseTitle()
    {
        var json = """
        {
          "name": "TestSource",
          "downloads": [
            { "title": "Cyberpunk 2077 v2.1", "uris": ["https://direct"], "fileSize": "70 GB", "uploadDate": "2024-01-01" }
          ]
        }
        """;
        var provider = new JsonSourceProvider("src1", "TestSource",
            "http://example.com/source.json", MakeClient(json));

        var results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

        Assert.Single(results);
        Assert.True(results[0].MatchScore > 0.5f);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenHttpFails()
    {
        var handler = new FailingHttpMessageHandler();
        var provider = new JsonSourceProvider("src1", "TestSource",
            "http://example.com/source.json", new HttpClient(handler));

        var results = await provider.SearchAsync("Any Game", CancellationToken.None);

        Assert.Empty(results);
    }
}

// Test helpers
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _json;
    public MockHttpMessageHandler(string json) => _json = json;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_json)
        });
}

public class FailingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => throw new HttpRequestException("Connection refused");
}
```

- [ ] **Step 3: Run tests — expect FAIL (JsonSourceProvider doesn't exist yet)**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~JsonSourceProviderTests"
```
Expected: Build error — `JsonSourceProvider` not found.

- [ ] **Step 4: Implement JsonSourceProvider.cs**

```csharp
// PlayniteDownloaderPlugin/Source/JsonSourceProvider.cs
using FuzzySharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public class JsonSourceProvider : ISourceProvider
{
    private const int MinFuzzyScore = 60; // 0-100 scale (FuzzySharp uses int)
    private static readonly string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly string _url;
    private readonly HttpClient _http;

    public string Id { get; }
    public string Name { get; }

    public JsonSourceProvider(string id, string name, string url, HttpClient? http = null)
    {
        Id = id;
        Name = name;
        _url = url;
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<List<DownloadResult>> SearchAsync(string gameName, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(_url, ct);
            var root = JObject.Parse(json);
            var downloads = root["downloads"] as JArray ?? new JArray();

            var results = new List<DownloadResult>();
            foreach (var item in downloads)
            {
                var title = item["title"]?.Value<string>() ?? string.Empty;
                var score = Fuzz.Ratio(gameName.ToLowerInvariant(), title.ToLowerInvariant());
                if (score < MinFuzzyScore) continue;

                var uris = (item["uris"] as JArray)?.Select(u => u.Value<string>()!)
                    .Where(u => !string.IsNullOrEmpty(u)).ToList() ?? new List<string>();

                results.Add(new DownloadResult
                {
                    Title = title,
                    SourceId = Id,
                    SourceName = Name,
                    Uris = uris,
                    FileSize = item["fileSize"]?.Value<string>() ?? string.Empty,
                    UploadDate = item["uploadDate"]?.Value<string>() ?? string.Empty,
                    MatchScore = score / 100f
                });
            }

            return results.OrderByDescending(r => r.MatchScore).ToList();
        }
        catch (Exception)
        {
            // Source fetch failures are non-fatal — skip and continue
            return new List<DownloadResult>();
        }
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~JsonSourceProviderTests"
```
Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add PlayniteDownloaderPlugin/Source/ PlayniteDownloaderPlugin.Tests/Source/JsonSourceProviderTests.cs
git commit -m "feat: add ISourceProvider and JsonSourceProvider with fuzzy matching"
```

---

### Task 4: SourceManager

**Files:**
- Create: `PlayniteDownloaderPlugin/Source/SourceManager.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Source/SourceManagerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Source/SourceManagerTests.cs
using Moq;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Source;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Source;

public class SourceManagerTests
{
    private static ISourceProvider MakeProvider(string id, List<DownloadResult> results)
    {
        var mock = new Mock<ISourceProvider>();
        mock.Setup(p => p.Id).Returns(id);
        mock.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        return mock.Object;
    }

    [Fact]
    public async Task SearchAllAsync_AggregatesResultsFromAllProviders()
    {
        var r1 = new DownloadResult { Title = "Game A", MatchScore = 0.9f };
        var r2 = new DownloadResult { Title = "Game A v2", MatchScore = 0.7f };
        var manager = new SourceManager(new[]
        {
            MakeProvider("p1", new List<DownloadResult> { r1 }),
            MakeProvider("p2", new List<DownloadResult> { r2 }),
        });

        var results = await manager.SearchAllAsync("Game A", CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAllAsync_ReturnsSortedByMatchScoreDescending()
    {
        var r1 = new DownloadResult { Title = "Game A", MatchScore = 0.7f };
        var r2 = new DownloadResult { Title = "Game A Plus", MatchScore = 0.95f };
        var manager = new SourceManager(new[]
        {
            MakeProvider("p1", new List<DownloadResult> { r1, r2 }),
        });

        var results = await manager.SearchAllAsync("Game A", CancellationToken.None);

        Assert.Equal(0.95f, results[0].MatchScore);
        Assert.Equal(0.7f, results[1].MatchScore);
    }

    [Fact]
    public void AddCustomSource_AddsToSources()
    {
        var manager = new SourceManager(Array.Empty<ISourceProvider>());

        manager.AddCustomSource("My Source", "https://example.com/source.json");

        Assert.Single(manager.GetAllSources());
    }

    [Fact]
    public void AddCustomSource_ThrowsOnDuplicateUrl()
    {
        var manager = new SourceManager(Array.Empty<ISourceProvider>());
        manager.AddCustomSource("Source", "https://example.com/source.json");

        Assert.Throws<InvalidOperationException>(
            () => manager.AddCustomSource("Source2", "https://example.com/source.json"));
    }

    [Fact]
    public void RemoveCustomSource_RemovesById()
    {
        var manager = new SourceManager(Array.Empty<ISourceProvider>());
        manager.AddCustomSource("My Source", "https://example.com/source.json");
        var id = manager.GetAllSources().First().Id;

        manager.RemoveCustomSource(id);

        Assert.Empty(manager.GetAllSources());
    }

    [Fact]
    public void CustomSources_PersistedAndReloadedAcrossInstances()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager1 = new SourceManager(Array.Empty<ISourceProvider>(),
                persistPath: tempDir);
            manager1.AddCustomSource("Saved Source", "https://example.com/s.json");

            var manager2 = new SourceManager(Array.Empty<ISourceProvider>(),
                persistPath: tempDir);
            var sources = manager2.GetCustomSources();

            Assert.Single(sources);
            Assert.Equal("Saved Source", sources[0].Name);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~SourceManagerTests"
```
Expected: Build error.

- [ ] **Step 3: Implement SourceManager.cs**

```csharp
// PlayniteDownloaderPlugin/Source/SourceManager.cs
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public class SourceManager
{
    private readonly List<ISourceProvider> _builtinProviders;
    private readonly List<SourceEntry> _builtinEntries;
    private readonly List<SourceEntry> _customSources = new();
    private readonly string? _persistPath; // null in tests

    public SourceManager(IEnumerable<ISourceProvider> builtinProviders,
        IEnumerable<SourceEntry>? builtinEntries = null,
        string? persistPath = null)
    {
        _builtinProviders = builtinProviders.ToList();
        _builtinEntries = builtinEntries?.ToList() ?? new();
        _persistPath = persistPath;
        if (persistPath != null) LoadCustomSources(persistPath);
    }

    private void LoadCustomSources(string persistPath)
    {
        var path = Path.Combine(persistPath, "sources.json");
        if (!File.Exists(path)) return;
        var loaded = JsonConvert.DeserializeObject<List<SourceEntry>>(File.ReadAllText(path));
        if (loaded != null) _customSources.AddRange(loaded);
    }

    public async Task<List<DownloadResult>> SearchAllAsync(string gameName, CancellationToken ct)
    {
        // Search built-in providers
        var builtinTasks = _builtinProviders.Select(p => p.SearchAsync(gameName, ct));

        // Search custom sources on-the-fly via JsonSourceProvider
        var customTasks = _customSources
            .Where(s => s.Enabled)
            .Select(s => (ISourceProvider)new JsonSourceProvider(s.Id, s.Name, s.Url))
            .Select(p => p.SearchAsync(gameName, ct));

        var all = await Task.WhenAll(builtinTasks.Concat(customTasks));
        return all.SelectMany(r => r)
            .OrderByDescending(r => r.MatchScore)
            .ToList();
    }

    public void AddCustomSource(string name, string url)
    {
        if (_customSources.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) ||
            _builtinEntries.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A source with URL '{url}' already exists.");

        _customSources.Add(new SourceEntry { Name = name, Url = url, IsCustom = true });
        PersistCustomSources();
    }

    public void RemoveCustomSource(string id)
    {
        var entry = _customSources.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Source '{id}' not found.");
        _customSources.Remove(entry);
        PersistCustomSources();
    }

    /// <summary>Returns all sources — built-in and custom — for UI display.</summary>
    public IReadOnlyList<SourceEntry> GetAllSources()
        => _builtinEntries.Concat(_customSources).ToList().AsReadOnly();

    public IReadOnlyList<SourceEntry> GetCustomSources() => _customSources.AsReadOnly();

    private void PersistCustomSources()
    {
        if (_persistPath == null) return;
        var path = Path.Combine(_persistPath, "sources.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_customSources, Formatting.Indented));
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~SourceManagerTests"
```
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Source/SourceManager.cs PlayniteDownloaderPlugin.Tests/Source/SourceManagerTests.cs
git commit -m "feat: add SourceManager with multi-provider search and custom source management"
```

---

## Chunk 3: Download Layer

### Task 5: IDownloader interface and DownloadProgress

**Files:**
- Create: `PlayniteDownloaderPlugin/Download/IDownloader.cs`

- [ ] **Step 1: Create IDownloader.cs**

```csharp
// PlayniteDownloaderPlugin/Download/IDownloader.cs
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Download;

public class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DownloaderStatus Status { get; set; }
}

public interface IDownloader
{
    event Action<DownloadProgress> ProgressChanged;
    // savePath is a directory; filename is derived from Content-Disposition or the URL.
    Task StartAsync(string url, string savePath, CancellationToken ct);
    void Pause();
    void Resume();
    void Cancel(bool deleteFile = true);
    DownloadProgress GetStatus();
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add PlayniteDownloaderPlugin/Download/IDownloader.cs
git commit -m "feat: add IDownloader interface and DownloadProgress"
```

---

### Task 6: RealDebridClient

**Files:**
- Create: `PlayniteDownloaderPlugin/Download/RealDebridModels.cs`
- Create: `PlayniteDownloaderPlugin/Download/RealDebridClient.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Download/RealDebridClientTests.cs`

- [ ] **Step 1: Create RealDebridModels.cs**

```csharp
// PlayniteDownloaderPlugin/Download/RealDebridModels.cs
using Newtonsoft.Json;

namespace PlayniteDownloaderPlugin.Download;

public class RdAddMagnetResponse
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("uri")] public string Uri { get; set; } = string.Empty;
}

public class RdTorrentInfo
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("links")] public List<string> Links { get; set; } = new();
    [JsonProperty("progress")] public int Progress { get; set; }
}

public class RdUnrestrictResponse
{
    [JsonProperty("download")] public string Download { get; set; } = string.Empty;
    [JsonProperty("filename")] public string Filename { get; set; } = string.Empty;
}

public class RdUser
{
    [JsonProperty("username")] public string Username { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("premium")] public int Premium { get; set; }
    [JsonProperty("expiration")] public string Expiration { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Write failing tests for RealDebridClient**

```csharp
// PlayniteDownloaderPlugin.Tests/Download/RealDebridClientTests.cs
using System.Net;
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Download;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class RealDebridClientTests
{
    private static HttpClient MakeClient(params (string url, string json)[] responses)
    {
        var handler = new MultiRouteHandler(responses);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task GetDownloadUrl_DirectLink_UnrestrictsAndReturnsUrl()
    {
        var unrestrict = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/file.zip" });
        var client = new RealDebridClient("test-token",
            MakeClient(("/unrestrict/link", unrestrict)));

        var urls = await client.GetDownloadUrlAsync(
            "https://1fichier.com/?abc123", CancellationToken.None);

        Assert.Single(urls);
        Assert.Equal("https://cdn.example.com/file.zip", urls[0]);
    }

    [Fact]
    public async Task GetDownloadUrl_Magnet_PollsUntilDownloadedAndReturnsAllLinks()
    {
        var addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        var torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "downloaded",
            Links = new List<string> { "https://rd.com/link1", "https://rd.com/link2" }
        });
        var unrestrict1 = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/part1.rar" });
        var unrestrict2 = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/part2.rar" });

        var client = new RealDebridClient("test-token", MakeClient(
            ("/torrents/addMagnet", addMagnet),
            ("/torrents/selectFiles/tor1", "{}"),
            ("/torrents/info/tor1", torrentInfo),
            ("/unrestrict/link", unrestrict1), // called twice
            ("/unrestrict/link", unrestrict2)
        ));

        var urls = await client.GetDownloadUrlAsync("magnet:?xt=test", CancellationToken.None);

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://cdn.example.com/part1.rar", urls[0]);
        Assert.Equal("https://cdn.example.com/part2.rar", urls[1]);
    }

    [Fact]
    public async Task GetDownloadUrl_Magnet_ThrowsWhenStatusIsError()
    {
        var addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        var torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "error", Links = new List<string>()
        });

        var client = new RealDebridClient("test-token", MakeClient(
            ("/torrents/addMagnet", addMagnet),
            ("/torrents/selectFiles/tor1", "{}"),
            ("/torrents/info/tor1", torrentInfo)
        ));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetDownloadUrlAsync("magnet:?xt=test", CancellationToken.None));
    }

    [Fact]
    public async Task GetDownloadUrl_Magnet_ThrowsTimeoutWhenPollDeadlineExceeded()
    {
        // Use a 0ms timeout so the deadline is exceeded immediately after the first poll.
        var addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        var torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "downloading", Links = new List<string>()
        });

        // Return "downloading" indefinitely — deadline will expire before next poll.
        var handler = new InfiniteHandler(new[]
        {
            ("/torrents/addMagnet", addMagnet),
            ("/torrents/selectFiles/tor1", "{}"),
        }, torrentInfo);

        var client = new RealDebridClient("test-token", new HttpClient(handler),
            pollTimeout: TimeSpan.FromMilliseconds(0));

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetDownloadUrlAsync("magnet:?xt=test", CancellationToken.None));
    }
}

public class MultiRouteHandler : HttpMessageHandler
{
    private readonly Queue<(string urlSuffix, string json)> _responses;
    public MultiRouteHandler(params (string, string)[] responses)
        => _responses = new Queue<(string, string)>(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (_, json) = _responses.Dequeue();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Serves a fixed sequence of responses for the first N requests,
/// then returns <paramref name="infiniteJson"/> for all subsequent requests.
/// Used to simulate "still processing" poll responses without bound.
/// </summary>
public class InfiniteHandler : HttpMessageHandler
{
    private readonly Queue<(string, string)> _preamble;
    private readonly string _infiniteJson;
    public InfiniteHandler(IEnumerable<(string, string)> preamble, string infiniteJson)
    {
        _preamble = new Queue<(string, string)>(preamble);
        _infiniteJson = infiniteJson;
    }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var json = _preamble.Count > 0 ? _preamble.Dequeue().Item2 : _infiniteJson;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
```

- [ ] **Step 3: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~RealDebridClientTests"
```
Expected: Build error.

- [ ] **Step 4: Implement RealDebridClient.cs**

```csharp
// PlayniteDownloaderPlugin/Download/RealDebridClient.cs
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Web;

namespace PlayniteDownloaderPlugin.Download;

public class RealDebridClient
{
    private const string BaseUrl = "https://api.real-debrid.com/rest/1.0";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromMinutes(30);
    private static readonly HashSet<string> TerminalErrorStatuses =
        new() { "error", "dead", "virus" };

    private readonly HttpClient _http;
    private readonly TimeSpan _pollTimeout;

    public RealDebridClient(string apiToken, HttpClient? http = null,
        TimeSpan? pollTimeout = null)
    {
        _http = http ?? new HttpClient();
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiToken);
        _pollTimeout = pollTimeout ?? DefaultPollTimeout;
    }

    public async Task<List<string>> GetDownloadUrlAsync(string uri, CancellationToken ct)
    {
        if (uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return await ResolveMagnetAsync(uri, ct);
        return await ResolveDirectLinkAsync(uri, ct);
    }

    private async Task<List<string>> ResolveMagnetAsync(string magnet, CancellationToken ct)
    {
        var addResp = await PostFormAsync<RdAddMagnetResponse>(
            "/torrents/addMagnet", new Dictionary<string, string> { ["magnet"] = magnet }, ct);
        var torrentId = addResp.Id;

        await PostFormAsync<object>(
            $"/torrents/selectFiles/{torrentId}",
            new Dictionary<string, string> { ["files"] = "all" }, ct);

        var deadline = DateTime.UtcNow + _pollTimeout;
        RdTorrentInfo info;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            info = await GetAsync<RdTorrentInfo>($"/torrents/info/{torrentId}", ct);

            if (info.Status == "downloaded") break;
            if (TerminalErrorStatuses.Contains(info.Status))
                throw new InvalidOperationException(
                    $"Real-Debrid torrent failed with status: {info.Status}");
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    "Real-Debrid took too long to process the torrent (30 min timeout).");

            await Task.Delay(PollInterval, ct);
        }

        var directUrls = new List<string>();
        foreach (var link in info.Links)
        {
            var unrestricted = await PostFormAsync<RdUnrestrictResponse>(
                "/unrestrict/link", new Dictionary<string, string> { ["link"] = link }, ct);
            directUrls.Add(Uri.UnescapeDataString(unrestricted.Download));
        }
        return directUrls;
    }

    private async Task<List<string>> ResolveDirectLinkAsync(string link, CancellationToken ct)
    {
        var unrestricted = await PostFormAsync<RdUnrestrictResponse>(
            "/unrestrict/link", new Dictionary<string, string> { ["link"] = link }, ct);
        return new List<string> { Uri.UnescapeDataString(unrestricted.Download) };
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        var response = await _http.GetStringAsync(BaseUrl + path, ct);
        return JsonConvert.DeserializeObject<T>(response)!;
    }

    private async Task<T> PostFormAsync<T>(
        string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(fields);
        var response = await _http.PostAsync(BaseUrl + path, content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return default!;
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    public async Task<RdUser> GetUserAsync(CancellationToken ct)
        => await GetAsync<RdUser>("/user", ct);
}
```

- [ ] **Step 5: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~RealDebridClientTests"
```
Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add PlayniteDownloaderPlugin/Download/RealDebrid* PlayniteDownloaderPlugin.Tests/Download/RealDebridClientTests.cs
git commit -m "feat: add RealDebridClient with magnet resolution, polling, and link unrestrict"
```

---

### Task 7: HttpDownloader

**Files:**
- Create: `PlayniteDownloaderPlugin/Download/HttpDownloader.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Download/HttpDownloaderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Download/HttpDownloaderTests.cs
using System.Net;
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class HttpDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public HttpDownloaderTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task StartAsync_DownloadsFileToDirectory()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new StaticFileHandler(content, "game.zip");
        var downloader = new HttpDownloader(new HttpClient(handler));

        await downloader.StartAsync("http://example.com/game.zip", _tempDir, CancellationToken.None);

        var files = Directory.GetFiles(_tempDir);
        Assert.Single(files);
        Assert.Equal(content, await File.ReadAllBytesAsync(files[0]));
    }

    [Fact]
    public async Task StartAsync_UsesContentDispositionFilename()
    {
        var content = new byte[] { 1, 2, 3 };
        var handler = new StaticFileHandler(content, "actual-name.rar",
            contentDisposition: "attachment; filename=\"actual-name.rar\"");
        var downloader = new HttpDownloader(new HttpClient(handler));

        await downloader.StartAsync("http://example.com/randomtoken123", _tempDir, CancellationToken.None);

        var files = Directory.GetFiles(_tempDir);
        Assert.Contains("actual-name.rar", files[0]);
    }

    [Fact]
    public async Task StartAsync_ReportsProgressEvents()
    {
        var content = new byte[1024 * 10]; // 10KB
        var handler = new StaticFileHandler(content, "big.zip");
        var downloader = new HttpDownloader(new HttpClient(handler));
        var progressEvents = new List<DownloadProgress>();
        downloader.ProgressChanged += p => progressEvents.Add(p);

        await downloader.StartAsync("http://example.com/big.zip", _tempDir, CancellationToken.None);

        Assert.NotEmpty(progressEvents);
        Assert.Equal(DownloaderStatus.Complete, downloader.GetStatus().Status);
    }

    [Fact]
    public async Task Cancel_DeletesPartialFile()
    {
        // Arrange: start a download that we can cancel mid-way
        var content = new byte[1024 * 64]; // 64KB
        var handler = new StaticFileHandler(content, "partial.zip");
        var downloader = new HttpDownloader(new HttpClient(handler));
        string? downloadedFile = null;
        downloader.ProgressChanged += p =>
        {
            if (!string.IsNullOrEmpty(p.FileName))
                downloadedFile = Path.Combine(_tempDir, p.FileName);
        };

        // Start download then immediately cancel
        var downloadTask = downloader.StartAsync("http://example.com/partial.zip",
            _tempDir, CancellationToken.None);
        downloader.Cancel(deleteFile: true);
        await Task.WhenAny(downloadTask, Task.Delay(2000));

        // If a file was being written, it should be deleted
        if (downloadedFile != null)
            Assert.False(File.Exists(downloadedFile), "Partial file should be deleted on cancel");
        Assert.Equal(DownloaderStatus.Paused, downloader.GetStatus().Status);
    }
}

public class StaticFileHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly string _filename;
    private readonly string? _contentDisposition;

    public StaticFileHandler(byte[] content, string filename, string? contentDisposition = null)
    {
        _content = content;
        _filename = filename;
        _contentDisposition = contentDisposition;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_content)
        };
        response.Content.Headers.ContentLength = _content.Length;
        if (_contentDisposition != null)
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", _contentDisposition);
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~HttpDownloaderTests"
```
Expected: Build error.

- [ ] **Step 3: Implement HttpDownloader.cs**

```csharp
// PlayniteDownloaderPlugin/Download/HttpDownloader.cs
using PlayniteDownloaderPlugin.Models;
using System.Text.RegularExpressions;

namespace PlayniteDownloaderPlugin.Download;

public class HttpDownloader : IDownloader
{
    private const int MaxRetryAttempts = 10;
    private const int StallTimeoutMs = 8000;
    private const int StallCheckIntervalMs = 2000;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private CancellationTokenSource? _pauseCts;
    private bool _isPaused;
    private string _currentUrl = string.Empty;
    private string _currentSavePath = string.Empty;
    private string _resolvedFileName = string.Empty;

    private long _bytesDownloaded;
    private long _fileSize;
    private double _speed;
    private DownloaderStatus _status = DownloaderStatus.Paused;

    public event Action<DownloadProgress>? ProgressChanged;

    public HttpDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public async Task StartAsync(string url, string savePath, CancellationToken ct)
    {
        _currentUrl = url;
        _currentSavePath = savePath;
        _isPaused = false;
        _bytesDownloaded = 0;
        _speed = 0;

        await DownloadWithRetryAsync(url, savePath, ct);
    }

    private async Task DownloadWithRetryAsync(string url, string savePath, CancellationToken ct)
    {
        var retries = 0;
        while (!_isPaused)
        {
            try
            {
                await ExecuteDownloadAsync(url, savePath, ct);
                return;
            }
            catch (OperationCanceledException) when (_isPaused)
            {
                _status = DownloaderStatus.Paused;
                return;
            }
            catch (Exception) when (retries < MaxRetryAttempts && !_isPaused)
            {
                retries++;
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(1000 * Math.Pow(2, retries - 1), MaxRetryDelay.TotalMilliseconds));
                await Task.Delay(delay, ct);
            }
        }
        _status = DownloaderStatus.Error;
    }

    private async Task ExecuteDownloadAsync(string url, string savePath, CancellationToken ct)
    {
        _status = DownloaderStatus.Active;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var existingFile = FindExistingFile(savePath);
        if (existingFile != null)
        {
            _bytesDownloaded = new FileInfo(existingFile).Length;
            request.Headers.Range =
                new System.Net.Http.Headers.RangeHeaderValue(_bytesDownloaded, null);
        }

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        _resolvedFileName = ParseFilename(response, url);
        var filePath = Path.Combine(savePath, _resolvedFileName);
        _fileSize = (response.Content.Headers.ContentLength ?? 0) + _bytesDownloaded;

        var flags = _bytesDownloaded > 0 ? FileMode.Append : FileMode.Create;
        using var fileStream = new FileStream(filePath, flags, FileAccess.Write);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        var lastSpeedUpdate = DateTime.UtcNow;
        var bytesAtLastUpdate = _bytesDownloaded;

        // Read with stall detection via a timed cancellation source
        int bytesRead;
        while (true)
        {
            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stallCts.CancelAfter(StallTimeoutMs);
            try
            {
                bytesRead = await stream.ReadAsync(buffer, stallCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Download stalled — no data received for 8 seconds.");
            }

            if (bytesRead == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            _bytesDownloaded += bytesRead;

            var now = DateTime.UtcNow;
            var elapsed = (now - lastSpeedUpdate).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _speed = (_bytesDownloaded - bytesAtLastUpdate) / elapsed;
                lastSpeedUpdate = now;
                bytesAtLastUpdate = _bytesDownloaded;
            }

            FireProgress();
        }

        _status = DownloaderStatus.Complete;
        FireProgress();
    }

    // Returns the partial file path if a file matching the expected name already exists,
    // enabling Range-based resume. Only matches if _resolvedFileName is already known.
    private string? FindExistingFile(string savePath)
    {
        if (!Directory.Exists(savePath) || string.IsNullOrEmpty(_resolvedFileName))
            return null;
        var candidate = Path.Combine(savePath, _resolvedFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string ParseFilename(HttpResponseMessage response, string url)
    {
        var cd = response.Content.Headers.ContentDisposition;
        if (cd != null)
        {
            var name = cd.FileNameStar ?? cd.FileName;
            if (!string.IsNullOrEmpty(name))
                return SanitizeFilename(name.Trim('"'));
        }
        var path = new Uri(url).AbsolutePath;
        var fromUrl = Path.GetFileName(path);
        return string.IsNullOrEmpty(fromUrl) ? "download" : fromUrl;
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private void FireProgress() => ProgressChanged?.Invoke(new DownloadProgress
    {
        BytesDownloaded = _bytesDownloaded,
        FileSize = _fileSize,
        SpeedBytesPerSecond = _speed,
        FileName = _resolvedFileName,
        Status = _status
    });

    public void Pause()
    {
        _isPaused = true;
        _status = DownloaderStatus.Paused;
    }

    public void Resume()
    {
        if (_status != DownloaderStatus.Paused) return;
        _ = StartAsync(_currentUrl, _currentSavePath, CancellationToken.None);
    }

    public void Cancel(bool deleteFile = true)
    {
        _isPaused = true;
        _status = DownloaderStatus.Paused;
        if (deleteFile && !string.IsNullOrEmpty(_currentSavePath)
            && !string.IsNullOrEmpty(_resolvedFileName))
        {
            var file = Path.Combine(_currentSavePath, _resolvedFileName);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    public DownloadProgress GetStatus() => new()
    {
        BytesDownloaded = _bytesDownloaded,
        FileSize = _fileSize,
        SpeedBytesPerSecond = _speed,
        FileName = _resolvedFileName,
        Status = _status
    };
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~HttpDownloaderTests"
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Download/HttpDownloader.cs PlayniteDownloaderPlugin.Tests/Download/HttpDownloaderTests.cs
git commit -m "feat: add HttpDownloader with resume, retry backoff, Content-Disposition parsing"
```

---

### Task 8: DownloaderFactory

**Files:**
- Create: `PlayniteDownloaderPlugin/Download/DownloaderFactory.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Download/DownloaderFactoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Download/DownloaderFactoryTests.cs
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class DownloaderFactoryTests
{
    private static UserConfig RdEnabled() => new()
        { RealDebridEnabled = true, RealDebridApiToken = "token" };
    private static UserConfig RdDisabled() => new() { RealDebridEnabled = false };

    [Fact]
    public void IsKnownHoster_ReturnsTrueForKnownDomains()
    {
        Assert.True(DownloaderFactory.IsKnownHoster("https://1fichier.com/?abc"));
        Assert.True(DownloaderFactory.IsKnownHoster("https://rapidgator.net/file/abc"));
    }

    [Fact]
    public void IsKnownHoster_ReturnsFalseForDirectLinks()
    {
        Assert.False(DownloaderFactory.IsKnownHoster("https://cdn.example.com/file.zip"));
        Assert.False(DownloaderFactory.IsKnownHoster("https://github.com/releases/file.zip"));
    }

    [Fact]
    public async Task ResolveUrlsAsync_DirectHttp_ReturnsSameUrl()
    {
        var (urls, usedRd) = await DownloaderFactory.ResolveUrlsAsync(
            "https://cdn.example.com/file.zip", RdDisabled(), CancellationToken.None);

        Assert.Single(urls);
        Assert.Equal("https://cdn.example.com/file.zip", urls[0]);
        Assert.False(usedRd);
    }

    [Fact]
    public async Task ResolveUrlsAsync_MagnetWithRdDisabled_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DownloaderFactory.ResolveUrlsAsync(
                "magnet:?xt=test", RdDisabled(), CancellationToken.None));
    }

    [Fact]
    public async Task ResolveUrlsAsync_HosterWithRdDisabled_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DownloaderFactory.ResolveUrlsAsync(
                "https://1fichier.com/?abc", RdDisabled(), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~DownloaderFactoryTests"
```
Expected: Build error.

- [ ] **Step 3: Implement DownloaderFactory.cs**

```csharp
// PlayniteDownloaderPlugin/Download/DownloaderFactory.cs
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Download;

public static class DownloaderFactory
{
    private static readonly HashSet<string> KnownHosters = new(StringComparer.OrdinalIgnoreCase)
    {
        "1fichier.com", "rapidgator.net", "nitroflare.com",
        "uploaded.net", "turbobit.net", "hitfile.net",
        "katfile.com", "filefox.cc", "ddownload.com"
    };

    public static bool IsKnownHoster(string uri)
    {
        try
        {
            var host = new Uri(uri).Host.TrimStart('w', '.');
            return KnownHosters.Any(h => host.EndsWith(h, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public static async Task<(List<string> urls, bool usedRealDebrid)> ResolveUrlsAsync(
        string uri, UserConfig config, CancellationToken ct)
    {
        var isMagnet = uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
        var isHoster = IsKnownHoster(uri);

        if (isMagnet || isHoster)
        {
            if (!config.RealDebridEnabled)
                throw new InvalidOperationException(
                    "Real-Debrid is required for magnet links and hosted file links. " +
                    "Enable Real-Debrid in settings or choose a direct HTTP link.");

            var rdClient = new RealDebridClient(config.RealDebridApiToken);
            var urls = await rdClient.GetDownloadUrlAsync(uri, ct);
            return (urls, true);
        }

        return (new List<string> { uri }, false);
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~DownloaderFactoryTests"
```
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Download/DownloaderFactory.cs PlayniteDownloaderPlugin.Tests/Download/DownloaderFactoryTests.cs
git commit -m "feat: add DownloaderFactory with known hoster detection and RD requirement enforcement"
```

---

## Chunk 4: Pipeline Layer

### Task 9: InstallResolver

**Files:**
- Create: `PlayniteDownloaderPlugin/Pipeline/InstallResolver.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Pipeline/InstallResolverTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Pipeline/InstallResolverTests.cs
using PlayniteDownloaderPlugin.Pipeline;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Pipeline;

public class InstallResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public InstallResolverTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateExe(string relativePath, int sizeBytes = 1000)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[sizeBytes]);
        return full;
    }

    [Fact]
    public void FindExecutable_ReturnsLargestExeWhenNoSetupFiles()
    {
        CreateExe("game.exe", 5_000_000);
        CreateExe("small.exe", 100_000);

        var result = InstallResolver.FindExecutable(_dir);

        Assert.NotNull(result);
        Assert.Contains("game.exe", result);
    }

    [Fact]
    public void FindExecutable_PenalisesSetupExe()
    {
        CreateExe("setup.exe", 10_000_000);  // large but penalised
        CreateExe("game.exe", 2_000_000);    // smaller but wins

        var result = InstallResolver.FindExecutable(_dir);

        Assert.Contains("game.exe", result);
    }

    [Fact]
    public void FindExecutable_ReturnsNullForEmptyDirectory()
    {
        var result = InstallResolver.FindExecutable(_dir);
        Assert.Null(result);
    }

    [Fact]
    public void FindExecutable_PrefersShallowerPath()
    {
        CreateExe("deep/subdir/game.exe", 5_000_000);
        CreateExe("game.exe", 5_000_000);

        var result = InstallResolver.FindExecutable(_dir);

        Assert.Equal(Path.Combine(_dir, "game.exe"), result);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~InstallResolverTests"
```
Expected: Build error.

- [ ] **Step 3: Implement InstallResolver.cs**

```csharp
// PlayniteDownloaderPlugin/Pipeline/InstallResolver.cs
namespace PlayniteDownloaderPlugin.Pipeline;

public static class InstallResolver
{
    private static readonly HashSet<string> PenalisedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "install", "unins", "redist", "vc_", "directx", "vcredist", "dxsetup"
    };

    public static string? FindExecutable(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var exeFiles = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories);
        var candidates = exeFiles.Select(path => Score(path, directory)).ToList();

        if (!candidates.Any()) return null;
        return candidates.OrderByDescending(c => c.score).First().path;
    }

    private static (string path, double score) Score(string path, string baseDir)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var fileInfo = new FileInfo(path);
        var depth = path[baseDir.Length..].TrimStart(Path.DirectorySeparatorChar)
            .Count(c => c == Path.DirectorySeparatorChar);

        double score = fileInfo.Length / (1024.0 * 1024.0 * 10.0); // MB/10
        score -= depth * 5;

        if (PenalisedKeywords.Any(k => name.Contains(k)))
            score -= 100;

        return (path, score);
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~InstallResolverTests"
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Pipeline/InstallResolver.cs PlayniteDownloaderPlugin.Tests/Pipeline/InstallResolverTests.cs
git commit -m "feat: add InstallResolver with scoring to find game executable"
```

---

### Task 10: ExtractionService

**Files:**
- Create: `PlayniteDownloaderPlugin/Pipeline/ExtractionService.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Pipeline/ExtractionServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Pipeline/ExtractionServiceTests.cs
using System.IO.Compression;
using PlayniteDownloaderPlugin.Pipeline;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Pipeline;

public class ExtractionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public ExtractionServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateZip(string name, string entryName, byte[] content)
    {
        var zipPath = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(content);
        return zipPath;
    }

    [Fact]
    public async Task FindArchiveEntryPoint_ReturnsZipFile()
    {
        var zipPath = CreateZip("game.zip", "game.exe", new byte[] { 1, 2, 3 });

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Equal(zipPath, result);
    }

    [Fact]
    public async Task ExtractAsync_ExtractsZipContents()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var zipPath = CreateZip("game.zip", "game.exe", content);
        var outputDir = Path.Combine(_dir, "output");
        Directory.CreateDirectory(outputDir);
        var progressValues = new List<float>();

        await ExtractionService.ExtractAsync(zipPath, outputDir,
            p => progressValues.Add(p), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(outputDir, "game.exe")));
        Assert.NotEmpty(progressValues);
        Assert.Equal(1.0f, progressValues.Last());
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsPart1RarWhenPresent()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.part1.rar"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_dir, "game.part2.rar"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.part1.rar", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsRarWhenR00SiblingExists()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.rar"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_dir, "game.r00"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.rar", result);
        Assert.DoesNotContain("game.r00", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsTarGzWhenPresent()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.tar.gz"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.tar.gz", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsNullWhenNoArchives()
    {
        var result = ExtractionService.FindArchiveEntryPoint(_dir);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~ExtractionServiceTests"
```
Expected: Build error.

- [ ] **Step 3: Implement ExtractionService.cs**

```csharp
// PlayniteDownloaderPlugin/Pipeline/ExtractionService.cs
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PlayniteDownloaderPlugin.Pipeline;

public static class ExtractionService
{
    private static readonly string[] ExtractableExtensions =
        { ".zip", ".rar", ".7z", ".tar", ".tar.gz" };

    public static string? FindArchiveEntryPoint(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var files = Directory.GetFiles(directory);

        // 1. New-style multi-part RAR: game.part1.rar
        var part1 = files.FirstOrDefault(f =>
            f.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase));
        if (part1 != null) return part1;

        // 2. Old-style multi-part RAR: game.rar + game.r00
        var rarFile = files.FirstOrDefault(f =>
            f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) &&
            !f.Contains(".part", StringComparison.OrdinalIgnoreCase));
        if (rarFile != null)
        {
            var r00Sibling = Path.ChangeExtension(rarFile, ".r00");
            if (File.Exists(r00Sibling)) return rarFile;
        }

        // 3. Single-volume archives
        foreach (var ext in new[] { ".zip", ".rar", ".7z", ".tar", ".tar.gz" })
        {
            var found = files.FirstOrDefault(f =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        // 4. Recurse into subdirectories
        foreach (var sub in Directory.GetDirectories(directory))
        {
            var result = FindArchiveEntryPoint(sub);
            if (result != null) return result;
        }

        return null;
    }

    public static async Task ExtractAsync(
        string archivePath,
        string outputPath,
        Action<float> onProgress,
        CancellationToken ct)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        using var archive = ArchiveFactory.Open(archivePath);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        var total = entries.Count;
        var done = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            entry.WriteToDirectory(outputPath, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });
            done++;
            onProgress(done / (float)total);
        }

        await Task.CompletedTask; // keeps the method async for consistency
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~ExtractionServiceTests"
```
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Pipeline/ExtractionService.cs PlayniteDownloaderPlugin.Tests/Pipeline/ExtractionServiceTests.cs
git commit -m "feat: add ExtractionService with RAR/zip/7z support and archive entry-point detection"
```

---

### Task 11: DownloadQueue

**Files:**
- Create: `PlayniteDownloaderPlugin/Pipeline/DownloadQueue.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Pipeline/DownloadQueueTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Pipeline/DownloadQueueTests.cs
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Pipeline;

public class DownloadQueueTests : IDisposable
{
    private readonly string _stateDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DownloadQueueTests() => Directory.CreateDirectory(_stateDir);
    public void Dispose() => Directory.Delete(_stateDir, recursive: true);

    private QueueEntry MakeEntry(string gameId = "game1") => new()
    {
        GameId = gameId,
        GameName = "Test Game",
        OriginalUri = "https://cdn.example.com/game.zip",
        ResolvedUrls = new List<string> { "https://cdn.example.com/game.zip" },
        DownloadPath = _stateDir,
        ExtractionPath = _stateDir
    };

    [Fact]
    public void Enqueue_AddsEntryWithWaitingStatus()
    {
        var queue = new DownloadQueue(_stateDir);
        var entry = MakeEntry();

        queue.Enqueue(entry);

        Assert.Single(queue.GetAll());
        Assert.Equal(DownloadStatus.Waiting, queue.GetAll()[0].Status);
    }

    [Fact]
    public void Cancel_RemovesEntryFromQueue()
    {
        var queue = new DownloadQueue(_stateDir);
        var entry = MakeEntry();
        queue.Enqueue(entry);

        queue.Cancel(entry.Id);

        Assert.Empty(queue.GetAll());
    }

    [Fact]
    public void Persist_SavesQueueToJson()
    {
        var queue = new DownloadQueue(_stateDir);
        queue.Enqueue(MakeEntry("g1"));
        queue.Enqueue(MakeEntry("g2"));

        queue.Persist();

        var jsonPath = Path.Combine(_stateDir, "queue.json");
        Assert.True(File.Exists(jsonPath));
        var loaded = DownloadQueue.LoadFrom(_stateDir);
        Assert.Equal(2, loaded.GetAll().Count);
    }

    [Fact]
    public void GetAll_ReturnsEntriesInAddOrder()
    {
        var queue = new DownloadQueue(_stateDir);
        queue.Enqueue(MakeEntry("first"));
        queue.Enqueue(MakeEntry("second"));

        var entries = queue.GetAll();

        Assert.Equal("first", entries[0].GameId);
        Assert.Equal("second", entries[1].GameId);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~DownloadQueueTests"
```
Expected: Build error.

- [ ] **Step 3: Implement DownloadQueue.cs**

```csharp
// PlayniteDownloaderPlugin/Pipeline/DownloadQueue.cs
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Pipeline;

public class DownloadQueue
{
    private readonly string _stateDir;
    private readonly List<QueueEntry> _entries = new();
    private readonly object _lock = new();

    public DownloadQueue(string stateDir) => _stateDir = stateDir;

    public static DownloadQueue LoadFrom(string stateDir)
    {
        var queue = new DownloadQueue(stateDir);
        var path = Path.Combine(stateDir, "queue.json");
        if (!File.Exists(path)) return queue;

        var json = File.ReadAllText(path);
        var entries = JsonConvert.DeserializeObject<List<QueueEntry>>(json) ?? new();
        // Resume incomplete entries as Waiting on load
        foreach (var e in entries)
        {
            if (e.Status == DownloadStatus.Active || e.Status == DownloadStatus.Extracting
                || e.Status == DownloadStatus.Paused)
                e.Status = DownloadStatus.Waiting;
        }
        queue._entries.AddRange(entries);
        return queue;
    }

    public void Enqueue(QueueEntry entry)
    {
        lock (_lock)
        {
            entry.Status = DownloadStatus.Waiting;
            _entries.Add(entry);
        }
        Persist();
    }

    public void Cancel(string entryId)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null) _entries.Remove(entry);
        }
        Persist();
    }

    public QueueEntry? Dequeue()
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.Status == DownloadStatus.Waiting);
        }
    }

    public void UpdateEntry(QueueEntry updated)
    {
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => e.Id == updated.Id);
            if (idx >= 0) _entries[idx] = updated;
        }
        Persist();
    }

    public IReadOnlyList<QueueEntry> GetAll()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Persist()
    {
        var path = Path.Combine(_stateDir, "queue.json");
        lock (_lock)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(_entries, Formatting.Indented));
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~DownloadQueueTests"
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Pipeline/DownloadQueue.cs PlayniteDownloaderPlugin.Tests/Pipeline/DownloadQueueTests.cs
git commit -m "feat: add DownloadQueue with persistence and state management"
```

---

## Chunk 5: Integration & Plugin Entry

### Task 12: PlayniteIntegration

**Files:**
- Create: `PlayniteDownloaderPlugin/Integration/PlayniteIntegration.cs`
- Create: `PlayniteDownloaderPlugin.Tests/Integration/PlayniteIntegrationTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PlayniteDownloaderPlugin.Tests/Integration/PlayniteIntegrationTests.cs
using Moq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteDownloaderPlugin.Integration;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Integration;

public class PlayniteIntegrationTests
{
    [Fact]
    public void MarkInstalled_SetsIsInstalledAndDirectory()
    {
        var game = new Game { Id = Guid.NewGuid(), Name = "Test Game" };
        var mockDb = new Mock<IGameDatabase>();
        mockDb.Setup(db => db.Games.Get(game.Id)).Returns(game);
        var mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        var integration = new PlayniteIntegration(mockApi.Object);
        integration.MarkInstalled(game.Id.ToString(), @"C:\Games\TestGame", @"C:\Games\TestGame\game.exe");

        Assert.True(game.IsInstalled);
        Assert.Equal(@"C:\Games\TestGame", game.InstallDirectory);
        Assert.Single(game.GameActions);
        Assert.True(game.GameActions[0].IsPlayAction);
        Assert.Equal(@"C:\Games\TestGame\game.exe", game.GameActions[0].Path);
        mockDb.Verify(db => db.Games.Update(game), Times.Once);
    }

    [Fact]
    public void MarkInstalled_WithNoExe_SetsInstalledWithEmptyActions()
    {
        var game = new Game { Id = Guid.NewGuid(), Name = "Test Game" };
        var mockDb = new Mock<IGameDatabase>();
        mockDb.Setup(db => db.Games.Get(game.Id)).Returns(game);
        var mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        var integration = new PlayniteIntegration(mockApi.Object);
        integration.MarkInstalled(game.Id.ToString(), @"C:\Games\TestGame", null);

        Assert.True(game.IsInstalled);
        Assert.Empty(game.GameActions ?? new List<GameAction>());
        mockDb.Verify(db => db.Games.Update(game), Times.Once);
    }

    [Fact]
    public void MarkInstalled_DoesNothingWhenGameNotFound()
    {
        var mockDb = new Mock<IGameDatabase>();
        mockDb.Setup(db => db.Games.Get(It.IsAny<Guid>())).Returns((Game?)null);
        var mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        var integration = new PlayniteIntegration(mockApi.Object);

        // Should not throw
        integration.MarkInstalled(Guid.NewGuid().ToString(), @"C:\Games\Game", null);

        mockDb.Verify(db => db.Games.Update(It.IsAny<Game>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~PlayniteIntegrationTests"
```
Expected: Build error.

- [ ] **Step 3: Implement PlayniteIntegration.cs**

```csharp
// PlayniteDownloaderPlugin/Integration/PlayniteIntegration.cs
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteDownloaderPlugin.Integration;

public class PlayniteIntegration
{
    private readonly IPlayniteAPI _api;

    public PlayniteIntegration(IPlayniteAPI api) => _api = api;

    public void MarkInstalled(string gameId, string installDirectory, string? executablePath)
    {
        if (!Guid.TryParse(gameId, out var guid)) return;

        var game = _api.Database.Games.Get(guid);
        if (game == null)
        {
            // Log warning — game was removed from library before download finished
            return;
        }

        game.IsInstalled = true;
        game.InstallDirectory = installDirectory;

        if (executablePath != null)
        {
            game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>
            {
                new GameAction
                {
                    Name = "Play",
                    Path = executablePath,
                    Type = GameActionType.File,
                    IsPlayAction = true
                }
            };
        }
        else
        {
            game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>();
        }

        _api.Database.Games.Update(game);
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --filter "FullyQualifiedName~PlayniteIntegrationTests"
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/Integration/ PlayniteDownloaderPlugin.Tests/Integration/
git commit -m "feat: add PlayniteIntegration to mark games as installed with play action"
```

---

### Task 13: DownloadPipelineRunner and Plugin Entry

**Files:**
- Create: `PlayniteDownloaderPlugin/Pipeline/DownloadPipelineRunner.cs`
- Create: `PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.cs`

- [ ] **Step 1: Create DownloadPipelineRunner.cs**

This ties the queue to the download/extract/install pipeline in a background loop.

```csharp
// PlayniteDownloaderPlugin/Pipeline/DownloadPipelineRunner.cs
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Integration;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Pipeline;

public class DownloadPipelineRunner
{
    private readonly DownloadQueue _queue;
    private readonly PlayniteIntegration _integration;
    private readonly UserConfig _config;
    private CancellationTokenSource _cts = new();
    private Task? _workerTask;

    public event Action<QueueEntry>? EntryUpdated;

    public DownloadPipelineRunner(
        DownloadQueue queue, PlayniteIntegration integration, UserConfig config)
    {
        _queue = queue;
        _integration = integration;
        _config = config;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        _workerTask?.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var entry = _queue.Dequeue();
            if (entry == null)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            await ProcessEntry(entry, ct);
        }
    }

    private async Task ProcessEntry(QueueEntry entry, CancellationToken ct)
    {
        try
        {
            // Resolve URLs if not yet resolved
            if (!entry.ResolvedUrls.Any())
            {
                var (urls, _) = await DownloaderFactory.ResolveUrlsAsync(
                    entry.OriginalUri, _config, ct);
                entry.ResolvedUrls = urls;
            }

            entry.Status = DownloadStatus.Active;
            Notify(entry);

            // Download each part sequentially
            for (var i = entry.CurrentUrlIndex; i < entry.ResolvedUrls.Count; i++)
            {
                entry.CurrentUrlIndex = i;
                var downloader = new HttpDownloader();
                downloader.ProgressChanged += p =>
                {
                    entry.BytesDownloaded = p.BytesDownloaded;
                    entry.FileSize = p.FileSize;
                    entry.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
                    entry.Progress = entry.FileSize > 0
                        ? (float)entry.BytesDownloaded / entry.FileSize : 0;
                    _queue.UpdateEntry(entry);
                    Notify(entry);
                };

                await downloader.StartAsync(entry.ResolvedUrls[i], entry.DownloadPath, ct);
            }

            // Extract
            entry.Status = DownloadStatus.Extracting;
            entry.ExtractionProgress = 0;
            Notify(entry);

            var archivePath = ExtractionService.FindArchiveEntryPoint(entry.DownloadPath);
            if (archivePath != null)
            {
                await ExtractionService.ExtractAsync(archivePath, entry.ExtractionPath,
                    p =>
                    {
                        entry.ExtractionProgress = p;
                        _queue.UpdateEntry(entry);
                        Notify(entry);
                    }, ct);
            }

            // Find exe and mark installed
            var exePath = InstallResolver.FindExecutable(entry.ExtractionPath);
            _integration.MarkInstalled(entry.GameId, entry.ExtractionPath, exePath);

            entry.Status = DownloadStatus.Complete;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
        catch (OperationCanceledException)
        {
            entry.Status = DownloadStatus.Paused;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
        catch (Exception ex)
        {
            entry.Status = DownloadStatus.Error;
            entry.ErrorMessage = ex.Message;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
    }

    private void Notify(QueueEntry entry) => EntryUpdated?.Invoke(entry);
}
```

- [ ] **Step 2: Create PlayniteDownloaderPlugin.cs**

```csharp
// PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.cs
using System.Reflection;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteDownloaderPlugin.Integration;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;
using PlayniteDownloaderPlugin.UI;

namespace PlayniteDownloaderPlugin;

public class PlayniteDownloaderPlugin : GenericPlugin
{
    public override Guid Id { get; } = Guid.Parse("a3b4c5d6-e7f8-9012-abcd-ef1234567890");

    private readonly DownloadQueue _queue;
    private readonly SourceManager _sourceManager;
    private readonly DownloadPipelineRunner _runner;
    private readonly UserConfig _config;
    private SidePanel? _sidePanel;

    public PlayniteDownloaderPlugin(IPlayniteAPI api) : base(api)
    {
        var dataPath = GetPluginUserDataPath();
        _config = LoadConfig(dataPath);
        _queue = DownloadQueue.LoadFrom(dataPath);
        _sourceManager = BuildSourceManager();
        var integration = new PlayniteIntegration(api);
        _runner = new DownloadPipelineRunner(_queue, integration, _config);
    }

    public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
    {
        _runner.Start();
    }

    public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
    {
        _runner.Stop();
    }

    public override IEnumerable<SidebarItem> GetSidebarItems()
    {
        yield return new SidebarItem
        {
            Title = "Downloader",
            Type = SiderbarItemType.View,
            Icon = new System.Windows.Controls.TextBlock { Text = "⬇" },
            Opened = () =>
            {
                _sidePanel ??= new SidePanel(_queue, _sourceManager, _config,
                    SaveConfig, _runner);
                return _sidePanel;
            }
        };
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        yield return new GameMenuItem
        {
            Description = "Search Downloads",
            Action = _ =>
            {
                var game = args.Games.First();
                var dialog = new SearchDialog(game, _sourceManager, _queue, _config, PlayniteApi);
                dialog.ShowDialog();
            }
        };
    }

    private UserConfig LoadConfig(string dataPath)
    {
        var path = Path.Combine(dataPath, "config.json");
        if (!File.Exists(path)) return new UserConfig();
        return JsonConvert.DeserializeObject<UserConfig>(File.ReadAllText(path)) ?? new UserConfig();
    }

    private void SaveConfig()
    {
        var path = Path.Combine(GetPluginUserDataPath(), "config.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_config, Formatting.Indented));
    }

    private SourceManager BuildSourceManager()
    {
        var builtinPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "builtin-sources.json");
        var builtinEntries = File.Exists(builtinPath)
            ? JsonConvert.DeserializeObject<List<SourceEntry>>(File.ReadAllText(builtinPath))
              ?? new List<SourceEntry>()
            : new List<SourceEntry>();

        var providers = builtinEntries
            .Where(e => e.Enabled)
            .Select(e => (ISourceProvider)new JsonSourceProvider(e.Id, e.Name, e.Url));

        var manager = new SourceManager(providers);

        // Load custom sources
        var customPath = Path.Combine(GetPluginUserDataPath(), "sources.json");
        if (File.Exists(customPath))
        {
            var customs = JsonConvert.DeserializeObject<List<SourceEntry>>(
                File.ReadAllText(customPath)) ?? new();
            foreach (var c in customs.Where(c => c.IsCustom))
                try { manager.AddCustomSource(c.Name, c.Url); } catch { /* skip dupes */ }
        }

        return manager;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors (UI classes will be missing until Task 14 — create stubs if needed).

- [ ] **Step 4: Commit**

```bash
git add PlayniteDownloaderPlugin/Pipeline/DownloadPipelineRunner.cs PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.cs
git commit -m "feat: add DownloadPipelineRunner and main plugin entry point"
```

---

## Chunk 6: UI Layer

### Task 14: SidePanelViewModel

**Files:**
- Create: `PlayniteDownloaderPlugin/UI/SidePanelViewModel.cs`
- Create: `PlayniteDownloaderPlugin.Tests/UI/SidePanelViewModelTests.cs` *(optional — MVVM logic only)*

- [ ] **Step 1: Create SidePanelViewModel.cs**

```csharp
// PlayniteDownloaderPlugin/UI/SidePanelViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI;

public class SidePanelViewModel : INotifyPropertyChanged
{
    private readonly DownloadQueue _queue;
    private readonly SourceManager _sourceManager;
    private readonly UserConfig _config;
    private readonly Action _saveConfig;
    private readonly DownloadPipelineRunner _runner;

    public ObservableCollection<QueueEntry> Entries { get; } = new();
    public ObservableCollection<SourceEntry> Sources { get; } = new();

    private string _rdToken = string.Empty;
    public string RdToken
    {
        get => _rdToken;
        set { _rdToken = value; OnPropertyChanged(); }
    }

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set { _downloadPath = value; OnPropertyChanged(); }
    }

    private string _newSourceUrl = string.Empty;
    public string NewSourceUrl
    {
        get => _newSourceUrl;
        set { _newSourceUrl = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand CancelDownloadCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand RemoveSourceCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public SidePanelViewModel(
        DownloadQueue queue,
        SourceManager sourceManager,
        UserConfig config,
        Action saveConfig,
        DownloadPipelineRunner runner)
    {
        _queue = queue;
        _sourceManager = sourceManager;
        _config = config;
        _saveConfig = saveConfig;
        _runner = runner;

        _rdToken = config.RealDebridApiToken;
        _downloadPath = config.DefaultDownloadPath;

        CancelDownloadCommand = new RelayCommand<string>(id =>
        {
            _queue.Cancel(id);
            Refresh();
        });

        AddSourceCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(NewSourceUrl)) return;
            try
            {
                _sourceManager.AddCustomSource("Custom Source", NewSourceUrl);
                NewSourceUrl = string.Empty;
                StatusMessage = "Source added.";
                RefreshSources();
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        });

        RemoveSourceCommand = new RelayCommand<string>(id =>
        {
            _sourceManager.RemoveCustomSource(id);
            RefreshSources();
        });

        SaveSettingsCommand = new RelayCommand(_ =>
        {
            _config.RealDebridApiToken = RdToken;
            _config.DefaultDownloadPath = DownloadPath;
            _config.RealDebridEnabled = !string.IsNullOrEmpty(RdToken);
            _saveConfig();
            StatusMessage = "Settings saved.";
        });

        _runner.EntryUpdated += _ => App.Current?.Dispatcher.BeginInvoke(Refresh);
        Refresh();
        RefreshSources();
    }

    public void Refresh()
    {
        Entries.Clear();
        foreach (var e in _queue.GetAll())
            Entries.Add(e);
    }

    private void RefreshSources()
    {
        Sources.Clear();
        foreach (var s in _sourceManager.GetAllSources())
            Sources.Add(s);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommand(Action<object?> execute) => _execute = execute;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _execute(p);
    public event EventHandler? CanExecuteChanged;
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _execute(p is T t ? t : default);
    public event EventHandler? CanExecuteChanged;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors (SidePanel XAML not yet created — next task).

- [ ] **Step 3: Commit**

```bash
git add PlayniteDownloaderPlugin/UI/SidePanelViewModel.cs
git commit -m "feat: add SidePanelViewModel with queue binding and settings commands"
```

---

### Task 15: SidePanel XAML

**Files:**
- Create: `PlayniteDownloaderPlugin/UI/SidePanel.xaml`
- Create: `PlayniteDownloaderPlugin/UI/SidePanel.xaml.cs`

- [ ] **Step 1: Create SidePanel.xaml**

```xml
<!-- PlayniteDownloaderPlugin/UI/SidePanel.xaml -->
<UserControl x:Class="PlayniteDownloaderPlugin.UI.SidePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             MinWidth="300">
    <TabControl>
        <!-- Downloads Tab -->
        <TabItem Header="Downloads">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <!-- Active download -->
                <StackPanel Grid.Row="0" Margin="8">
                    <TextBlock Text="Active Download" FontWeight="Bold" Margin="0,0,0,4"/>
                    <ItemsControl ItemsSource="{Binding Entries}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border BorderBrush="Gray" BorderThickness="1"
                                        CornerRadius="4" Padding="8" Margin="0,4">
                                    <Grid>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto"/>
                                            <RowDefinition Height="Auto"/>
                                            <RowDefinition Height="Auto"/>
                                        </Grid.RowDefinitions>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>

                                        <TextBlock Grid.Row="0" Grid.Column="0"
                                                   Text="{Binding GameName}"
                                                   FontWeight="SemiBold"/>
                                        <Button Grid.Row="0" Grid.Column="1"
                                                Content="✕"
                                                Command="{Binding DataContext.CancelDownloadCommand,
                                                    RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding Id}"/>

                                        <ProgressBar Grid.Row="1" Grid.ColumnSpan="2"
                                                     Value="{Binding Progress, Mode=OneWay}"
                                                     Maximum="1" Height="8" Margin="0,4"/>

                                        <TextBlock Grid.Row="2" Grid.ColumnSpan="2"
                                                   FontSize="11" Foreground="Gray">
                                            <Run Text="{Binding Status, Mode=OneWay}"/>
                                            <Run Text=" — "/>
                                            <Run Text="{Binding ErrorMessage, Mode=OneWay}"/>
                                        </TextBlock>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Grid>
        </TabItem>

        <!-- Settings Tab -->
        <TabItem Header="Settings">
            <ScrollViewer>
                <StackPanel Margin="12">
                    <TextBlock Text="Real-Debrid API Token" FontWeight="Bold" Margin="0,0,0,4"/>
                    <PasswordBox x:Name="RdTokenBox" Margin="0,0,0,8"
                                 PasswordChanged="RdTokenBox_PasswordChanged"/>

                    <TextBlock Text="Default Download Path" FontWeight="Bold" Margin="0,0,0,4"/>
                    <TextBox Text="{Binding DownloadPath, UpdateSourceTrigger=PropertyChanged}"
                             Margin="0,0,0,8"/>

                    <Button Content="Save Settings"
                            Command="{Binding SaveSettingsCommand}"
                            HorizontalAlignment="Left" Margin="0,0,0,12"/>

                    <TextBlock Text="{Binding StatusMessage}" Foreground="Green"/>

                    <Separator Margin="0,12"/>

                    <TextBlock Text="Sources" FontWeight="Bold" Margin="0,0,0,8"/>
                    <ItemsControl ItemsSource="{Binding Sources}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                                    <Button Grid.Column="1" Content="Remove"
                                            Command="{Binding DataContext.RemoveSourceCommand,
                                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding Id}"
                                            Visibility="{Binding IsCustom, Converter={StaticResource BoolToVis}}"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock Text="Add Custom Source URL" FontWeight="Bold" Margin="0,12,0,4"/>
                    <TextBox Text="{Binding NewSourceUrl, UpdateSourceTrigger=PropertyChanged}"
                             Margin="0,0,0,4"/>
                    <Button Content="Add Source"
                            Command="{Binding AddSourceCommand}"
                            HorizontalAlignment="Left"/>
                </StackPanel>
            </ScrollViewer>
        </TabItem>
    </TabControl>
</UserControl>
```

- [ ] **Step 2: Create SidePanel.xaml.cs**

```csharp
// PlayniteDownloaderPlugin/UI/SidePanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI;

public partial class SidePanel : UserControl
{
    public SidePanel(
        DownloadQueue queue,
        SourceManager sourceManager,
        UserConfig config,
        Action saveConfig,
        DownloadPipelineRunner runner)
    {
        InitializeComponent();
        DataContext = new SidePanelViewModel(queue, sourceManager, config, saveConfig, runner);
        // Populate PasswordBox from config (PasswordBox.Password does not support data binding)
        RdTokenBox.Password = config.RealDebridApiToken;
    }

    private void RdTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SidePanelViewModel vm)
            vm.RdToken = RdTokenBox.Password;
    }
}
```

- [ ] **Step 3: Add BoolToVisibilityConverter resource — add to App.xaml or inline**

If no App.xaml exists (plugin doesn't need one), add the converter inline in SidePanel.xaml:

```xml
<!-- Add inside <UserControl.Resources> at top of SidePanel.xaml -->
<UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis"/>
</UserControl.Resources>
```

- [ ] **Step 4: Build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add PlayniteDownloaderPlugin/UI/SidePanel.xaml PlayniteDownloaderPlugin/UI/SidePanel.xaml.cs
git commit -m "feat: add SidePanel WPF UserControl with queue display and settings"
```

---

### Task 16: SearchDialogViewModel

**Files:**
- Create: `PlayniteDownloaderPlugin/UI/SearchDialogViewModel.cs`

> **Note:** `RelayCommand` and `RelayCommand<T>` are defined at the bottom of `SidePanelViewModel.cs` (Task 14). They are available to `SearchDialogViewModel` since they share the same namespace. Do **not** redefine them here — it will cause a duplicate-type build error.

- [ ] **Step 1: Create SearchDialogViewModel.cs**

```csharp
// PlayniteDownloaderPlugin/UI/SearchDialogViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI;

public class SearchDialogViewModel : INotifyPropertyChanged
{
    private readonly SourceManager _sourceManager;
    private readonly DownloadQueue _queue;
    private readonly UserConfig _config;
    private readonly string _gameId;
    private CancellationTokenSource _searchCts = new();

    public ObservableCollection<DownloadResult> Results { get; } = new();
    public ObservableCollection<string> SelectedUris { get; } = new();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set { _downloadPath = value; OnPropertyChanged(); }
    }

    private DownloadResult? _selectedResult;
    public DownloadResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            _selectedResult = value;
            OnPropertyChanged();
            SelectedUris.Clear();
            if (value != null)
                foreach (var uri in value.Uris)
                    SelectedUris.Add(uri);
            SelectedUri = SelectedUris.FirstOrDefault();
        }
    }

    private string? _selectedUri;
    public string? SelectedUri
    {
        get => _selectedUri;
        set { _selectedUri = value; OnPropertyChanged(); }
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set { _isSearching = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand SearchCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand BrowseCommand { get; }

    public Action? CloseDialog { get; set; }

    private readonly IPlayniteAPI _playniteApi;

    public SearchDialogViewModel(
        string gameId,
        string gameName,
        SourceManager sourceManager,
        DownloadQueue queue,
        UserConfig config,
        IPlayniteAPI playniteApi)
    {
        _gameId = gameId;
        _sourceManager = sourceManager;
        _queue = queue;
        _config = config;
        _searchText = gameName;
        _downloadPath = config.DefaultDownloadPath;
        _playniteApi = playniteApi;

        SearchCommand = new RelayCommand(async _ =>
        {
            _searchCts.Cancel();
            _searchCts = new CancellationTokenSource();
            IsSearching = true;
            Results.Clear();
            StatusMessage = string.Empty;
            try
            {
                var results = await _sourceManager.SearchAllAsync(SearchText, _searchCts.Token);
                foreach (var r in results) Results.Add(r);
                if (!results.Any()) StatusMessage = "No results found.";
            }
            catch (OperationCanceledException) { }
            finally { IsSearching = false; }
        });

        DownloadCommand = new RelayCommand(async _ =>
        {
            if (SelectedUri == null)
            {
                StatusMessage = "Please select a download link.";
                return;
            }
            if (string.IsNullOrEmpty(DownloadPath))
            {
                StatusMessage = "Please set a download path.";
                return;
            }

            try
            {
                var (urls, _) = await DownloaderFactory.ResolveUrlsAsync(
                    SelectedUri, _config, CancellationToken.None);

                var gameName = SelectedResult?.Title ?? SearchText;
                // Archives are downloaded into extractionPath; extraction output goes to
                // the same folder — FindArchiveEntryPoint will locate the archive there.
                var extractionPath = Path.Combine(DownloadPath, SanitizePath(gameName));

                var entry = new QueueEntry
                {
                    GameId = _gameId,
                    GameName = gameName,
                    OriginalUri = SelectedUri,
                    ResolvedUrls = urls,
                    DownloadPath = extractionPath,
                    ExtractionPath = extractionPath
                };

                _queue.Enqueue(entry);
                CloseDialog?.Invoke();
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        });

        BrowseCommand = new RelayCommand(_ =>
        {
            // Use Playnite's built-in folder dialog (no System.Windows.Forms dependency)
            var selected = _playniteApi.Dialogs.SelectFolder();
            if (!string.IsNullOrEmpty(selected))
                DownloadPath = selected;
        });
    }

    private static string SanitizePath(string name)
        => string.Concat(name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 2: Build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
```
Expected: 0 errors (SearchDialog XAML not yet created — add reference after next step).

- [ ] **Step 3: Commit**

```bash
git add PlayniteDownloaderPlugin/UI/SearchDialogViewModel.cs
git commit -m "feat: add SearchDialogViewModel with search, URI selection, and enqueue logic"
```

---

### Task 17: SearchDialog XAML

**Files:**
- Create: `PlayniteDownloaderPlugin/UI/SearchDialog.xaml`
- Create: `PlayniteDownloaderPlugin/UI/SearchDialog.xaml.cs`

- [ ] **Step 1: Create SearchDialog.xaml**

```xml
<!-- PlayniteDownloaderPlugin/UI/SearchDialog.xaml -->
<Window x:Class="PlayniteDownloaderPlugin.UI.SearchDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Search Downloads" Width="700" Height="550"
        WindowStartupLocation="CenterScreen">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Search bar -->
        <Grid Grid.Row="0" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                     FontSize="14" Padding="4"/>
            <Button Grid.Column="1" Content="Search" Margin="8,0,0,0" Padding="12,4"
                    Command="{Binding SearchCommand}"/>
        </Grid>

        <!-- Progress indicator -->
        <ProgressBar Grid.Row="1" IsIndeterminate="True" Height="4" Margin="0,0,0,8"
                     Visibility="{Binding IsSearching, Converter={StaticResource BoolToVis}}"/>

        <!-- Results -->
        <DataGrid Grid.Row="2" ItemsSource="{Binding Results}"
                  SelectedItem="{Binding SelectedResult}"
                  AutoGenerateColumns="False" IsReadOnly="True"
                  SelectionMode="Single" Margin="0,0,0,8">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Title" Binding="{Binding Title}" Width="*"/>
                <DataGridTextColumn Header="Source" Binding="{Binding SourceName}" Width="120"/>
                <DataGridTextColumn Header="Size" Binding="{Binding FileSize}" Width="80"/>
                <DataGridTextColumn Header="Date" Binding="{Binding UploadDate}" Width="90"/>
                <DataGridTextColumn Header="Links" Binding="{Binding Uris.Count}" Width="50"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- URI selection -->
        <StackPanel Grid.Row="3" Margin="0,0,0,8">
            <TextBlock Text="Select link:" FontWeight="Bold" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding SelectedUris}"
                      SelectedItem="{Binding SelectedUri}"/>
        </StackPanel>

        <!-- Download path -->
        <Grid Grid.Row="4" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding DownloadPath, UpdateSourceTrigger=PropertyChanged}"
                     Padding="4"/>
            <Button Grid.Column="1" Content="Browse..." Margin="8,0,0,0"
                    Command="{Binding BrowseCommand}"/>
        </Grid>

        <!-- Bottom bar -->
        <Grid Grid.Row="5">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="{Binding StatusMessage}" Foreground="OrangeRed"
                       VerticalAlignment="Center"/>
            <Button Grid.Column="1" Content="Download" Padding="16,6"
                    Command="{Binding DownloadCommand}"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create SearchDialog.xaml.cs**

```csharp
// PlayniteDownloaderPlugin/UI/SearchDialog.xaml.cs
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;
using System.Windows;

namespace PlayniteDownloaderPlugin.UI;

public partial class SearchDialog : Window
{
    public SearchDialog(Game game, SourceManager sourceManager,
        DownloadQueue queue, UserConfig config, IPlayniteAPI playniteApi)
    {
        InitializeComponent();
        var vm = new SearchDialogViewModel(
            game.Id.ToString(), game.Name, sourceManager, queue, config, playniteApi);
        vm.CloseDialog = Close;
        DataContext = vm;
    }
}
```

- [ ] **Step 3: Final build**

```bash
dotnet build PlayniteDownloaderPlugin/PlayniteDownloaderPlugin.csproj
dotnet test PlayniteDownloaderPlugin.Tests/
```
Expected: 0 build errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add PlayniteDownloaderPlugin/UI/SearchDialog.xaml PlayniteDownloaderPlugin/UI/SearchDialog.xaml.cs
git commit -m "feat: add SearchDialog WPF Window with results table, URI picker, and download enqueue"
```

---

## Final Verification

- [ ] **Run all tests**

```bash
dotnet test PlayniteDownloaderPlugin.Tests/ --verbosity normal
```
Expected: All tests pass, 0 failures.

- [ ] **Build release**

```bash
dotnet publish PlayniteDownloaderPlugin/ -c Release -o out/
```
Expected: `PlayniteDownloaderPlugin.dll` and supporting files in `out/`.

- [ ] **Manual smoke test in Playnite**
  1. Copy `out/` contents to `%AppData%\Playnite\Extensions\PlayniteDownloader\`
  2. Start Playnite — plugin sidebar item "Downloader" should appear
  3. Right-click any game → "Search Downloads" — dialog opens with game name pre-filled
  4. Search returns results from enabled sources
  5. Select a result, pick a direct HTTP link, click Download — appears in sidebar queue
  6. Verify download starts and progress updates in sidebar
