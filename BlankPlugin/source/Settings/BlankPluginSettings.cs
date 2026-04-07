using Playnite.SDK;
using System.Collections.Generic;

namespace BlankPlugin
{
    public class BlankPluginSettings : ObservableObject, ISettings
    {
        private readonly BlankPlugin _plugin;

        private string _apiKey = string.Empty;
        public string ApiKey
        {
            get => _apiKey;
            set => SetValue(ref _apiKey, value);
        }

        private string _downloadPath = string.Empty;
        public string DownloadPath
        {
            get => _downloadPath;
            set => SetValue(ref _downloadPath, value);
        }

        private int _maxDownloads = 20;
        public int MaxDownloads
        {
            get => _maxDownloads;
            set => SetValue(ref _maxDownloads, value < 1 ? 1 : value > 64 ? 64 : value);
        }

        private string _steamUsername = string.Empty;
        public string SteamUsername
        {
            get => _steamUsername;
            set => SetValue(ref _steamUsername, value ?? string.Empty);
        }

        // Parameterless constructor required by LoadPluginSettings
        public BlankPluginSettings() { }

        public BlankPluginSettings(BlankPlugin plugin)
        {
            _plugin = plugin;
            var saved = plugin.LoadPluginSettings<BlankPluginSettings>();
            if (saved != null)
            {
                ApiKey = saved.ApiKey;
                DownloadPath = saved.DownloadPath;
                MaxDownloads = saved.MaxDownloads > 0 ? saved.MaxDownloads : 20;
                SteamUsername = saved.SteamUsername ?? string.Empty;
            }
        }

        public void BeginEdit() { }
        public void CancelEdit() { }

        public void EndEdit()
        {
            _plugin.SavePluginSettings(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(ApiKey))
                errors.Add("Morrenus API key is required.");
            if (string.IsNullOrWhiteSpace(DownloadPath))
                errors.Add("Download path is required.");
            return errors.Count == 0;
        }
    }
}
