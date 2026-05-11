using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuDownloader.App
{
    public class SettingsWindow : Window
    {
        public SettingsWindow(Settings.StandaloneSettings settings, Window owner)
        {
            var editableSettings = settings.CloneForEdit();

            Title = "Settings";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 35));

            var view = new BlankPlugin.AppSettingsView(editableSettings);

            var saveBtn = new Button
            {
                Content = "Save",
                Width = 80,
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            saveBtn.Click += (s, e) =>
            {
                settings.CommitFrom(editableSettings);
                settings.Save();
                Close();
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Margin = new Thickness(0, 8, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            cancelBtn.Click += (s, e) => Close();

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(saveBtn);

            var root = new DockPanel();
            DockPanel.SetDock(btnRow, Dock.Bottom);
            root.Children.Add(btnRow);

            var scroll = new ScrollViewer
            {
                Content = view,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 700
            };
            root.Children.Add(scroll);

            Content = root;
        }
    }
}
