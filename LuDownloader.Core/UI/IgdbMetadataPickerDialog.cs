using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlankPlugin
{
    /// <summary>
    /// Modal dialog for searching IGDB and selecting a game to apply metadata from.
    /// </summary>
    public class IgdbMetadataPickerDialog : UserControl
    {
        private readonly IgdbClient _igdb;
        private readonly string _initialQuery;

        private TextBox _searchBox;
        private Button _searchBtn;
        private StackPanel _resultsList;
        private TextBlock _statusText;
        private Button _okBtn;

        private List<IgdbGameResult> _currentResults = new List<IgdbGameResult>();
        private IgdbGameResult _selected;

        public IgdbGameResult SelectedResult => _selected;

        public IgdbMetadataPickerDialog(string gameName, IgdbClient igdb)
        {
            _igdb         = igdb;
            _initialQuery = gameName;

            Content = BuildLayout();
            Loaded += (s, e) => BeginSearch(gameName);
        }

        // ── Layout ────────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0 search bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1 results
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 2 status + buttons

            // Row 0: search bar
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            _searchBtn = new Button
            {
                Content = "Search",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(6, 0, 0, 0)
            };
            _searchBtn.Click += (s, e) => BeginSearch(_searchBox.Text.Trim());
            DockPanel.SetDock(_searchBtn, Dock.Right);
            searchRow.Children.Add(_searchBtn);

            _searchBox = new TextBox
            {
                Text = _initialQuery,
                VerticalAlignment = VerticalAlignment.Center
            };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) BeginSearch(_searchBox.Text.Trim());
            };
            searchRow.Children.Add(_searchBox);

            Grid.SetRow(searchRow, 0);
            root.Children.Add(searchRow);

            // Row 1: results list
            _resultsList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _resultsList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // Row 2: status + OK/Cancel
            var bottomBar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0)
            };
            cancelBtn.Click += (s, e) =>
            {
                _selected = null;
                Window.GetWindow(this)?.Close();
            };
            DockPanel.SetDock(cancelBtn, Dock.Right);
            bottomBar.Children.Add(cancelBtn);

            _okBtn = new Button
            {
                Content = "Select",
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

        // ── Search ────────────────────────────────────────────────────────────────

        private void BeginSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            _searchBtn.IsEnabled = false;
            _okBtn.IsEnabled = false;
            _selected = null;
            _resultsList.Children.Clear();
            _statusText.Text = "Searching...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var results = _igdb.SearchWithDetails(query);
                    Dispatcher.Invoke(() => ShowResults(results));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _statusText.Text = "Search failed: " + ex.Message;
                        _searchBtn.IsEnabled = true;
                    });
                }
            });
        }

        private void ShowResults(List<IgdbGameResult> results)
        {
            _searchBtn.IsEnabled = true;
            _currentResults = results;
            _resultsList.Children.Clear();

            if (results.Count == 0)
            {
                _statusText.Text = "No results found.";
                return;
            }

            _statusText.Text = results.Count + " result(s) — click a game to select it";

            foreach (var r in results)
                _resultsList.Children.Add(CreateResultRow(r));
        }

        private Border CreateResultRow(IgdbGameResult result)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Thumbnail
            var thumb = new Border
            {
                Width = 45,
                Height = 64,
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                ClipToBounds = true,
                CornerRadius = new CornerRadius(2)
            };

            if (!string.IsNullOrEmpty(result.CoverImageId))
            {
                var img = new Image { Stretch = Stretch.UniformToFill };
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(
                        "https://images.igdb.com/igdb/image/upload/t_cover_small/" +
                        result.CoverImageId + ".jpg",
                        UriKind.Absolute);
                    bmp.DecodePixelWidth = 45;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    img.Source = bmp;
                }
                catch { /* leave blank */ }
                thumb.Child = img;
            }

            Grid.SetColumn(thumb, 0);
            grid.Children.Add(thumb);

            // Name + year
            var info = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            info.Children.Add(new TextBlock
            {
                Text = result.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (result.ReleaseYear.HasValue)
            {
                info.Children.Add(new TextBlock
                {
                    Text = result.ReleaseYear.Value.ToString(),
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 150)),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            row.Child = grid;

            // Selection handling
            row.MouseLeftButtonDown += (s, e) => SelectRow(result, row);

            return row;
        }

        private void SelectRow(IgdbGameResult result, Border clickedRow)
        {
            // Deselect all
            foreach (var child in _resultsList.Children)
            {
                if (child is Border b)
                    b.Background = new SolidColorBrush(Color.FromRgb(37, 37, 42));
            }
            // Highlight selected
            clickedRow.Background = new SolidColorBrush(Color.FromRgb(0, 100, 160));
            _selected = result;
            _okBtn.IsEnabled = true;
        }

        // ── Static entry point ────────────────────────────────────────────────────

        public static IgdbGameResult ShowPicker(Window owner, string gameName, IgdbClient igdb, IDialogService dialogService)
        {
            var dialog = new IgdbMetadataPickerDialog(gameName, igdb);
            var window = dialogService.CreateWindow("IGDB Metadata — " + gameName, dialog, owner);
            window.Width = 520;
            window.Height = 560;
            window.ShowDialog();

            return dialog.SelectedResult;
        }
    }
}
