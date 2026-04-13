using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlankPlugin
{
    /// <summary>
    /// Shows a grid of IGDB artwork thumbnails so the user can pick a background image.
    /// </summary>
    public class IgdbBackgroundPickerDialog : UserControl
    {
        private readonly IgdbClient _igdb;
        private readonly List<string> _imageIds;

        private WrapPanel _grid;
        private TextBlock _statusText;
        private Button _okBtn;
        private string _selectedImageId;

        public string SelectedImageId => _selectedImageId;

        public IgdbBackgroundPickerDialog(List<string> imageIds, IgdbClient igdb)
        {
            _imageIds = imageIds;
            _igdb     = igdb;

            Content = BuildLayout();
            Loaded += (s, e) => LoadThumbnails();
        }

        // ── Layout ────────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: header
            var header = new TextBlock
            {
                Text = "Select a background image:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Row 1: scrollable thumbnail grid
            _grid = new WrapPanel { Orientation = Orientation.Horizontal };
            var scroll = new ScrollViewer
            {
                Content = _grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // Row 2: status + buttons
            var bottomBar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };

            var skipBtn = new Button
            {
                Content = "Skip",
                Width = 70,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0)
            };
            skipBtn.Click += (s, e) =>
            {
                _selectedImageId = null;
                Window.GetWindow(this)?.Close();
            };
            DockPanel.SetDock(skipBtn, Dock.Right);
            bottomBar.Children.Add(skipBtn);

            _okBtn = new Button
            {
                Content = "Use this",
                Width = 80,
                Height = 28,
                IsEnabled = false
            };
            _okBtn.Click += (s, e) => Window.GetWindow(this)?.Close();
            DockPanel.SetDock(_okBtn, Dock.Right);
            bottomBar.Children.Add(_okBtn);

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                FontSize = 11
            };
            bottomBar.Children.Add(_statusText);

            Grid.SetRow(bottomBar, 2);
            root.Children.Add(bottomBar);

            var border = new Border { Padding = new Thickness(14), Child = root };
            TextElement.SetForeground(border, Brushes.WhiteSmoke);
            return border;
        }

        // ── Thumbnail loading ─────────────────────────────────────────────────────

        private void LoadThumbnails()
        {
            _statusText.Text = "Loading images...";

            // Load each thumbnail on a background thread and add to the grid as it arrives
            foreach (var imageId in _imageIds)
            {
                var id = imageId; // capture for closure
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var thumbPath = _igdb.DownloadArtworkThumbByImageId(id);
                        Dispatcher.Invoke(() => AddThumbnail(id, thumbPath));
                    }
                    catch
                    {
                        Dispatcher.Invoke(() => AddThumbnail(id, null));
                    }
                });
            }

            _statusText.Text = _imageIds.Count + " image(s) available — click to select";
        }

        private void AddThumbnail(string imageId, string thumbPath)
        {
            var cell = new Border
            {
                Width = 200,
                Height = 113,
                Margin = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Tag = imageId
            };

            if (thumbPath != null)
            {
                try
                {
                    var img = new Image { Stretch = Stretch.UniformToFill };
                    img.Source = new BitmapImage(new Uri(thumbPath, UriKind.Absolute));
                    cell.Child = img;
                }
                catch { AddPlaceholderText(cell); }
            }
            else
            {
                AddPlaceholderText(cell);
            }

            cell.MouseLeftButtonDown += (s, e) => SelectCell(imageId, cell);
            _grid.Children.Add(cell);
        }

        private static void AddPlaceholderText(Border cell)
        {
            cell.Child = new TextBlock
            {
                Text = "Image unavailable",
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
        }

        private void SelectCell(string imageId, Border clickedCell)
        {
            // Deselect all
            foreach (var child in _grid.Children)
            {
                if (child is Border b)
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
            }
            // Highlight selected
            clickedCell.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 220));
            _selectedImageId = imageId;
            _okBtn.IsEnabled = true;
        }

        // ── Static entry point ────────────────────────────────────────────────────

        public static string ShowPicker(Window owner, List<string> imageIds, IgdbClient igdb, IPlayniteAPI api)
        {
            if (imageIds == null || imageIds.Count == 0) return null;

            // Single image — use it without asking
            if (imageIds.Count == 1) return imageIds[0];

            var window = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });
            window.Owner = owner;

            window.Title = "Select Background Image";
            window.Width = 680;
            window.Height = 520;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var dialog = new IgdbBackgroundPickerDialog(imageIds, igdb);
            window.Content = dialog;
            window.ShowDialog();

            return dialog.SelectedImageId;
        }
    }
}
