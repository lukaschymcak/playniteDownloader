using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Download;

public class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long FileSize { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DownloaderStatus Status { get; set; }
}

public interface IDownloader
{
    event Action<DownloadProgress>? ProgressChanged;
    Task StartAsync(string url, string savePath, CancellationToken ct);
    void Pause();
    Task ResumeAsync();
    void Cancel(bool deleteFile = true);
    DownloadProgress GetStatus();
}
