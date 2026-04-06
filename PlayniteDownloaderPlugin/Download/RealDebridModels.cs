using Newtonsoft.Json;

namespace PlayniteDownloaderPlugin.Download;

public class RdAddMagnetResponse
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

public class RdTorrentInfo
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("links")] public List<string> Links { get; set; } = new();
    [JsonProperty("progress")] public int Progress { get; set; }
}

public class RdUnrestrictResponse
{
    [JsonProperty("download")] public string Download { get; set; } = string.Empty;
    [JsonProperty("filename")] public string Filename { get; set; } = string.Empty;
}

public class RdUser
{
    [JsonProperty("username")] public string Username { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("premium")] public int Premium { get; set; }
    [JsonProperty("expiration")] public string Expiration { get; set; } = string.Empty;
}
