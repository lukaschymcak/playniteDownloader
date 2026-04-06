using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class DownloaderFactoryTests
{
    private static UserConfig RdEnabled() => new()
        { RealDebridEnabled = true, RealDebridApiToken = "token" };
    private static UserConfig RdDisabled() => new() { RealDebridEnabled = false };

    [Fact]
    public void IsKnownHoster_ReturnsTrueForKnownDomains()
    {
        Assert.True(DownloaderFactory.IsKnownHoster("https://1fichier.com/?abc"));
        Assert.True(DownloaderFactory.IsKnownHoster("https://rapidgator.net/file/abc"));
    }

    [Fact]
    public void IsKnownHoster_ReturnsFalseForDirectLinks()
    {
        Assert.False(DownloaderFactory.IsKnownHoster("https://cdn.example.com/file.zip"));
        Assert.False(DownloaderFactory.IsKnownHoster("https://github.com/releases/file.zip"));
    }

    [Fact]
    public async Task ResolveUrlsAsync_DirectHttp_ReturnsSameUrl()
    {
        (List<string> urls, bool usedRd) = await DownloaderFactory.ResolveUrlsAsync(
            "https://cdn.example.com/file.zip", RdDisabled(), CancellationToken.None);

        Assert.Single(urls);
        Assert.Equal("https://cdn.example.com/file.zip", urls[0]);
        Assert.False(usedRd);
    }

    [Fact]
    public async Task ResolveUrlsAsync_MagnetWithRdDisabled_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DownloaderFactory.ResolveUrlsAsync(
                "magnet:?xt=test", RdDisabled(), CancellationToken.None));
    }

    [Fact]
    public async Task ResolveUrlsAsync_HosterWithRdDisabled_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DownloaderFactory.ResolveUrlsAsync(
                "https://1fichier.com/?abc", RdDisabled(), CancellationToken.None));
    }
}
