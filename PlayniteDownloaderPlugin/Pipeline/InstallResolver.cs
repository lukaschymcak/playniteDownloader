namespace PlayniteDownloaderPlugin.Pipeline;

public static class InstallResolver
{
    private static readonly HashSet<string> PenalisedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "install", "unins", "redist", "vc_", "directx", "vcredist", "dxsetup"
    };

    public static string? FindExecutable(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var exeFiles = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories);
        var candidates = exeFiles.Select(path => Score(path, directory)).ToList();

        if (candidates.Count == 0) return null;
        return candidates.OrderByDescending(c => c.score).First().path;
    }

    private static (string path, double score) Score(string path, string baseDir)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var fileInfo = new FileInfo(path);
        var depth = path[baseDir.Length..].TrimStart(Path.DirectorySeparatorChar)
            .Count(c => c == Path.DirectorySeparatorChar);

        double score = fileInfo.Length / (1024.0 * 1024.0 * 10.0);
        score -= depth * 5;

        if (PenalisedKeywords.Any(name.Contains))
            score -= 100;

        return (path, score);
    }
}
