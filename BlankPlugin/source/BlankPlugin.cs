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
    public class BlankPlugin : GenericPlugin, IAppHost
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        internal BlankPluginSettings Settings { get; private set; }
        public InstalledGamesManager InstalledGames { get; private set; }
        internal LibraryGamesManager LibraryGames { get; private set; }
        internal string PluginUserDataPath => GetPluginUserDataPath();

        public string UserDataPath => GetPluginUserDataPath();

        private PlayniteDialogService _dialogService;
        private UpdateChecker _updateChecker;
        private Game _lastSelectedGame;

        public BlankPlugin(IPlayniteAPI api) : base(api)
        {
            CoreLogManager.SetFactory(() => new PlayniteLoggerAdapter(Playnite.SDK.LogManager.GetLogger()));
            _dialogService = new PlayniteDialogService(api);
            Settings = new BlankPluginSettings(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override UserControl GetSettingsView(bool firstRunSettings)
            => new BlankPluginSettingsView(Settings);

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("LuDownloader started.");
            InstalledGames = new InstalledGamesManager(GetPluginUserDataPath());
            LibraryGames   = new LibraryGamesManager(GetPluginUserDataPath());

            try
            {
                var reconcile = ReconcileInstalledState();
                logger.Info("Startup reconcile: added=" + reconcile.Added + ", updated=" + reconcile.Updated + ", removed=" + reconcile.Removed);
            }
            catch (Exception ex)
            {
                logger.Warn("Startup reconcile failed: " + ex.Message);
            }

            // Initialize update checker
            var runner = new ManifestCheckerRunner();
            _updateChecker = new UpdateChecker(runner, InstalledGames, this);

            // Run update check on startup (fire and forget — does not block Playnite)
            _ = _updateChecker.RunAsync();
        }

        public ReconcileResult ReconcileInstalledState()
        {
            if (InstalledGames == null)
                return new ReconcileResult();

            var steamPluginGuid = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");
            var steamAppIds = PlayniteApi?.Database?.Games
                ?.Where(g => g != null && g.PluginId == steamPluginGuid && !string.IsNullOrWhiteSpace(g.GameId))
                ?.Select(g => g.GameId);
            return InstalledGames.ReconcileWithSteamLibraries(LibraryGames?.GetAll(), steamAppIds);
        }

        public void RemoveFromHostLibrary(string playniteGameId)
        {
            if (Guid.TryParse(playniteGameId, out var guid))
            {
                var game = PlayniteApi?.Database?.Games?.Get(guid);
                if (game != null)
                    PlayniteApi.Database.Games.Remove(game);
            }
        }

        public void ShowNotification(string message, bool isError = false)
        {
            PlayniteApi?.Notifications?.Add(new NotificationMessage(
                "ludownloader_" + DateTime.Now.Ticks,
                message,
                isError ? NotificationType.Error : NotificationType.Info));
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("LuDownloader stopped.");
            _updateChecker?.Cancel();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            logger.Info("LuDownloader: library updated, triggering update check.");
            _ = _updateChecker?.RunAsync();
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "LuDownloader",
                Icon = new TextBlock { Text = "LD" },
                Type = SiderbarItemType.Button,
                Activated = () => OpenPluginWindow(_lastSelectedGame)
            };

        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                MenuSection = "LuDownloader",
                Description = "Open in LuDownloader",
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
                        MenuSection = "LuDownloader",
                        Description = "Open Install Folder",
                        Action = menuArgs =>
                        {
                            if (Directory.Exists(installed.InstallPath))
                                Process.Start("explorer.exe", installed.InstallPath);
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "LuDownloader",
                        Description = "Uninstall",
                        Action = menuArgs =>
                        {
                            var result = _dialogService.ShowMessage(
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
                                    _dialogService.ShowMessage("Error during uninstall: " + ex.Message, "Uninstall Error",
                                        MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "LuDownloader",
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
                                    _dialogService.ShowMessage("Steamless completed.", "DRM Removal",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    _dialogService.ShowMessage("No Steam DRM detected on executables.", "Steamless",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            catch (Exception ex)
                            {
                                _dialogService.ShowMessage("Steamless error: " + ex.Message, "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "LuDownloader",
                        Description = "Apply Goldberg Emulator",
                        Action = menuArgs =>
                        {
                            if (!Directory.Exists(installed.InstallPath)) return;
                            if (string.IsNullOrWhiteSpace(Settings.GoldbergFilesPath))
                            {
                                _dialogService.ShowMessage("Goldberg files path not configured. Open Plugin Settings first.",
                                    "Goldberg", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            var detectedArch = GoldbergRunner.DetectArch(installed.InstallPath);
                            var appOutputDir = System.IO.Path.Combine(
                                Settings.GoldbergFilesPath, "generate_emu_config", "_OUTPUT", installed.AppId.Trim());
                            var options = GoldbergOptionsDialog.ShowPicker(
                                _dialogService.GetMainWindow(), _dialogService, detectedArch, appOutputDir);
                            if (options == null) return;
                            try
                            {
                                var runner = new GoldbergRunner(Settings.GoldbergFilesPath);
                                var outputPath = runner.Run(installed.InstallPath, installed.AppId, options, Settings,
                                    line => logger.Info("[Goldberg] " + line),
                                    gseSavesCopied: installed.GseSavesCopied);
                                if (string.IsNullOrEmpty(outputPath))
                                {
                                    _dialogService.ShowMessage(
                                        "Goldberg setup did not complete. Check the Playnite log for details.",
                                        "Goldberg", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    return;
                                }
                                installed.GseSavesCopied = true;
                                InstalledGames.Save(installed);
                                if (!options.CopyFiles)
                                {
                                    _dialogService.ShowMessage(
                                        "Goldberg files were generated here:\n\n" + outputPath,
                                        "Goldberg", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    _dialogService.ShowMessage("Goldberg emulator applied successfully.", "Goldberg",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            catch (Exception ex)
                            {
                                _dialogService.ShowMessage("Goldberg error: " + ex.Message, "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    yield return new GameMenuItem
                    {
                        MenuSection = "LuDownloader",
                        Description = "Update Game",
                        Action = menuArgs =>
                        {
                            var dialog = new UpdateGameDialog(
                                installed, Settings, InstalledGames, _dialogService, _updateChecker);
                            var window = _dialogService.CreateWindow(
                                "Update Game — " + installed.GameName,
                                dialog,
                                _dialogService.GetMainWindow());
                            window.Width = 480;
                            window.Height = 300;
                            window.ShowDialog();
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Opens the main Library/Search shell when <paramref name="game"/> is null (e.g. sidebar);
        /// opens <see cref="DownloadView"/> for the selected game when non-null (game context menu).
        /// </summary>
        private void OpenPluginWindow(Game game)
        {
            if (game != null)
            {
                var manifestCache = ManifestCache.GetCacheDirectory(GetPluginUserDataPath());
                var links = game.Links?.Select(l => l.Url);
                var view = new DownloadView(
                    game.Name, game.GameId, game.PluginId, game.Id, links,
                    Settings, InstalledGames, _dialogService, this, _updateChecker, manifestCache);
                var window = _dialogService.CreateWindow("LuDownloader — " + game.Name, view, _dialogService.GetMainWindow());
                window.Width = 700;
                window.Height = 600;
                window.ShowDialog();
            }
            else
            {
                var view = new PluginMainView(Settings, InstalledGames, LibraryGames, _dialogService, _updateChecker, this);
                var window = _dialogService.CreateWindow("LuDownloader", view, _dialogService.GetMainWindow());
                window.Width = 800;
                window.Height = 600;
                window.ShowDialog();
            }
            _lastSelectedGame = null;
        }

        public void OpenDownloadForAppId(string appId, string name, string imageUrl = null)
        {
            var manifestCache = ManifestCache.GetCacheDirectory(GetPluginUserDataPath());
            var view = new DownloadView(appId, name, Settings, InstalledGames, _dialogService, this, _updateChecker, manifestCache, imageUrl);
            var window = _dialogService.CreateWindow("LuDownloader — " + name, view, _dialogService.GetMainWindow());
            window.Width = 700;
            window.Height = 600;
            window.ShowDialog();
        }
    }
}
