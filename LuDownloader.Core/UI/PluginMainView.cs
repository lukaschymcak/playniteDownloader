using Playnite.SDK;
using System.Windows.Controls;

namespace BlankPlugin
{
    public class PluginMainView : UserControl
    {
        public PluginMainView(
            BlankPluginSettings settings,
            InstalledGamesManager installedGames,
            LibraryGamesManager libraryGames,
            IPlayniteAPI api,
            UpdateChecker updateChecker,
            BlankPlugin plugin)
        {
            var tabs = new TabControl();

            // Tab 0: Library (existing view, untouched)
            var libraryTab = new TabItem
            {
                Header  = "Library",
                Content = new LibraryView(settings, installedGames, libraryGames, api, updateChecker, plugin)
            };

            // Tab 1: Search (new)
            var searchTab = new TabItem
            {
                Header  = "Search",
                Content = new SearchView(settings, api, plugin, libraryGames)
            };

            // Tab 2: Cached Morrenus manifests (sidebar window only — not in DownloadView)
            var manifestsTab = new TabItem
            {
                Header  = "Manifests",
                Content = new ManifestsView(plugin)
            };

            tabs.Items.Add(libraryTab);
            tabs.Items.Add(searchTab);
            tabs.Items.Add(manifestsTab);

            Content = tabs;
        }
    }
}
