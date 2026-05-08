using System.Windows;

namespace LuDownloader.App.Services
{
    public class StandaloneDialogService : BlankPlugin.IDialogService
    {
        private Window _mainWindow;

        public void SetMainWindow(Window w) => _mainWindow = w;
        public Window GetMainWindow() => _mainWindow ?? Application.Current.MainWindow;

        public Window CreateWindow(string title, object content, Window owner = null)
        {
            return new Window
            {
                Title = title,
                Content = content,
                Owner = owner ?? GetMainWindow(),
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
        }

        public MessageBoxResult ShowMessage(
            string msg, string title,
            MessageBoxButton buttons,
            MessageBoxImage icon = MessageBoxImage.None)
            => MessageBox.Show(GetMainWindow(), msg, title, buttons, icon);

        public string SelectFile(string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            return dlg.ShowDialog(GetMainWindow()) == true ? dlg.FileName : null;
        }
    }
}
