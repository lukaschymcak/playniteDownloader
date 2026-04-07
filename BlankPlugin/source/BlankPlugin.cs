using System;
using System.Collections.Generic;
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
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("BlankPlugin stopped.");
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
        }

        private void OpenPluginWindow(Game game)
        {
            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Title = game != null ? "BlankPlugin — " + game.Name : "BlankPlugin";
            window.Width = 700;
            window.Height = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.Content = new GameWindow(game, Settings);
            window.ShowDialog();
            _lastSelectedGame = null;
        }
    }
}
