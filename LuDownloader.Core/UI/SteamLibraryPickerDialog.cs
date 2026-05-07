using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlankPlugin
{
    /// <summary>
    /// Modal dialog that lets the user pick a Steam library folder
    /// before starting a download. Shows each library with its path
    /// and available disk space.
    /// </summary>
    public class SteamLibraryPickerDialog : UserControl
    {
        private readonly List<string> _libraries;
        private readonly ListBox _listBox;
        private string _selectedPath;

        /// <summary>The library path the user selected, or null if cancelled.</summary>
        public string SelectedPath => _selectedPath;

        public SteamLibraryPickerDialog(List<string> libraries)
        {
            _libraries = libraries ?? new List<string>();

            var stack = new StackPanel { Margin = new Thickness(16) };

            // Title
            var title = new TextBlock
            {
                Text = "Select a Steam Library",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = "Choose where to install the game:",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(subtitle);

            // Library list
            _listBox = new ListBox
            {
                Height = Math.Min(200, _libraries.Count * 60 + 20),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                Margin = new Thickness(0, 0, 0, 16)
            };

            foreach (var lib in _libraries)
            {
                var freeSpace = SteamLibraryHelper.GetFreeDiskSpace(lib);
                var item = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };

                var pathText = new TextBlock
                {
                    Text = lib,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Medium
                };
                item.Children.Add(pathText);

                var spaceText = new TextBlock
                {
                    Text = "Free: " + SteamLibraryHelper.FormatSize(freeSpace),
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                item.Children.Add(spaceText);

                _listBox.Items.Add(item);
            }

            if (_listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;

            stack.Children.Add(_listBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            };
            okButton.Click += (s, e) =>
            {
                if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _libraries.Count)
                {
                    _selectedPath = _libraries[_listBox.SelectedIndex];
                }
                var wnd = Window.GetWindow(this);
                wnd?.Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28
            };
            cancelButton.Click += (s, e) =>
            {
                _selectedPath = null;
                var wnd = Window.GetWindow(this);
                wnd?.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            Content = stack;
        }

        /// <summary>
        /// Shows the picker as a modal dialog. Returns the selected library path,
        /// or null if the user cancelled.
        /// </summary>
        public static string ShowPicker(System.Windows.Window owner, List<string> libraries, IDialogService dialogService)
        {
            if (libraries == null || libraries.Count == 0)
                return null;

            // Auto-select if only one library
            if (libraries.Count == 1)
                return libraries[0];

            var picker = new SteamLibraryPickerDialog(libraries);
            var window = dialogService.CreateWindow("Select Steam Library", picker, owner);
            window.Width = 500;
            window.Height = 350;
            window.ShowDialog();

            return picker.SelectedPath;
        }
    }
}
