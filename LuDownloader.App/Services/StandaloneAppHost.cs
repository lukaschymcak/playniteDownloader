using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LuDownloader.App.Services
{
    public class StandaloneAppHost : BlankPlugin.IAppHost
    {
        private readonly StandaloneDialogService _dialogs;
        private BlankPlugin.InstalledGamesManager _installedGames;
        private BlankPlugin.UpdateChecker _updateChecker;
        private BlankPlugin.AppSettings _settings;
        private BlankPlugin.IDialogService _dialogService;
        private Action<string, string, string> _openDownloadAction;
        private Window _mainWindow;

        public string UserDataPath { get; }

        public BlankPlugin.InstalledGamesManager InstalledGames => _installedGames;

        public StandaloneAppHost(string dataPath, StandaloneDialogService dialogs, BlankPlugin.AppSettings settings)
        {
            UserDataPath = dataPath;
            _dialogs = dialogs;
            _settings = settings;
        }

        public void SetMainWindow(Window w) => _mainWindow = w;

        public void Initialize(
            BlankPlugin.InstalledGamesManager installedGames,
            BlankPlugin.UpdateChecker updateChecker,
            BlankPlugin.AppSettings settings,
            BlankPlugin.IDialogService dialogService)
        {
            _installedGames = installedGames;
            _updateChecker = updateChecker;
            _settings = settings;
            _dialogService = dialogService;
        }

        public void SetOpenDownloadAction(Action<string, string, string> action)
            => _openDownloadAction = action;

        public void OpenDownloadForAppId(string appId, string name, string imageUrl = null)
        {
            if (_openDownloadAction != null)
            {
                _openDownloadAction(appId, name, imageUrl);
                return;
            }
            // Fallback: open a standalone DownloadView window
            Application.Current.Dispatcher.Invoke(() =>
            {
                var manifestCache = BlankPlugin.ManifestCache.GetCacheDirectory(UserDataPath);
                var view = new BlankPlugin.DownloadView(
                    appId, name, _settings, _installedGames, _dialogService, this, _updateChecker, manifestCache, imageUrl);
                var win = _dialogService.CreateWindow("Download — " + name, view, _mainWindow);
                win.Width = 900;
                win.Height = 700;
                win.SizeToContent = SizeToContent.Manual;
                win.Show();
            });
        }

        public BlankPlugin.ReconcileResult ReconcileInstalledState()
        {
            if (_installedGames == null) return new BlankPlugin.ReconcileResult();
            return _installedGames.ReconcileWithSteamLibraries(
                Enumerable.Empty<BlankPlugin.SavedLibraryGame>(),
                System.Linq.Enumerable.Empty<string>());
        }

        public void RemoveFromHostLibrary(string playniteGameId) { /* no Playnite DB in standalone */ }

        public void ShowNotification(string message, bool isError = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
                _dialogs.ShowMessage(message, isError ? "Error" : "LuDownloader",
                    MessageBoxButton.OK,
                    isError ? MessageBoxImage.Error : MessageBoxImage.Information));
        }
    }
}
