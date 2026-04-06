using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PlayniteDownloaderPlugin.Pipeline;

public static class ExtractionService
{
    public static string? FindArchiveEntryPoint(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        string[] files = Directory.GetFiles(directory);

        string? part1 = files.FirstOrDefault(f =>
            f.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase));
        if (part1 != null) return part1;

        string? multiPartRar = files.FirstOrDefault(f =>
            f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) &&
            !f.Contains(".part", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.ChangeExtension(f, ".r00")));
        if (multiPartRar != null) return multiPartRar;

        foreach (string ext in new[] { ".zip", ".rar", ".7z", ".tar", ".tar.gz" })
        {
            string? found = files.FirstOrDefault(f =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        foreach (string subDir in Directory.GetDirectories(directory))
        {
            string? result = FindArchiveEntryPoint(subDir);
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

        await Task.Run(() =>
        {
            using IArchive archive = ArchiveFactory.Open(archivePath);
            List<IArchiveEntry> entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            int total = entries.Count;
            int done = 0;

            foreach (IArchiveEntry entry in entries)
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
        }, ct);
    }
}
