using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlankPlugin
{
    /// <summary>
    /// Dialog for updating an already-installed game.
    /// Shows current update status, saved manifest info, and offers
    /// two update paths: via Morrenus API or local ZIP file.
    /// </summary>
    public class UpdateGameDialog : UserControl
    {
        private readonly InstalledGame _game;
        private readonly AppSettings _settings;
        private readonly InstalledGamesManager _gamesManager;
        private readonly IDialogService _dialogService;
        private readonly UpdateChecker _updateChecker;

        public UpdateGameDialog(
            InstalledGame game,
            AppSettings settings,
            InstalledGamesManager gamesManager,
            IDialogService dialogService,
            UpdateChecker updateChecker)
        {
            _game = game;
            _settings = settings;
            _gamesManager = gamesManager;
            _dialogService = dialogService;
            _updateChecker = updateChecker;

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            // Title
            var title = new TextBlock
            {
                Text = "Update: " + game.GameName,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(title);

            // Status section
            var statusText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            if (_updateChecker != null)
            {
                var status = _updateChecker.GetStatus(game.AppId);
                if (status == "update_available")
                {
                    statusText.Text = "Status: Update available";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                }
                else if (status == "up_to_date")
                {
                    statusText.Text = "Status: Up to date";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                }
                else
                {
                    statusText.Text = "Status: Unknown (run a check first)";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
                }
            }
            else
            {
                statusText.Text = "Status: Update checker not available";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
            }
            panel.Children.Add(statusText);

            // Saved manifests section
            if (game.ManifestGIDs != null && game.ManifestGIDs.Count > 0)
            {
                var manifestsHeader = new TextBlock
                {
                    Text = "Saved manifests:",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                panel.Children.Add(manifestsHeader);

                foreach (var kv in game.ManifestGIDs)
                {
                    var depotInfo = new TextBlock
                    {
                        Text = "  Depot " + kv.Key + ": " + kv.Value,
                        Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 0, 1)
                    };
                    panel.Children.Add(depotInfo);
                }
            }

            // Separator
            var separator = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 12, 0, 12)
            };
            panel.Children.Add(separator);

            // Button row
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var apiButton = new Button
            {
                Content = "Update via API",
                Width = 110,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            };
            apiButton.Click += (s, e) => OnUpdateViaApi();
            buttonRow.Children.Add(apiButton);

            var zipButton = new Button
            {
                Content = "Update via ZIP",
                Width = 110,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            };
            zipButton.Click += (s, e) => OnUpdateViaZip();
            buttonRow.Children.Add(zipButton);

            var closeButton = new Button
            {
                Content = "Close",
                Width = 70,
                Height = 28
            };
            closeButton.Click += (s, e) => Window.GetWindow(this)?.Close();
            buttonRow.Children.Add(closeButton);

            panel.Children.Add(buttonRow);

            Content = panel;
        }

        private void OnUpdateViaApi()
        {
            Window.GetWindow(this)?.Close();

            var updateWindow = new UpdateWindow(_game, _settings, _gamesManager, _dialogService, null, _updateChecker);
            var window = _dialogService.CreateWindow("Update — " + _game.GameName, updateWindow, _dialogService.GetMainWindow());
            window.Width = 700;
            window.Height = 500;
            window.ShowDialog();
        }

        private void OnUpdateViaZip()
        {
            var zipPath = _dialogService.SelectFile("Manifest ZIP|*.zip");
            if (string.IsNullOrEmpty(zipPath)) return;

            GameData freshData;
            try
            {
                freshData = new ZipProcessor().Process(zipPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read ZIP: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (freshData.AppId != _game.AppId)
            {
                MessageBox.Show(
                    "This ZIP is for AppID " + freshData.AppId + " but this game is AppID " + _game.AppId + ".",
                    "Wrong Game", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Window.GetWindow(this)?.Close();

            var updateWindow = new UpdateWindow(_game, _settings, _gamesManager, _dialogService, freshData, _updateChecker);
            var window = _dialogService.CreateWindow("Update — " + _game.GameName, updateWindow, _dialogService.GetMainWindow());
            window.Width = 700;
            window.Height = 500;
            window.ShowDialog();
        }
    }
}
