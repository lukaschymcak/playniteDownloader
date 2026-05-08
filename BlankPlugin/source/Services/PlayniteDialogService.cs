using Playnite.SDK;
using System.Windows;

namespace BlankPlugin
{
    public class PlayniteDialogService : IDialogService
    {
        private readonly IPlayniteAPI _api;
        public PlayniteDialogService(IPlayniteAPI api) => _api = api;

        public Window CreateWindow(string title, object content, Window owner = null)
        {
            var w = _api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });
            w.Title = title;
            w.Content = content;
            if (owner != null) w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return w;
        }

        public MessageBoxResult ShowMessage(string msg, string title, MessageBoxButton buttons, MessageBoxImage icon = MessageBoxImage.None)
            => _api.Dialogs.ShowMessage(msg, title, buttons, icon);

        public Window GetMainWindow() => _api.Dialogs.GetCurrentAppWindow();

        public string SelectFile(string filter) => _api.Dialogs.SelectFile(filter);
    }
}
