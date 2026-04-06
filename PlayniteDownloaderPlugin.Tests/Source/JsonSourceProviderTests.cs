using System.Net;
using System.Net.Http;
using Moq;
using PlayniteDownloaderPlugin.Models;
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

        List<DownloadResult> results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

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

        List<DownloadResult> results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

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

        List<DownloadResult> results = await provider.SearchAsync("Cyberpunk 2077", CancellationToken.None);

        Assert.Single(results);
        Assert.True(results[0].MatchScore > 0.5f);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenHttpFails()
    {
        var handler = new FailingHttpMessageHandler();
        var provider = new JsonSourceProvider("src1", "TestSource",
            "http://example.com/source.json", new HttpClient(handler));

        List<DownloadResult> results = await provider.SearchAsync("Any Game", CancellationToken.None);

        Assert.Empty(results);
    }
}

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
