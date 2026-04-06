namespace PlayniteDownloaderPlugin.Models;

public class UserConfig
{
    public bool RealDebridEnabled { get; set; }
    public string RealDebridApiToken { get; set; } = string.Empty;
    public string DefaultDownloadPath { get; set; } = string.Empty;
}
