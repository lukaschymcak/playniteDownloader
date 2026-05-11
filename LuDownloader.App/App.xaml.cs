using System;
using System.IO;
using System.Windows;

namespace LuDownloader.App
{
    public partial class App : Application
    {
        private Settings.StandaloneSettings _settings;
        private MainWindow _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var userDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LuDownloader");
            Directory.CreateDirectory(userDataPath);

            var fileLogger = new Logging.FileLogger(Path.Combine(userDataPath, "ludownloader.log"));
            BlankPlugin.CoreLogManager.SetFactory(() => fileLogger);

            _settings = Settings.StandaloneSettings.Load(userDataPath);
            var dialogService = new Services.StandaloneDialogService();
            var appHost = new Services.StandaloneAppHost(userDataPath, dialogService, _settings);

            _mainWindow = new MainWindow(_settings, dialogService, appHost);
            appHost.SetMainWindow(_mainWindow);
            dialogService.SetMainWindow(_mainWindow);
            Application.Current.MainWindow = _mainWindow;
            _mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainWindow?.CancelActiveOperations();
            _settings?.Save();
            base.OnExit(e);
        }
    }
}
