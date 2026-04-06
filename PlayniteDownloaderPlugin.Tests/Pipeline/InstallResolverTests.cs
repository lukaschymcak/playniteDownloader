using PlayniteDownloaderPlugin.Pipeline;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Pipeline;

public class InstallResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public InstallResolverTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateExe(string relativePath, int sizeBytes = 1000)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[sizeBytes]);
        return full;
    }

    [Fact]
    public void FindExecutable_ReturnsLargestExeWhenNoSetupFiles()
    {
        CreateExe("game.exe", 5_000_000);
        CreateExe("small.exe", 100_000);

        var result = InstallResolver.FindExecutable(_dir);

        Assert.NotNull(result);
        Assert.Contains("game.exe", result);
    }

    [Fact]
    public void FindExecutable_PenalisesSetupExe()
    {
        CreateExe("setup.exe", 10_000_000);
        CreateExe("game.exe", 2_000_000);

        var result = InstallResolver.FindExecutable(_dir);

        Assert.Contains("game.exe", result);
    }

    [Fact]
    public void FindExecutable_ReturnsNullForEmptyDirectory()
    {
        var result = InstallResolver.FindExecutable(_dir);
        Assert.Null(result);
    }

    [Fact]
    public void FindExecutable_PrefersShallowerPath()
    {
        CreateExe("deep/subdir/game.exe", 5_000_000);
        CreateExe("game.exe", 5_000_000);

        var result = InstallResolver.FindExecutable(_dir);

        Assert.Equal(Path.Combine(_dir, "game.exe"), result);
    }
}
