using System.Net.Http;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Download;

public static class DownloaderFactory
{
    private static readonly HttpClient _sharedRdHttp = new HttpClient();

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
            string host = new Uri(uri).Host.TrimStart('w', '.');
            return KnownHosters.Any(h => host.EndsWith(h, StringComparison.OrdinalIgnoreCase));
        }
        catch (UriFormatException) { return false; }
    }

    public static async Task<(List<string> urls, bool usedRealDebrid)> ResolveUrlsAsync(
        string uri, UserConfig config, CancellationToken ct)
    {
        bool isMagnet = uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
        bool isHoster = IsKnownHoster(uri);

        if (isMagnet || isHoster)
        {
            if (!config.RealDebridEnabled)
                throw new InvalidOperationException(
                    "Real-Debrid is required for magnet links and hosted file links. " +
                    "Enable Real-Debrid in settings or choose a direct HTTP link.");

            RealDebridClient rdClient = new RealDebridClient(config.RealDebridApiToken, _sharedRdHttp);
            List<string> urls = await rdClient.GetDownloadUrlAsync(uri, ct);
            return (urls, true);
        }

        return (new List<string> { uri }, false);
    }
}
