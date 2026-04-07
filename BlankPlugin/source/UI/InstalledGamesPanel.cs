using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlankPlugin
{
    /// <summary>
    /// Sidebar panel showing all games installed through BlankPlugin.
    /// Each entry shows game name, size, install path, and an uninstall button.
    /// Double-click opens the game folder in Explorer.
    /// </summary>
    public class InstalledGamesPanel : UserControl
    {
        private readonly InstalledGamesManager _manager;
        private StackPanel _gamesList;
        private TextBlock _statusText;
        private Button _refreshButton;

        public InstalledGamesPanel(InstalledGamesManager manager)
        {
            _manager = manager;

            var mainPanel = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(8)
            };

            // Header bar with title and refresh button
            var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(headerPanel, Dock.Top);

            var title = new TextBlock
            {
                Text = "Installed Games",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(title, Dock.Left);
            headerPanel.Children.Add(title);

            _refreshButton = new Button
            {
                Content = "Refresh",
                Width = 60,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _refreshButton.Click += (s, e) => RefreshList();
            headerPanel.Children.Add(_refreshButton);

            mainPanel.Children.Add(headerPanel);

            // Status text
            _statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 11
            };
            DockPanel.SetDock(_statusText, Dock.Top);
            mainPanel.Children.Add(_statusText);

            // Scrollable games list
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _gamesList = new StackPanel();
            scrollViewer.Content = _gamesList;

            mainPanel.Children.Add(scrollViewer);
            Content = mainPanel;

            RefreshList();
        }

        public void RefreshList()
        {
            _gamesList.Children.Clear();

            var games = _manager.ScanLibrary();

            if (games.Count == 0)
            {
                _statusText.Text = "No games installed through BlankPlugin.";
                var emptyText = new TextBlock
                {
                    Text = "Download a game to see it here.",
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                };
                _gamesList.Children.Add(emptyText);
                return;
            }

            _statusText.Text = games.Count + " game(s) installed";

            foreach (var game in games.OrderBy(g => g.GameName))
            {
                _gamesList.Children.Add(CreateGameEntry(game));
            }
        }

        private Border CreateGameEntry(InstalledGame game)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var entryPanel = new DockPanel { LastChildFill = true };

            // Uninstall button on the right
            var uninstallBtn = new Button
            {
                Content = "Uninstall",
                Width = 70,
                Height = 22,
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            uninstallBtn.Click += (s, e) =>
            {
                UninstallGame(game, border);
            };
            DockPanel.SetDock(uninstallBtn, Dock.Right);
            entryPanel.Children.Add(uninstallBtn);

            // Open folder button
            var openBtn = new Button
            {
                Content = "Open",
                Width = 50,
                Height = 22,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            openBtn.Click += (s, e) =>
            {
                if (Directory.Exists(game.InstallPath))
                    Process.Start("explorer.exe", game.InstallPath);
            };
            DockPanel.SetDock(openBtn, Dock.Right);
            entryPanel.Children.Add(openBtn);

            // Game info on the left
            var infoPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = game.GameName,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium,
                FontSize = 13
            };
            infoPanel.Children.Add(nameText);

            var detailText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var sizeStr = SteamLibraryHelper.FormatSize(game.SizeOnDisk);
            var dateStr = game.InstalledDate.ToString("yyyy-MM-dd");
            detailText.Text = sizeStr + "  |  " + dateStr + "  |  " + game.InstallPath;
            infoPanel.Children.Add(detailText);

            entryPanel.Children.Add(infoPanel);

            // Double-click to open folder
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && Directory.Exists(game.InstallPath))
                {
                    Process.Start("explorer.exe", game.InstallPath);
                }
            };

            border.Child = entryPanel;
            return border;
        }

        private void UninstallGame(InstalledGame game, Border entryBorder)
        {
            var result = MessageBox.Show(
                string.Format("Uninstall \"{0}\"?\n\nThis will delete:\n{1}\n\nSave files in Documents/My Games and AppData will be preserved.",
                    game.GameName, game.InstallPath),
                "Uninstall Game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // Delete game folder
                if (Directory.Exists(game.InstallPath))
                {
                    PreserveSaveFiles(game);
                    Directory.Delete(game.InstallPath, true);
                }

                // Delete ACF manifest
                if (!string.IsNullOrEmpty(game.LibraryPath))
                {
                    var acfPath = Path.Combine(game.LibraryPath, "steamapps",
                        "appmanifest_" + game.AppId + ".acf");
                    if (File.Exists(acfPath))
                        File.Delete(acfPath);
                }

                // Remove from installed games list
                _manager.Remove(game.AppId);

                // Remove the entry from the UI
                _gamesList.Children.Remove(entryBorder);

                // Update status
                var remaining = _manager.GetAll();
                _statusText.Text = remaining.Count + " game(s) installed";

                if (remaining.Count == 0)
                {
                    _statusText.Text = "No games installed through BlankPlugin.";
                    var emptyText = new TextBlock
                    {
                        Text = "Download a game to see it here.",
                        Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 24, 0, 0)
                    };
                    _gamesList.Children.Add(emptyText);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during uninstall: " + ex.Message, "Uninstall Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Preserves common save file locations before deleting the game folder.
        /// Copies saves to a backup directory if they exist.
        /// </summary>
        private void PreserveSaveFiles(InstalledGame game)
        {
            try
            {
                var backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BlankPlugin", "save_backups", game.AppId);

                var saveLocations = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", game.GameName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), game.GameName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), game.GameName)
                };

                foreach (var savePath in saveLocations)
                {
                    if (Directory.Exists(savePath))
                    {
                        var dest = Path.Combine(backupDir, Path.GetFileName(savePath));
                        CopyDirectory(savePath, dest);
                    }
                }
            }
            catch
            {
                // Save preservation is best-effort; don't block uninstall
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
