using System.IO;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

namespace BlankPlugin
{
    /// <summary>
    /// Combined Goldberg options: architecture (when needed), run mode, and whether to copy into the game dir.
    /// </summary>
    public class GoldbergOptionsDialog : UserControl
    {
        private GoldbergOptions _result;
        private readonly StackPanel _archSection;
        private readonly RadioButton _modeFullRadio;
        private readonly RadioButton _modeAchievementsRadio;
        private readonly CheckBox _copyCheck;
        private readonly TextBlock _infoLabel;
        private readonly string _detectedArch;
        private readonly string _appOutputDir;

        private GoldbergOptionsDialog(string detectedArch, string appOutputDir)
        {
            _detectedArch = detectedArch;
            _appOutputDir = appOutputDir ?? "";

            var stack = new StackPanel { Margin = new Thickness(16) };

            // Arch picker shown only when DetectArch() returned null (both or neither DLL found).
            _archSection = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            _archSection.Children.Add(new TextBlock
            {
                Text = "Could not auto-detect architecture. Select the DLL type used by this game:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
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
                Margin = new Thickness(0, 0, 0, 0)
            };
            _archSection.Children.Add(x64Radio);
            _archSection.Children.Add(x32Radio);
            if (!string.IsNullOrEmpty(detectedArch))
                _archSection.Visibility = Visibility.Collapsed;
            stack.Children.Add(_archSection);

            stack.Children.Add(new TextBlock
            {
                Text = "Install mode:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _modeFullRadio = new RadioButton
            {
                Content = "Full install (DLLs + achievements)",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _modeAchievementsRadio = new RadioButton
            {
                Content = "Achievements only (steam_settings folder)",
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_modeFullRadio);
            stack.Children.Add(_modeAchievementsRadio);

            _copyCheck = new CheckBox
            {
                Content = "Copy files to game directory",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_copyCheck);

            _infoLabel = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = SystemColors.GrayTextBrush,
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(_infoLabel);

            void refreshInfo()
            {
                if (_copyCheck.IsChecked == true)
                {
                    _infoLabel.Visibility = Visibility.Collapsed;
                    return;
                }
                _infoLabel.Visibility = Visibility.Visible;
                var steamSettingsPath = Path.Combine(_appOutputDir, "steam_settings");
                if (_modeAchievementsRadio.IsChecked == true)
                    _infoLabel.Text = "steam_settings: " + steamSettingsPath;
                else
                    _infoLabel.Text = "DLLs: " + _appOutputDir + "\nsteam_settings: " + steamSettingsPath;
            }

            _copyCheck.Checked += (s, e) => refreshInfo();
            _copyCheck.Unchecked += (s, e) => refreshInfo();
            _modeFullRadio.Checked += (s, e) => refreshInfo();
            _modeAchievementsRadio.Checked += (s, e) => refreshInfo();

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
                string arch;
                if (!string.IsNullOrEmpty(_detectedArch))
                    arch = _detectedArch;
                else
                    arch = x64Radio.IsChecked == true ? "x64" : "x32";

                var mode = _modeAchievementsRadio.IsChecked == true
                    ? GoldbergRunMode.AchievementsOnly
                    : GoldbergRunMode.Full;
                var copy = _copyCheck.IsChecked == true;
                _result = new GoldbergOptions(arch, mode, copy);
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

        /// <summary>Returns null if cancelled.</summary>
        public static GoldbergOptions ShowPicker(
            Window owner,
            IPlayniteAPI api,
            string detectedArch,
            string appOutputDir)
        {
            var dialog = new GoldbergOptionsDialog(detectedArch, appOutputDir);
            var window = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });
            window.Title = "Goldberg Emulator";
            window.Width = 420;
            window.SizeToContent = SizeToContent.Height;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Owner = owner;
            window.Content = dialog;
            window.ShowDialog();
            return dialog._result;
        }
    }
}
