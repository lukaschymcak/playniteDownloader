namespace PlayniteDownloaderPlugin.Models;

public class QueueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string OriginalUri { get; set; } = string.Empty;
    public List<string> ResolvedUrls { get; set; } = new();
    public int CurrentUrlIndex { get; set; }
    public string DownloadPath { get; set; } = string.Empty;
    public string ExtractionPath { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Waiting;
    public float Progress { get; set; }
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public float ExtractionProgress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
