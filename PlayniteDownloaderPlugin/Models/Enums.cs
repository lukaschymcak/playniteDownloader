namespace PlayniteDownloaderPlugin.Models;

public enum DownloadStatus
{
    Waiting,
    Active,
    Paused,
    Error,
    Complete,
    Extracting
}

public enum DownloaderStatus
{
    Active,
    Paused,
    Complete,
    Error
}

public enum UriType
{
    Magnet,
    Hoster,
    DirectHttp
}
