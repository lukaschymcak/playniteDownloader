using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace BlankPlugin
{
    public class BlankPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        internal BlankPluginSettings Settings { get; private set; }
        internal InstalledGamesManager InstalledGames { get; private set; }
        internal LibraryGamesManager LibraryGames { get; private set; }

        private UpdateChecker _updateChecker;
        private Game _lastSelectedGame;

        public BlankPlugin(IPlayniteAPI api) : base(api)
        {
            Settings = new BlankPluginSettings(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override UserControl GetSettingsView(bool firstRunSettings)
            => new BlankPluginSettingsView(Settings);

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("BlankPlugin started.");
            InstalledGames = new InstalledGamesManager(GetPluginUserDataPath());
            LibraryGames   = new LibraryGamesManager(GetPluginUserDataPath());

            // Initialize update checker
            var runner = new ManifestCheckerRunner();
            _updateChecker = new UpdateChecker(runner, InstalledGames, PlayniteApi);

            // Run update check on startup (fire and forget — does not block Playnite)
            _ = _updateChecker.RunAsync();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("BlankPlugin stopped.");
            _updateChecker?.Cancel();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            logger.Info("BlankPlugin: library updated, triggering update check.");
            _ = _updateChecker?.RunAsync();
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "BlankPlugin",
                Icon = new TextBlock { Text = "BP" },
                Type = SiderbarItemType.Button,
                Activated = () => OpenPluginWindow(_lastSelectedGame)
            };

        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                MenuSection = "BlankPlugin",
                Description = "Open in BlankPlugin",
                Action = menuArgs =>
                {
                    _lastSelectedGame = menuArgs.Games.FirstOrDefault();
                    OpenPluginWindow(_lastSelectedGame);
                }
            };

            // Additional menu items for installed games
            var firstGame = args.Games.FirstOrDefault();
            if (firstGame != null && InstalledGames != null)
            {
                // Try to find the game by Playnite ID or by name matching
                var installed = InstalledGames.FindByPlayniteId(firstGame.Id);

                // If not found by Playnite ID, try matching by Steam GameId
                if (installed == null)
                {
                    var steamPluginGuid = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");
                    if (firstGame.PluginId == steamPluginGuid)
                    {
                        installed = InstalledGames.FindByAppId(firstGame.GameId);
                    }
                }

                if (installed != null)
                {
                    yield return new GameMenuItem
                    {
                        MenuSection = "BlankPlugin",
                        Description = "Open Install Folder",
                        Action = menuArgs =>
                        {
                            if (Directory.Exists(installed.InstallPath))
                                Process.Start("explorer.exe", installed.InstallPath);
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "BlankPlugin",
                        Description = "Uninstall",
                        Action = menuArgs =>
                        {
                            var result = MessageBox.Show(
                                string.Format("Uninstall \"{0}\"?\n\nThis will delete:\n{1}\n\nSave files in Documents/My Games and AppData will be preserved.",
                                    installed.GameName, installed.InstallPath),
                                "Uninstall Game",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (result == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    if (Directory.Exists(installed.InstallPath))
                                        Directory.Delete(installed.InstallPath, true);

                                    if (!string.IsNullOrEmpty(installed.LibraryPath))
                                    {
                                        var acfPath = Path.Combine(installed.LibraryPath, "steamapps",
                                            "appmanifest_" + installed.AppId + ".acf");
                                        if (File.Exists(acfPath))
                                            File.Delete(acfPath);
                                    }

                                    InstalledGames.Remove(installed.AppId);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error during uninstall: " + ex.Message, "Uninstall Error",
                                        MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "BlankPlugin",
                        Description = "Run Steamless (Strip DRM)",
                        Action = menuArgs =>
                        {
                            if (!Directory.Exists(installed.InstallPath)) return;
                            try
                            {
                                var check = SteamDrmChecker.Check(installed.InstallPath);
                                if (check.HasDrm)
                                {
                                    var runner = new SteamlessRunner(new DepotDownloaderRunner().DotnetPath);
                                    runner.Run(installed.InstallPath, line => { });
                                    installed.DrmStripped = true;
                                    InstalledGames.Save(installed);
                                    MessageBox.Show("Steamless completed.", "DRM Removal",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    MessageBox.Show("No Steam DRM detected on executables.", "Steamless",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Steamless error: " + ex.Message, "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "BlankPlugin",
                        Description = "Apply Goldberg Emulator",
                        Action = menuArgs =>
                        {
                            if (!Directory.Exists(installed.InstallPath)) return;
                            if (string.IsNullOrWhiteSpace(Settings.GoldbergFilesPath))
                            {
                                MessageBox.Show("Goldberg files path not configured. Open Plugin Settings first.",
                                    "Goldberg", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            var arch = GoldbergRunner.DetectArch(installed.InstallPath);
                            if (arch == null)
                            {
                                arch = GoldbergArchDialog.ShowPicker(PlayniteApi.Dialogs.GetCurrentAppWindow());
                                if (arch == null) return;
                            }
                            try
                            {
                                var runner = new GoldbergRunner(Settings.GoldbergFilesPath);
                                runner.Run(installed.InstallPath, installed.AppId, arch, Settings,
                                    line => logger.Info("[Goldberg] " + line),
                                    gseSavesCopied: installed.GseSavesCopied);
                                installed.GseSavesCopied = true;
                                InstalledGames.Save(installed);
                                MessageBox.Show("Goldberg emulator applied successfully.", "Goldberg",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Goldberg error: " + ex.Message, "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "BlankPlugin",
                        Description = "Update Game",
                        Action = menuArgs =>
                        {
                            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                            {
                                ShowMinimizeButton = false,
                                ShowMaximizeButton = false,
                                ShowCloseButton = true
                            });
                            window.Title = "Update Game — " + installed.GameName;
                            window.Width = 480;
                            window.Height = 300;
                            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                            window.Content = new UpdateGameDialog(
                                installed, Settings, InstalledGames, PlayniteApi, _updateChecker);
                            window.ShowDialog();
                        }
                    };
                }
            }
        }

        private void OpenPluginWindow(Game game)
        {
            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Width = 800;
            window.Height = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.Title = "BlankPlugin";
            window.Content = new PluginMainView(Settings, InstalledGames, LibraryGames, PlayniteApi, _updateChecker, this);
            window.ShowDialog();
            _lastSelectedGame = null;
        }

        public void OpenDownloadForAppId(string appId, string name)
        {
            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });
            window.Width = 700;
            window.Height = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.Title = "BlankPlugin \u2014 " + name;
            window.Content = new DownloadView(appId, name, Settings, InstalledGames, PlayniteApi, _updateChecker);
            window.ShowDialog();
        }
    }
}
