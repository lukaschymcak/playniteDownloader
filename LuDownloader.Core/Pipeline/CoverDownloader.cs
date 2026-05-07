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
    /// <remarks>
    /// Synchronous HTTP. Call only from a background thread, not the Playnite UI dispatcher.
    /// </remarks>
    public static class CoverDownloader
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();
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
                var dest = Path.Combine(Path.GetTempPath(), "blankplugin_steam_cover_" + appId.Trim() + ".jpg");
                if (File.Exists(dest)) return dest;

                foreach (var uri in SteamStoreImageUrls.GetHeaderStyleCoverUris(appId))
                {
                    using (var response = _http.GetAsync(uri).Result)
                    {
                        if (!response.IsSuccessStatusCode) continue;
                        File.WriteAllBytes(dest, response.Content.ReadAsByteArrayAsync().Result);
                        logger.Info("Cover from Steam CDN: AppID " + appId + " (" + uri + ")");
                        return dest;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.Warn("Steam CDN cover failed: " + ex.Message);
                return null;
            }
        }
    }
}
