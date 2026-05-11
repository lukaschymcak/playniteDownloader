using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuDownloader.App
{
    public class MainWindow : Window
    {
        private readonly BlankPlugin.PluginMainView _mainView;
        private readonly BlankPlugin.InstalledGamesManager _installedGames;
        private readonly BlankPlugin.LibraryGamesManager _libraryGames;
        private readonly BlankPlugin.UpdateChecker _updateChecker;

        public MainWindow(
            BlankPlugin.AppSettings settings,
            BlankPlugin.IDialogService dialogService,
            Services.StandaloneAppHost appHost)
        {
            Title = "LuDownloader";
            Width = 1100;
            Height = 750;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 35));

            _installedGames = new BlankPlugin.InstalledGamesManager(appHost.UserDataPath);
            _libraryGames   = new BlankPlugin.LibraryGamesManager(appHost.UserDataPath);
            var runner       = new BlankPlugin.ManifestCheckerRunner();
            _updateChecker   = new BlankPlugin.UpdateChecker(runner, _installedGames, appHost);

            appHost.Initialize(_installedGames, _updateChecker, settings, dialogService);

            _mainView = new BlankPlugin.PluginMainView(
                settings, _installedGames, _libraryGames, dialogService, _updateChecker, appHost);

            var menuBar = new Menu();
            var settingsMenu = new MenuItem { Header = "Settings" };
            settingsMenu.Click += (s, e) =>
            {
                var win = new SettingsWindow((Settings.StandaloneSettings)settings, this);
                win.ShowDialog();
            };
            menuBar.Items.Add(settingsMenu);

            DockPanel.SetDock(menuBar, Dock.Top);

            var outer = new DockPanel();
            outer.Children.Add(menuBar);
            outer.Children.Add(_mainView);
            Content = outer;

            Loaded += (s, e) => _ = _updateChecker.RunAsync();
        }

        public void CancelActiveOperations()
        {
            _updateChecker?.Cancel();
        }
    }
}
