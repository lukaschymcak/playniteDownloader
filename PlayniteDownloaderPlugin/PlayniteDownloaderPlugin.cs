#if WINDOWS
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteDownloaderPlugin.Integration;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;
using PlayniteDownloaderPlugin.UI;

namespace PlayniteDownloaderPlugin;

public class PlayniteDownloaderPlugin : GenericPlugin
{
    public override Guid Id { get; } = Guid.Parse("a3b4c5d6-e7f8-9012-abcd-ef1234567890");

    private readonly DownloadQueue _queue;
    private readonly SourceManager _sourceManager;
    private readonly DownloadPipelineRunner _runner;
    private readonly UserConfig _config;

    public PlayniteDownloaderPlugin(IPlayniteAPI api) : base(api)
    {
        string dataPath = GetPluginUserDataPath();
        _config = LoadConfig(dataPath);
        _queue = DownloadQueue.LoadFrom(dataPath);
        _sourceManager = BuildSourceManager();
        PlayniteIntegration integration = new PlayniteIntegration(api);
        _runner = new DownloadPipelineRunner(_queue, integration, _config);
    }

    public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
    {
        _runner.Start();
    }

    public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
    {
        _runner.Stop();
    }

    public override IEnumerable<SidebarItem> GetSidebarItems()
    {
        yield return new SidebarItem
        {
            Title = "Downloader",
            Type = SiderbarItemType.View,
            Opened = () => new SidePanel(_queue, _sourceManager, _config, SaveConfig, _runner)
        };
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        yield return new GameMenuItem
        {
            Description = "Search Downloads",
            Action = a =>
            {
                Game game = a.Games.First();
                SearchDialog dialog = new SearchDialog(
                    game, _sourceManager, _queue, _config, PlayniteApi);
                dialog.ShowDialog();
            }
        };
    }

    private UserConfig LoadConfig(string dataPath)
    {
        string path = Path.Combine(dataPath, "config.json");
        if (!File.Exists(path)) return new UserConfig();
        return JsonConvert.DeserializeObject<UserConfig>(File.ReadAllText(path)) ?? new UserConfig();
    }

    private void SaveConfig()
    {
        string path = Path.Combine(GetPluginUserDataPath(), "config.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_config, Formatting.Indented));
    }

    private SourceManager BuildSourceManager()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string builtinPath = assemblyDir != null
            ? Path.Combine(assemblyDir, "builtin-sources.json")
            : "";
        List<SourceEntry> builtinEntries = File.Exists(builtinPath)
            ? JsonConvert.DeserializeObject<List<SourceEntry>>(File.ReadAllText(builtinPath))
              ?? new List<SourceEntry>()
            : new List<SourceEntry>();

        IEnumerable<ISourceProvider> providers = builtinEntries
            .Where(e => e.Enabled)
            .Select(e => (ISourceProvider)new JsonSourceProvider(e.Id, e.Name, e.Url));

        SourceManager manager = new SourceManager(providers, builtinEntries, GetPluginUserDataPath());
        return manager;
    }
}
#endif
