using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace BlankPlugin
{
    public class IgdbGameResult
    {
        public int    Id          { get; set; }
        public string Name        { get; set; }
        public int?   ReleaseYear { get; set; }
        public string CoverImageId { get; set; }
    }

    public class IgdbMetadata
    {
        public int           Id          { get; set; }
        public string        Name        { get; set; }
        public string        Summary     { get; set; }
        public List<string>  Genres      { get; set; } = new List<string>();
        public List<string>  Developers  { get; set; } = new List<string>();
        public List<string>  Publishers  { get; set; } = new List<string>();
        public List<string>  Tags        { get; set; } = new List<string>();
        public List<string>  Artworks    { get; set; } = new List<string>();
        public string        CoverImageId { get; set; }
        public int?          ReleaseYear  { get; set; }
    }

    /// <summary>
    /// IGDB API client using Twitch OAuth2 client credentials.
    /// Token is cached per-instance to avoid redundant auth calls.
    /// </summary>
    /// <remarks>
    /// Synchronous HTTP. Call only from background threads (e.g. <c>DownloadView</c> install worker), not the Playnite dispatcher.
    /// </remarks>
    public class IgdbClient
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();

        private const string TokenUrl  = "https://id.twitch.tv/oauth2/token";
        private const string GamesUrl  = "https://api.igdb.com/v4/games";
        private const string ImageUrl      = "https://images.igdb.com/igdb/image/upload/t_cover_big/{0}.jpg";
        private const string ThumbUrl      = "https://images.igdb.com/igdb/image/upload/t_cover_small/{0}.jpg";
        private const string ArtworkUrl    = "https://images.igdb.com/igdb/image/upload/t_1080p/{0}.jpg";
        private const string ArtThumbUrl   = "https://images.igdb.com/igdb/image/upload/t_screenshot_med/{0}.jpg";

        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly HttpClient _http;

        private string _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

        public IgdbClient(string clientId, string clientSecret)
        {
            _clientId     = clientId;
            _clientSecret = clientSecret;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        // ── Token ────────────────────────────────────────────────────────────────

        private string GetToken()
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            var url = string.Format("{0}?client_id={1}&client_secret={2}&grant_type=client_credentials",
                TokenUrl, _clientId, _clientSecret);
            var response = _http.PostAsync(url, new StringContent("")).Result;
            if (!response.IsSuccessStatusCode)
            {
                logger.Warn("IGDB: token request failed (" + (int)response.StatusCode + ")");
                return null;
            }
            var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            _cachedToken = json.Value<string>("access_token");
            _tokenExpiry = DateTime.UtcNow.AddSeconds(json.Value<int>("expires_in") - 60);
            return _cachedToken;
        }

        // ── Search ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Searches IGDB by name and returns up to 10 results with id, name, year and cover.
        /// </summary>
        public List<IgdbGameResult> SearchWithDetails(string query)
        {
            var results = new List<IgdbGameResult>();
            if (!HasCredentials) return results;

            var token = GetToken();
            if (token == null) return results;

            var safe = query.Replace("\"", "\\\"");
            var body = "fields id,name,cover.image_id,first_release_date; search \"" + safe + "\"; limit 10;";

            var arr = Post(GamesUrl, body, token);
            if (arr == null) return results;

            foreach (var item in arr)
            {
                results.Add(new IgdbGameResult
                {
                    Id          = item.Value<int>("id"),
                    Name        = item.Value<string>("name") ?? "(unknown)",
                    CoverImageId = item["cover"]?.Value<string>("image_id"),
                    ReleaseYear = ParseYear(item.Value<long?>("first_release_date"))
                });
            }
            return results;
        }

        // ── Full metadata ────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches complete metadata for a game by its IGDB id.
        /// </summary>
        public IgdbMetadata GetMetadata(int gameId)
        {
            if (!HasCredentials) return null;

            var token = GetToken();
            if (token == null) return null;

            var body =
                "fields name,summary,genres.name," +
                "involved_companies.company.name,involved_companies.developer,involved_companies.publisher," +
                "themes.name,cover.image_id,artworks.image_id,first_release_date;" +
                " where id = " + gameId + "; limit 1;";

            var arr = Post(GamesUrl, body, token);
            if (arr == null || arr.Count == 0) return null;

            var g = arr[0];
            var meta = new IgdbMetadata
            {
                Id           = g.Value<int>("id"),
                Name         = g.Value<string>("name") ?? string.Empty,
                Summary      = g.Value<string>("summary") ?? string.Empty,
                CoverImageId = g["cover"]?.Value<string>("image_id"),
                ReleaseYear  = ParseYear(g.Value<long?>("first_release_date"))
            };

            if (g["genres"] is JArray genres)
                foreach (var x in genres)
                    meta.Genres.Add(x.Value<string>("name"));

            if (g["themes"] is JArray themes)
                foreach (var x in themes)
                    meta.Tags.Add(x.Value<string>("name"));

            if (g["artworks"] is JArray artworks)
                foreach (var a in artworks)
                {
                    var id = a.Value<string>("image_id");
                    if (!string.IsNullOrEmpty(id)) meta.Artworks.Add(id);
                }

            if (g["involved_companies"] is JArray companies)
            {
                foreach (var c in companies)
                {
                    var name = c["company"]?.Value<string>("name");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (c.Value<bool>("developer"))  meta.Developers.Add(name);
                    if (c.Value<bool>("publisher"))  meta.Publishers.Add(name);
                }
            }

            return meta;
        }

        // ── Images ───────────────────────────────────────────────────────────────

        /// <summary>Downloads the full-size cover (t_cover_big). Returns temp file path or null.</summary>
        public string DownloadCoverByImageId(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return null;
            try { return DownloadFile(string.Format(ImageUrl, imageId), "igdb_cover_" + imageId + ".jpg"); }
            catch (Exception ex) { logger.Warn("IGDB cover download failed: " + ex.Message); return null; }
        }

        /// <summary>Downloads a full-size artwork/background (t_1080p). Returns temp file path or null.</summary>
        public string DownloadArtworkByImageId(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return null;
            try { return DownloadFile(string.Format(ArtworkUrl, imageId), "igdb_art_" + imageId + ".jpg"); }
            catch (Exception ex) { logger.Warn("IGDB artwork download failed: " + ex.Message); return null; }
        }

        /// <summary>Downloads the medium screenshot thumbnail (t_screenshot_med). Returns temp file path or null.</summary>
        public string DownloadArtworkThumbByImageId(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return null;
            try { return DownloadFile(string.Format(ArtThumbUrl, imageId), "igdb_artthumb_" + imageId + ".jpg"); }
            catch { return null; }
        }

        /// <summary>Downloads the small thumbnail (t_cover_small). Returns temp file path or null.</summary>
        public string DownloadThumbnailByImageId(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return null;
            try { return DownloadFile(string.Format(ThumbUrl, imageId), "igdb_thumb_" + imageId + ".jpg"); }
            catch { return null; }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private JArray Post(string url, string body, string token)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain")
                };
                req.Headers.Add("Client-ID", _clientId);
                req.Headers.Add("Authorization", "Bearer " + token);

                var response = _http.SendAsync(req).Result;
                if (!response.IsSuccessStatusCode)
                {
                    logger.Warn("IGDB POST failed (" + (int)response.StatusCode + "): " + url);
                    return null;
                }
                return JArray.Parse(response.Content.ReadAsStringAsync().Result);
            }
            catch (Exception ex)
            {
                logger.Warn("IGDB POST exception: " + ex.Message);
                return null;
            }
        }

        private string DownloadFile(string url, string fileName)
        {
            var dest = Path.Combine(Path.GetTempPath(), "blankplugin_" + fileName);
            if (File.Exists(dest)) return dest;
            using (var response = _http.GetAsync(url).Result)
            {
                if (!response.IsSuccessStatusCode) return null;
                File.WriteAllBytes(dest, response.Content.ReadAsByteArrayAsync().Result);
            }
            return dest;
        }

        private static int? ParseYear(long? unixTimestamp)
        {
            if (unixTimestamp == null) return null;
            return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value).Year;
        }
    }
}
