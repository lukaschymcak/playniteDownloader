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
