using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public interface ISourceProvider
{
    string Id { get; }
    string Name { get; }
    Task<List<DownloadResult>> SearchAsync(string gameName, CancellationToken ct);
}
