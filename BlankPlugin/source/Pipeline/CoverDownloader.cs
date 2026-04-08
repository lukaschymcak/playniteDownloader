using Playnite.SDK;
using System;
using System.IO;
using System.Net;
using System.Net.Http;

namespace BlankPlugin
{
    /// <summary>
    /// Downloads a game cover from Steam CDN for storage in Playnite's database.
    /// Used as a fallback when IGDB credentials are not configured.
    /// Returns a local temp file path, or null if the download failed.
    /// </summary>
    public static class CoverDownloader
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly HttpClient _http;

        static CoverDownloader()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public static string Download(string gameName, string appId, IgdbClient igdb)
        {
            if (string.IsNullOrWhiteSpace(appId)) return null;
            try
            {
                var url  = "https://cdn.akamai.steamstatic.com/steam/apps/" + appId + "/header.jpg";
                var dest = Path.Combine(Path.GetTempPath(), "blankplugin_steam_cover_" + appId + ".jpg");
                if (File.Exists(dest)) return dest;

                using (var response = _http.GetAsync(url).Result)
                {
                    if (!response.IsSuccessStatusCode) return null;
                    File.WriteAllBytes(dest, response.Content.ReadAsByteArrayAsync().Result);
                }
                logger.Info("Cover from Steam CDN: AppID " + appId);
                return dest;
            }
            catch (Exception ex)
            {
                logger.Warn("Steam CDN cover failed: " + ex.Message);
                return null;
            }
        }
    }
}
