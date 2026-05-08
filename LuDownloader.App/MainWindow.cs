using System;
using System.Windows;

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

            _installedGames = new BlankPlugin.InstalledGamesManager(appHost.UserDataPath);
            _libraryGames   = new BlankPlugin.LibraryGamesManager(appHost.UserDataPath);
            var runner       = new BlankPlugin.ManifestCheckerRunner();
            _updateChecker   = new BlankPlugin.UpdateChecker(runner, _installedGames, appHost);

            appHost.Initialize(_installedGames, _updateChecker, settings, dialogService);

            _mainView = new BlankPlugin.PluginMainView(
                settings, _installedGames, _libraryGames, dialogService, _updateChecker, appHost);

            Content = _mainView;
        }
    }
}
