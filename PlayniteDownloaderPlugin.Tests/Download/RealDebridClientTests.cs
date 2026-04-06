using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Download;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class RealDebridClientTests
{
    private static HttpClient MakeClient(params (string url, string json)[] responses)
    {
        MultiRouteHandler handler = new MultiRouteHandler(responses);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task GetDownloadUrl_DirectLink_UnrestrictsAndReturnsUrl()
    {
        string unrestrict = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/file.zip" });
        RealDebridClient client = new RealDebridClient("test-token",
            MakeClient(("/unrestrict/link", unrestrict)));

        List<string> urls = await client.GetDownloadUrlAsync(
            "https://1fichier.com/?abc123", CancellationToken.None);

        Assert.Single(urls);
        Assert.Equal("https://cdn.example.com/file.zip", urls[0]);
    }

    [Fact]
    public async Task GetDownloadUrl_Magnet_PollsUntilDownloadedAndReturnsAllLinks()
    {
        string addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        string torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "downloaded",
            Links = new List<string> { "https://rd.com/link1", "https://rd.com/link2" }
        });
        string unrestrict1 = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/part1.rar" });
        string unrestrict2 = JsonConvert.SerializeObject(
            new RdUnrestrictResponse { Download = "https://cdn.example.com/part2.rar" });

        RealDebridClient client = new RealDebridClient("test-token", MakeClient(
            ("/torrents/addMagnet", addMagnet),
            ("/torrents/selectFiles/tor1", "{}"),
            ("/torrents/info/tor1", torrentInfo),
            ("/unrestrict/link", unrestrict1),
            ("/unrestrict/link", unrestrict2)
        ));

        List<string> urls = await client.GetDownloadUrlAsync("magnet:?xt=test", CancellationToken.None);

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://cdn.example.com/part1.rar", urls[0]);
        Assert.Equal("https://cdn.example.com/part2.rar", urls[1]);
    }

    [Fact]
    public async Task GetDownloadUrl_Magnet_ThrowsWhenStatusIsError()
    {
        string addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        string torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "error", Links = new List<string>()
        });

        RealDebridClient client = new RealDebridClient("test-token", MakeClient(
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
        string addMagnet = JsonConvert.SerializeObject(new RdAddMagnetResponse { Id = "tor1" });
        string torrentInfo = JsonConvert.SerializeObject(new RdTorrentInfo
        {
            Id = "tor1", Status = "downloading", Links = new List<string>()
        });

        InfiniteHandler handler = new InfiniteHandler(new[]
        {
            ("/torrents/addMagnet", addMagnet),
            ("/torrents/selectFiles/tor1", "{}"),
        }, torrentInfo);

        RealDebridClient client = new RealDebridClient("test-token", new HttpClient(handler),
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
        (string, string) next = _responses.Dequeue();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(next.Item2, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

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
        string json = _preamble.Count > 0 ? _preamble.Dequeue().Item2 : _infiniteJson;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
