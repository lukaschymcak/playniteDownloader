namespace PlayniteDownloaderPlugin.Models;

public class SourceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsCustom { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
