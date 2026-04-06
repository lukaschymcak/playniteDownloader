using Moq;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Source;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Source;

public class SourceManagerTests
{
    private static ISourceProvider MakeProvider(string id, List<DownloadResult> results)
    {
        Mock<ISourceProvider> mock = new Mock<ISourceProvider>();
        mock.Setup(p => p.Id).Returns(id);
        mock.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        return mock.Object;
    }

    [Fact]
    public async Task SearchAllAsync_AggregatesResultsFromAllProviders()
    {
        DownloadResult r1 = new DownloadResult { Title = "Game A", MatchScore = 0.9f };
        DownloadResult r2 = new DownloadResult { Title = "Game A v2", MatchScore = 0.7f };
        SourceManager manager = new SourceManager(new[]
        {
            MakeProvider("p1", new List<DownloadResult> { r1 }),
            MakeProvider("p2", new List<DownloadResult> { r2 }),
        });

        List<DownloadResult> results = await manager.SearchAllAsync("Game A", CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAllAsync_ReturnsSortedByMatchScoreDescending()
    {
        DownloadResult r1 = new DownloadResult { Title = "Game A", MatchScore = 0.7f };
        DownloadResult r2 = new DownloadResult { Title = "Game A Plus", MatchScore = 0.95f };
        SourceManager manager = new SourceManager(new[]
        {
            MakeProvider("p1", new List<DownloadResult> { r1, r2 }),
        });

        List<DownloadResult> results = await manager.SearchAllAsync("Game A", CancellationToken.None);

        Assert.Equal(0.95f, results[0].MatchScore);
        Assert.Equal(0.7f, results[1].MatchScore);
    }

    [Fact]
    public void AddCustomSource_AddsToSources()
    {
        SourceManager manager = new SourceManager(Array.Empty<ISourceProvider>());

        manager.AddCustomSource("My Source", "https://example.com/source.json");

        Assert.Single(manager.GetAllSources());
    }

    [Fact]
    public void AddCustomSource_ThrowsOnDuplicateUrl()
    {
        SourceManager manager = new SourceManager(Array.Empty<ISourceProvider>());
        manager.AddCustomSource("Source", "https://example.com/source.json");

        Assert.Throws<InvalidOperationException>(
            () => manager.AddCustomSource("Source2", "https://example.com/source.json"));
    }

    [Fact]
    public void RemoveCustomSource_RemovesById()
    {
        SourceManager manager = new SourceManager(Array.Empty<ISourceProvider>());
        manager.AddCustomSource("My Source", "https://example.com/source.json");
        string id = manager.GetAllSources().First().Id;

        manager.RemoveCustomSource(id);

        Assert.Empty(manager.GetAllSources());
    }

    [Fact]
    public void CustomSources_PersistedAndReloadedAcrossInstances()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            SourceManager manager1 = new SourceManager(Array.Empty<ISourceProvider>(),
                persistPath: tempDir);
            manager1.AddCustomSource("Saved Source", "https://example.com/s.json");

            SourceManager manager2 = new SourceManager(Array.Empty<ISourceProvider>(),
                persistPath: tempDir);
            IReadOnlyList<SourceEntry> sources = manager2.GetCustomSources();

            Assert.Single(sources);
            Assert.Equal("Saved Source", sources[0].Name);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
