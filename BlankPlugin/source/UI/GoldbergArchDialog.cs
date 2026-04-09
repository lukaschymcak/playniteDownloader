using System.Windows;
using System.Windows.Controls;

namespace BlankPlugin
{
    /// <summary>
    /// Simple modal dialog asking the user to pick x64 or x32 architecture
    /// when it cannot be auto-detected from the game directory.
    /// </summary>
    public class GoldbergArchDialog : UserControl
    {
        private string _result;

        private GoldbergArchDialog()
        {
            var stack = new StackPanel { Margin = new Thickness(16) };

            stack.Children.Add(new TextBlock
            {
                Text = "Could not auto-detect architecture. Select the DLL type used by this game:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var x64Radio = new RadioButton
            {
                Content = "64-bit  (steam_api64.dll)",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var x32Radio = new RadioButton
            {
                Content = "32-bit  (steam_api.dll)",
                Margin = new Thickness(0, 0, 0, 16)
            };

            stack.Children.Add(x64Radio);
            stack.Children.Add(x32Radio);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 72,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 72,
                Padding = new Thickness(0, 6, 0, 6),
                IsCancel = true
            };

            okBtn.Click += (s, e) =>
            {
                _result = x64Radio.IsChecked == true ? "x64" : "x32";
                Window.GetWindow(this)?.Close();
            };
            cancelBtn.Click += (s, e) =>
            {
                _result = null;
                Window.GetWindow(this)?.Close();
            };

            buttonRow.Children.Add(okBtn);
            buttonRow.Children.Add(cancelBtn);
            stack.Children.Add(buttonRow);

            Content = stack;
        }

        /// <summary>Returns "x64", "x32", or null if cancelled.</summary>
        public static string ShowPicker(Window owner)
        {
            var dialog = new GoldbergArchDialog();
            var window = new Window
            {
                Title                 = "Select Architecture",
                Width                 = 340,
                SizeToContent         = SizeToContent.Height,
                ResizeMode            = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner                 = owner,
                Content               = dialog
            };
            window.ShowDialog();
            return dialog._result;
        }
    }
}
