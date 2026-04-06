using System.Net.Http.Headers;
using Newtonsoft.Json;

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
        RdAddMagnetResponse addResp = await PostFormAsync<RdAddMagnetResponse>(
            "/torrents/addMagnet", new Dictionary<string, string> { ["magnet"] = magnet }, ct);
        string torrentId = addResp.Id;

        await PostFormAsync<object>(
            $"/torrents/selectFiles/{torrentId}",
            new Dictionary<string, string> { ["files"] = "all" }, ct);

        DateTime deadline = DateTime.UtcNow + _pollTimeout;
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

        List<string> directUrls = new List<string>();
        foreach (string link in info.Links)
        {
            RdUnrestrictResponse unrestricted = await PostFormAsync<RdUnrestrictResponse>(
                "/unrestrict/link", new Dictionary<string, string> { ["link"] = link }, ct);
            directUrls.Add(Uri.UnescapeDataString(unrestricted.Download));
        }
        return directUrls;
    }

    private async Task<List<string>> ResolveDirectLinkAsync(string link, CancellationToken ct)
    {
        RdUnrestrictResponse unrestricted = await PostFormAsync<RdUnrestrictResponse>(
            "/unrestrict/link", new Dictionary<string, string> { ["link"] = link }, ct);
        return new List<string> { Uri.UnescapeDataString(unrestricted.Download) };
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        string response = await _http.GetStringAsync(BaseUrl + path, ct);
        return JsonConvert.DeserializeObject<T>(response)!;
    }

    private async Task<T> PostFormAsync<T>(
        string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        FormUrlEncodedContent content = new FormUrlEncodedContent(fields);
        HttpResponseMessage response = await _http.PostAsync(BaseUrl + path, content, ct);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return default!;
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    public async Task<RdUser> GetUserAsync(CancellationToken ct)
        => await GetAsync<RdUser>("/user", ct);
}
