using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BlankPlugin
{
    public class MorrenusSearchResult
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public override string ToString() => GameName + "  (" + GameId + ")";
    }

    public class MorrenusUserStats
    {
        public string Username { get; set; }
        public int DailyUsage { get; set; }
        public int DailyLimit { get; set; }
        public string Error { get; set; }
    }

    /// <remarks>
    /// All public methods perform synchronous HTTP (.Result). Call only from a background
    /// thread (e.g. ThreadPool, <c>DownloadView</c> worker) — never from the Playnite UI dispatcher.
    /// </remarks>
    public class MorrenusClient
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();
        private const string BaseUrl = "https://manifest.morrenus.xyz/api/v1";

        private readonly HttpClient _http;
        private readonly Func<string> _getApiKey;

        public MorrenusClient(Func<string> getApiKey)
        {
            _getApiKey = getApiKey;

            // Accept any SSL cert — mirrors ACCELA's permissive SSL adapter
            // ServicePointManager is the net462 equivalent of ServerCertificateCustomValidationCallback
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        private void SetAuthHeader()
        {
            var key = _getApiKey();
            _http.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrWhiteSpace(key))
                _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
        }

        // ── Health ──────────────────────────────────────────────────────────────

        public bool CheckHealth()
        {
            try
            {
                var response = _http.GetAsync(BaseUrl + "/health").Result;
                if (!response.IsSuccessStatusCode) return false;
                var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                return json.Value<string>("status") == "healthy";
            }
            catch (Exception ex)
            {
                logger.Warn("Health check failed: " + ex.Message);
                return false;
            }
        }

        // ── User stats ──────────────────────────────────────────────────────────

        public MorrenusUserStats GetUserStats()
        {
            try
            {
                SetAuthHeader();
                var response = _http.GetAsync(BaseUrl + "/user/stats").Result;
                var body = response.Content.ReadAsStringAsync().Result;
                var json = JObject.Parse(body);

                if (!response.IsSuccessStatusCode)
                    return new MorrenusUserStats { Error = MapStatusError((int)response.StatusCode, body) };

                return new MorrenusUserStats
                {
                    Username = json.Value<string>("username"),
                    DailyUsage = json.Value<int>("daily_usage"),
                    DailyLimit = json.Value<int>("daily_limit")
                };
            }
            catch (Exception ex)
            {
                logger.Warn("GetUserStats failed: " + ex.Message);
                return new MorrenusUserStats { Error = ex.Message };
            }
        }

        // ── Search ──────────────────────────────────────────────────────────────

        public List<MorrenusSearchResult> SearchGames(string query)
        {
            var results = new List<MorrenusSearchResult>();
            try
            {
                SetAuthHeader();
                var url = BaseUrl + "/search?q=" + Uri.EscapeDataString(query) + "&limit=50";
                var response = _http.GetAsync(url).Result;
                var body = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                    throw new Exception(MapStatusError((int)response.StatusCode, body));

                var token = JToken.Parse(body);
                JArray arr = null;

                if (token is JArray ja) arr = ja;
                else if (token is JObject jo && jo["results"] is JArray jr) arr = jr;

                if (arr == null) return results;

                foreach (var item in arr)
                {
                    results.Add(new MorrenusSearchResult
                    {
                        GameId = item.Value<string>("game_id") ?? item.Value<int>("game_id").ToString(),
                        GameName = item.Value<string>("game_name")
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error("SearchGames failed: " + ex.Message);
                throw;
            }
            return results;
        }

        // ── Download manifest ZIP ────────────────────────────────────────────────

        /// <summary>
        /// Downloads the manifest ZIP for the given AppID.
        /// Returns the local file path on success.
        /// When <paramref name="destinationZipPath"/> is null, writes under the temp directory (legacy path).
        /// </summary>
        public string DownloadManifest(string appId, IProgress<int> progress = null, string destinationZipPath = null)
        {
            SetAuthHeader();
            var url = BaseUrl + "/manifest/" + appId;

            string destFile;
            if (!string.IsNullOrWhiteSpace(destinationZipPath))
            {
                destFile = Path.GetFullPath(destinationZipPath);
                var parent = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            else
            {
                var destDir = Path.Combine(Path.GetTempPath(), "blankplugin_manifests");
                Directory.CreateDirectory(destDir);
                destFile = Path.Combine(destDir, "accela_fetch_" + appId + ".zip");
            }

            logger.Info("Downloading manifest for AppID " + appId + " → " + destFile);

            try
            {
                using (var response = _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = response.Content.ReadAsStringAsync().Result;
                        throw new Exception(MapStatusError((int)response.StatusCode, body));
                    }

                    var total = response.Content.Headers.ContentLength ?? -1L;
                    using (var src = response.Content.ReadAsStreamAsync().Result)
                    using (var dst = File.Create(destFile))
                    {
                        var buf = new byte[8192];
                        long downloaded = 0;
                        int read;
                        while ((read = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            dst.Write(buf, 0, read);
                            downloaded += read;
                            if (total > 0)
                                progress?.Report((int)(downloaded * 100 / total));
                        }
                    }
                }

                progress?.Report(100);
                logger.Info("Manifest downloaded: " + destFile);
                return destFile;
            }
            catch
            {
                try
                {
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                }
                catch
                {
                    // best effort
                }
                throw;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string MapStatusError(int code, string body)
        {
            switch (code)
            {
                case 401: return "Invalid or missing API key. Check Settings.";
                case 403: return "Access denied. Your account may be blocked or this AppID is not accessible.";
                case 404: return "Game not found in Morrenus library.";
                case 429: return "Daily API limit exceeded. Try again later.";
                case 500: return "Server error. The manifest may be temporarily unavailable.";
                default:
                    try
                    {
                        var j = JObject.Parse(body);
                        var detail = j.Value<string>("detail");
                        if (!string.IsNullOrEmpty(detail)) return "API Error (" + code + "): " + detail;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug("MorrenusClient: could not parse error body for HTTP " + code + ": " + ex.Message);
                    }
                    return "API Error (" + code + ")";
            }
        }
    }
}
