namespace PlayniteDownloaderPlugin.Models;

public class DownloadResult
{
    public string Title { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public List<string> Uris { get; set; } = new();
    public string FileSize { get; set; } = string.Empty;
    public string UploadDate { get; set; } = string.Empty;
    public float MatchScore { get; set; }
}
