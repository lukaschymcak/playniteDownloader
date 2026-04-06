using System.Net.Http;
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Source;

public class SourceManager
{
    private readonly List<ISourceProvider> _builtinProviders;
    private readonly List<SourceEntry> _builtinEntries;
    private readonly List<SourceEntry> _customSources = new();
    private readonly string? _persistPath;
    private readonly HttpClient _httpClient = JsonSourceProvider.CreateDefaultClient();

    public SourceManager(IEnumerable<ISourceProvider> builtinProviders,
        IEnumerable<SourceEntry>? builtinEntries = null,
        string? persistPath = null)
    {
        _builtinProviders = builtinProviders.ToList();
        _builtinEntries = builtinEntries?.ToList() ?? new();
        _persistPath = persistPath;
        if (persistPath != null) LoadCustomSources(persistPath);
    }

    private void LoadCustomSources(string persistPath)
    {
        string path = Path.Combine(persistPath, "sources.json");
        if (!File.Exists(path)) return;
        List<SourceEntry>? loaded = JsonConvert.DeserializeObject<List<SourceEntry>>(File.ReadAllText(path));
        if (loaded != null) _customSources.AddRange(loaded);
    }

    public async Task<List<DownloadResult>> SearchAllAsync(string gameName, CancellationToken ct)
    {
        IEnumerable<Task<List<DownloadResult>>> builtinTasks = _builtinProviders
            .Select(p => p.SearchAsync(gameName, ct));

        IEnumerable<Task<List<DownloadResult>>> customTasks = _customSources
            .Where(s => s.Enabled)
            .Select(s => new JsonSourceProvider(s.Id, s.Name, s.Url, _httpClient))
            .Select(p => p.SearchAsync(gameName, ct));

        List<DownloadResult>[] all = await Task.WhenAll(builtinTasks.Concat(customTasks));
        return all.SelectMany(r => r)
            .OrderByDescending(r => r.MatchScore)
            .ToList();
    }

    public void AddCustomSource(string name, string url)
    {
        if (_customSources.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) ||
            _builtinEntries.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A source with URL '{url}' already exists.");

        _customSources.Add(new SourceEntry { Name = name, Url = url, IsCustom = true });
        PersistCustomSources();
    }

    public void RemoveCustomSource(string id)
    {
        SourceEntry entry = _customSources.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Source '{id}' not found.");
        _customSources.Remove(entry);
        PersistCustomSources();
    }

    public IReadOnlyList<SourceEntry> GetAllSources()
        => _builtinEntries.Concat(_customSources).ToList().AsReadOnly();

    public IReadOnlyList<SourceEntry> GetCustomSources() => _customSources.AsReadOnly();

    private void PersistCustomSources()
    {
        if (_persistPath == null) return;
        string path = Path.Combine(_persistPath, "sources.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_customSources, Formatting.Indented));
    }
}
