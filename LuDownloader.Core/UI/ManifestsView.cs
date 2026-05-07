using System;
using System.Windows;
using System.Windows.Controls;

namespace BlankPlugin
{
    /// <summary>
    /// Lists cached Morrenus manifest ZIPs (plugin user data). Shown only in <see cref="PluginMainView"/>.
    /// </summary>
    public class ManifestsView : UserControl
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();

        private readonly IAppHost _appHost;
        private StackPanel _listPanel;
        private TextBlock _emptyLabel;

        public ManifestsView(IAppHost appHost)
        {
            _appHost = appHost ?? throw new ArgumentNullException(nameof(appHost));
            Content = BuildLayout();
            Loaded += (_, __) => RefreshList();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var refreshBtn = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            refreshBtn.Click += (_, __) => RefreshList();
            DockPanel.SetDock(refreshBtn, Dock.Left);
            top.Children.Add(refreshBtn);

            var hint = new TextBlock
            {
                Text = "Manifests saved from Morrenus downloads. Install opens the downloader for that AppID.",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            };
            top.Children.Add(hint);
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            _emptyLabel = new TextBlock
            {
                Text = "No saved manifests yet. Fetch a game from Morrenus in the downloader to cache one.",
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(4, 16, 4, 4),
                Visibility = Visibility.Collapsed
            };

            _listPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _listPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var stack = new StackPanel();
            stack.Children.Add(_emptyLabel);
            stack.Children.Add(scroll);
            Grid.SetRow(stack, 1);
            root.Children.Add(stack);

            return root;
        }

        private void RefreshList()
        {
            try
            {
                var cacheDir = ManifestCache.GetCacheDirectory(_appHost.UserDataPath);
                var entries = ManifestCache.EnumerateCached(cacheDir);

                _listPanel.Children.Clear();
                if (entries.Count == 0)
                {
                    _emptyLabel.Visibility = Visibility.Visible;
                    return;
                }

                _emptyLabel.Visibility = Visibility.Collapsed;
                foreach (var entry in entries)
                    _listPanel.Children.Add(BuildRow(entry, cacheDir));
            }
            catch (Exception ex)
            {
                logger.Error("ManifestsView.RefreshList failed: " + ex.Message);
                _listPanel.Children.Clear();
                _emptyLabel.Visibility = Visibility.Visible;
                _emptyLabel.Text = "Could not read manifest cache: " + ex.Message;
            }
        }

        private UIElement BuildRow(ManifestCacheEntry entry, string cacheDir)
        {
            var border = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var row = new DockPanel();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);

            var installBtn = new Button
            {
                Content = "Install",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            installBtn.Click += (_, __) =>
            {
                try
                {
                    _appHost.OpenDownloadForAppId(entry.AppId, entry.DisplayName);
                }
                catch (Exception ex)
                {
                    logger.Error("ManifestsView Install: " + ex.Message);
                    MessageBox.Show("Could not open downloader: " + ex.Message, "LuDownloader",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var deleteBtn = new Button
            {
                Content = "Delete",
                Padding = new Thickness(10, 6, 10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Click += (_, __) =>
            {
                var r = MessageBox.Show(
                    "Delete cached manifest for " + entry.DisplayName + " (AppID " + entry.AppId + ")?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
                try
                {
                    ManifestCache.DeleteCached(cacheDir, entry.AppId);
                    RefreshList();
                }
                catch (Exception ex)
                {
                    logger.Error("ManifestsView Delete: " + ex.Message);
                    MessageBox.Show("Delete failed: " + ex.Message, "LuDownloader",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            buttons.Children.Add(installBtn);
            buttons.Children.Add(deleteBtn);

            var leftStack = new StackPanel();
            leftStack.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 2)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = "AppID: " + entry.AppId,
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontSize = 12
            });

            row.Children.Add(buttons);
            row.Children.Add(leftStack);

            border.Child = row;
            return border;
        }
    }
}
