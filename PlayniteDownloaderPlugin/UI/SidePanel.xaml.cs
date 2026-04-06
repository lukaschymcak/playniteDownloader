#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI
{
    public partial class SidePanel : UserControl
    {
        public SidePanel(
            DownloadQueue queue,
            SourceManager sourceManager,
            UserConfig config,
            Action saveConfig,
            DownloadPipelineRunner runner)
        {
            InitializeComponent();
            DataContext = new SidePanelViewModel(queue, sourceManager, config, saveConfig, runner);
            RdTokenBox.Password = config.RealDebridApiToken;
        }

        private void RdTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SidePanelViewModel vm)
                vm.RdToken = RdTokenBox.Password;
        }
    }
}
#endif
