using SharpCompress.Archives;
using SharpCompress.Common;

namespace PlayniteDownloaderPlugin.Pipeline;

public static class ExtractionService
{
    public static string? FindArchiveEntryPoint(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var files = Directory.GetFiles(directory);

        var part1 = files.FirstOrDefault(f =>
            f.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase));
        if (part1 != null) return part1;

        var rarFile = files.FirstOrDefault(f =>
            f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) &&
            !f.Contains(".part", StringComparison.OrdinalIgnoreCase));
        if (rarFile != null)
        {
            var r00Sibling = Path.ChangeExtension(rarFile, ".r00");
            if (File.Exists(r00Sibling)) return rarFile;
        }

        foreach (var ext in new[] { ".zip", ".rar", ".7z", ".tar", ".tar.gz" })
        {
            var found = files.FirstOrDefault(f =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        foreach (var sub in Directory.GetDirectories(directory))
        {
            var result = FindArchiveEntryPoint(sub);
            if (result != null) return result;
        }

        return null;
    }

    public static async Task ExtractAsync(
        string archivePath,
        string outputPath,
        Action<float> onProgress,
        CancellationToken ct)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        using var archive = ArchiveFactory.Open(archivePath);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        var total = entries.Count;
        var done = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            entry.WriteToDirectory(outputPath, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });
            done++;
            onProgress(done / (float)total);
        }

        await Task.CompletedTask;
    }
}
