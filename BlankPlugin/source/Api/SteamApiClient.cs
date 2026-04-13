using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace BlankPlugin
{
    // Returned by IStoreQueryService/Query — one entry per appid
    public class SteamSearchItem
    {
        public string AppId { get; set; }   // e.g. "1245620"
        public string Name  { get; set; }   // e.g. "Elden Ring"
    }

    // Wrapper for paginated search results
    public class SteamSearchResult
    {
        public List<SteamSearchItem> Items { get; set; } = new List<SteamSearchItem>();
        public int Total { get; set; }  // total matching records from Steam
    }

    // Returned by store.steampowered.com/api/appdetails
    public class SteamGameDetails
    {
        public string Type             { get; set; }  // "game", "dlc", "music", "demo", etc.
        public string AppId            { get; set; }
        public string Name             { get; set; }
        public string ShortDescription { get; set; }
        public string HeaderImageUrl   { get; set; }
        public string ReleaseDate      { get; set; }  // raw string, e.g. "25 Feb, 2022"
        public int    ReleaseYear      { get; set; }  // parsed from ReleaseDate
        public int    DlcCount         { get; set; }  // length of dlc[] array
        public List<string> DlcIds     { get; set; }  // DLC appid list
    }

    public class SteamApiClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string ApiBase   = "https://api.steampowered.com";
        private const string StoreBase = "https://store.steampowered.com";

        private readonly HttpClient   _http;
        private readonly Func<string> _getKey;

        public SteamApiClient(Func<string> getKey)
        {
            _getKey = getKey;
            // Same SSL and TLS settings as MorrenusClient
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ── Search ──────────────────────────────────────────────────────────────

        public SteamSearchResult SearchGames(string searchTerm, int start = 0, int count = 10)
        {
            logger.Info("SteamApiClient.SearchGames() called. Term: '" + searchTerm + "', start=" + start + ", count=" + count);

            var url = StoreBase + "/api/storesearch/?term="
                      + Uri.EscapeDataString(searchTerm)
                      + "&l=english&cc=US"
                      + "&start=" + start + "&count=" + count;

            try
            {
                var response = _http.GetAsync(url).Result;
                logger.Info("SearchGames HTTP status: " + response.StatusCode);
                var body = response.Content.ReadAsStringAsync().Result;
                logger.Info("SearchGames response (" + body.Length + " chars): " + (body.Length > 1000 ? body.Substring(0, 1000) + "..." : body));

                var root = JObject.Parse(body);
                var items = root["items"] as JArray;
                var total = root.Value<int?>("total") ?? 0;
                logger.Info("SearchGames response keys: " + string.Join(", ", root.Properties().Select(p => p.Name + "=" + p.Value)));
                logger.Info("SearchGames items: " + (items != null ? items.Count + " items" : "NULL") + ", total=" + total);

                var result = new SteamSearchResult { Total = total };
                if (items == null)
                {
                    logger.Info("SearchGames root keys: " + string.Join(", ", root.Properties().Select(p => p.Name)));
                    return result;
                }

                foreach (var item in items)
                {
                    var appid = item.Value<int?>("id");
                    var name  = item.Value<string>("name");
                    var type  = item.Value<string>("type");
                    if (appid == null || string.IsNullOrEmpty(name)) continue;
                    result.Items.Add(new SteamSearchItem { AppId = appid.ToString(), Name = name });
                }
                logger.Info("SearchGames returning " + result.Items.Count + " results, total=" + total);
                return result;
            }
            catch (Exception ex)
            {
                logger.Error("SearchGames FAILED: " + ex.Message + "\n" + ex.StackTrace);
                throw;
            }
        }

        // ── Game Details ────────────────────────────────────────────────────────

        public SteamGameDetails GetGameDetails(string appId)
        {
            logger.Info("SteamApiClient.GetGameDetails() called. AppId: " + appId);
            var url  = StoreBase + "/api/appdetails?appids=" + appId
                       + "&filters=basic,release_date,short_description,dlc";
            try
            {
                var body = _http.GetAsync(url).Result.Content.ReadAsStringAsync().Result;
                logger.Info("GetGameDetails(" + appId + ") response (" + body.Length + " chars): " + (body.Length > 800 ? body.Substring(0, 800) + "..." : body));
                var root = JObject.Parse(body);
                var node = root[appId];
                if (node == null || !node.Value<bool>("success"))
                {
                    logger.Warn("GetGameDetails(" + appId + "): success=false or node null");
                    return null;
                }

                var data = node["data"] as JObject;
                if (data == null)
                {
                    logger.Warn("GetGameDetails(" + appId + "): data node null");
                    return null;
                }

            // Parse release year from string like "25 Feb, 2022" or "Feb 2022"
            var relStr = data["release_date"]?.Value<string>("date") ?? string.Empty;
            int.TryParse(relStr.Length >= 4 ? relStr.Substring(relStr.Length - 4) : "0", out int year);

            // DLC: array of appids
            var dlcIds = new List<string>();
            if (data["dlc"] is JArray dlc)
            {
                foreach (var d in dlc)
                    dlcIds.Add(d.ToString());
            }

            var details = new SteamGameDetails
            {
                Type             = data.Value<string>("type"),
                AppId            = appId,
                Name             = data.Value<string>("name"),
                ShortDescription = data.Value<string>("short_description"),
                HeaderImageUrl   = data.Value<string>("header_image"),
                ReleaseDate      = relStr,
                ReleaseYear      = year,
                DlcCount         = dlcIds.Count,
                DlcIds           = dlcIds
            };
            logger.Info("GetGameDetails(" + appId + "): name=" + details.Name + ", type=" + details.Type + ", year=" + year + ", dlc=" + dlcIds.Count);
            return details;
            }
            catch (Exception ex)
            {
                logger.Error("GetGameDetails(" + appId + ") FAILED: " + ex.Message + "\n" + ex.StackTrace);
                throw;
            }
        }
    }
}
