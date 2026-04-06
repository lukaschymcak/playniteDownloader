#if WINDOWS
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI
{
    public partial class SearchDialog : Window
    {
        public SearchDialog(Game game, SourceManager sourceManager,
            DownloadQueue queue, UserConfig config, IPlayniteAPI playniteApi)
        {
            InitializeComponent();
            SearchDialogViewModel vm = new SearchDialogViewModel(
                game.Id.ToString(), game.Name, sourceManager, queue, config, playniteApi);
            vm.CloseDialog = Close;
            DataContext = vm;
        }
    }
}
#endif
