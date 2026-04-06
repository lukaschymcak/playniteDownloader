using System.IO.Compression;
using PlayniteDownloaderPlugin.Pipeline;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Pipeline;

public class ExtractionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public ExtractionServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateZip(string name, string entryName, byte[] content)
    {
        var zipPath = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(content);
        return zipPath;
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsZipFile()
    {
        var zipPath = CreateZip("game.zip", "game.exe", new byte[] { 1, 2, 3 });

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Equal(zipPath, result);
    }

    [Fact]
    public async Task ExtractAsync_ExtractsZipContents()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var zipPath = CreateZip("game.zip", "game.exe", content);
        var outputDir = Path.Combine(_dir, "output");
        Directory.CreateDirectory(outputDir);
        var progressValues = new List<float>();

        await ExtractionService.ExtractAsync(zipPath, outputDir,
            p => progressValues.Add(p), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(outputDir, "game.exe")));
        Assert.NotEmpty(progressValues);
        Assert.Equal(1.0f, progressValues.Last());
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsPart1RarWhenPresent()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.part1.rar"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_dir, "game.part2.rar"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.part1.rar", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsRarWhenR00SiblingExists()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.rar"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_dir, "game.r00"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.rar", result);
        Assert.DoesNotContain("game.r00", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsTarGzWhenPresent()
    {
        File.WriteAllBytes(Path.Combine(_dir, "game.tar.gz"), new byte[1]);

        var result = ExtractionService.FindArchiveEntryPoint(_dir);

        Assert.Contains("game.tar.gz", result);
    }

    [Fact]
    public void FindArchiveEntryPoint_ReturnsNullWhenNoArchives()
    {
        var result = ExtractionService.FindArchiveEntryPoint(_dir);
        Assert.Null(result);
    }
}
