using System.Windows;

namespace BlankPlugin
{
    public interface IDialogService
    {
        Window CreateWindow(string title, object content, Window owner = null);
        MessageBoxResult ShowMessage(string msg, string title,
            MessageBoxButton buttons, MessageBoxImage icon = MessageBoxImage.None);
        Window GetMainWindow();
        string SelectFile(string filter);
    }
}
