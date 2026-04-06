using System.Net.Http;
using FuzzySharp;
using Newtonsoft.Json.Linq;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public class JsonSourceProvider : ISourceProvider
{
    private const int MinFuzzyScore = 60;
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
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<List<DownloadResult>> SearchAsync(string gameName, CancellationToken ct)
    {
        try
        {
            string json = await _http.GetStringAsync(_url, ct);
            JObject root = JObject.Parse(json);
            JArray downloads = root["downloads"] as JArray ?? new JArray();

            List<DownloadResult> results = new List<DownloadResult>();
            foreach (JToken item in downloads)
            {
                string title = item["title"]?.Value<string>() ?? string.Empty;
                int score = Fuzz.Ratio(gameName.ToLowerInvariant(), title.ToLowerInvariant());
                if (score < MinFuzzyScore) continue;

                List<string> uris = (item["uris"] as JArray)?
                    .Select(u => u.Value<string>()!)
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
            return new List<DownloadResult>();
        }
    }
}
