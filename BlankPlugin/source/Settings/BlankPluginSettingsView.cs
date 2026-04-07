using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace BlankPlugin
{
    public class BlankPluginSettingsView : System.Windows.Controls.UserControl
    {
        private readonly BlankPluginSettings _settings;

        public BlankPluginSettingsView(BlankPluginSettings settings)
        {
            _settings = settings;
            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            // API Key
            root.Children.Add(new TextBlock
            {
                Text = "Morrenus API Key",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var apiKeyBox = new System.Windows.Controls.TextBox
            {
                Text = _settings.ApiKey,
                Margin = new Thickness(0, 0, 0, 16)
            };
            apiKeyBox.TextChanged += (s, e) => _settings.ApiKey = apiKeyBox.Text;
            root.Children.Add(apiKeyBox);

            // Download Path
            root.Children.Add(new TextBlock
            {
                Text = "Default Download Path",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var browseBtn = new System.Windows.Controls.Button
            {
                Content = "Browse...",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(browseBtn, Dock.Right);

            var pathBox = new System.Windows.Controls.TextBox { Text = _settings.DownloadPath };
            pathBox.TextChanged += (s, e) => _settings.DownloadPath = pathBox.Text;

            browseBtn.Click += (s, e) =>
            {
                var dialog = new FolderBrowserDialog
                {
                    Description = "Select download folder",
                    ShowNewFolderButton = true,
                    SelectedPath = _settings.DownloadPath
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    pathBox.Text = dialog.SelectedPath;
                    _settings.DownloadPath = dialog.SelectedPath;
                }
            };

            pathRow.Children.Add(browseBtn);
            pathRow.Children.Add(pathBox);
            root.Children.Add(pathRow);

            root.Children.Add(new TextBlock
            {
                Text = "Games download to: {path}\\steamapps\\common\\{installdir}",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Parallel downloads
            root.Children.Add(new TextBlock
            {
                Text = "Parallel Downloads (1–64)",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var downloadsBox = new System.Windows.Controls.TextBox
            {
                Text = _settings.MaxDownloads.ToString(),
                Width = 60,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };
            downloadsBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(downloadsBox.Text, out int v))
                    _settings.MaxDownloads = v;
            };
            root.Children.Add(downloadsBox);

            root.Children.Add(new TextBlock
            {
                Text = "Higher = more simultaneous chunk downloads. Default 20. Lower if on a slow or congested connection.",
                FontSize = 11,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray
            });

            Content = root;
        }
    }
}
