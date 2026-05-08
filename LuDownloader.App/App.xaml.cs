using System;
using System.IO;
using System.Windows;

namespace LuDownloader.App
{
    public partial class App : Application
    {
        private Settings.StandaloneSettings _settings;

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

            var mainWindow = new MainWindow(_settings, dialogService, appHost);
            appHost.SetMainWindow(mainWindow);
            dialogService.SetMainWindow(mainWindow);
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _settings?.Save();
            base.OnExit(e);
        }
    }
}
