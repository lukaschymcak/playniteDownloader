using System;
using System.IO;
using System.Windows;

namespace LuDownloader.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var userDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LuDownloader");
            Directory.CreateDirectory(userDataPath);

            BlankPlugin.CoreLogManager.SetFactory(
                () => new Logging.FileLogger(Path.Combine(userDataPath, "ludownloader.log")));

            var settings = Settings.StandaloneSettings.Load(userDataPath);
            var dialogService = new Services.StandaloneDialogService();
            var appHost = new Services.StandaloneAppHost(userDataPath, dialogService, settings);

            var mainWindow = new MainWindow(settings, dialogService, appHost);
            appHost.SetMainWindow(mainWindow);
            dialogService.SetMainWindow(mainWindow);
            mainWindow.Show();
        }
    }
}
