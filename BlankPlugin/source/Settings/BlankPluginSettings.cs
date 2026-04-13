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

        private string _steamWebApiKey = string.Empty;
        public string SteamWebApiKey
        {
            get => _steamWebApiKey;
            set => SetValue(ref _steamWebApiKey, value ?? string.Empty);
        }

        private string _igdbClientId = string.Empty;
        public string IgdbClientId
        {
            get => _igdbClientId;
            set => SetValue(ref _igdbClientId, value ?? string.Empty);
        }

        private string _igdbClientSecret = string.Empty;
        public string IgdbClientSecret
        {
            get => _igdbClientSecret;
            set => SetValue(ref _igdbClientSecret, value ?? string.Empty);
        }

        private string _goldbergFilesPath = string.Empty;
        public string GoldbergFilesPath
        {
            get => _goldbergFilesPath;
            set => SetValue(ref _goldbergFilesPath, value ?? string.Empty);
        }

        private string _goldbergAccountName = string.Empty;
        public string GoldbergAccountName
        {
            get => _goldbergAccountName;
            set => SetValue(ref _goldbergAccountName, value ?? string.Empty);
        }

        private string _goldbergSteamId = string.Empty;
        public string GoldbergSteamId
        {
            get => _goldbergSteamId;
            set => SetValue(ref _goldbergSteamId, value ?? string.Empty);
        }

        /// <summary>In-memory copy taken when the Playnite settings dialog opens (Cancel restores this).</summary>
        private BlankPluginSettingsSnapshot _editSnapshot;

        private sealed class BlankPluginSettingsSnapshot
        {
            public string ApiKey;
            public string DownloadPath;
            public int MaxDownloads;
            public string SteamUsername;
            public string SteamWebApiKey;
            public string IgdbClientId;
            public string IgdbClientSecret;
            public string GoldbergFilesPath;
            public string GoldbergAccountName;
            public string GoldbergSteamId;
        }

        // Parameterless constructor required by LoadPluginSettings
        public BlankPluginSettings() { }

        public BlankPluginSettings(BlankPlugin plugin)
        {
            _plugin = plugin;
            var saved = plugin.LoadPluginSettings<BlankPluginSettings>();
            if (saved != null)
            {
                ApiKey        = saved.ApiKey;
                DownloadPath  = saved.DownloadPath;
                MaxDownloads  = saved.MaxDownloads > 0 ? saved.MaxDownloads : 20;
                SteamUsername  = saved.SteamUsername ?? string.Empty;
                SteamWebApiKey = saved.SteamWebApiKey ?? string.Empty;
                IgdbClientId     = saved.IgdbClientId     ?? string.Empty;
                IgdbClientSecret = saved.IgdbClientSecret ?? string.Empty;
                GoldbergFilesPath   = saved.GoldbergFilesPath   ?? string.Empty;
                GoldbergAccountName = saved.GoldbergAccountName ?? string.Empty;
                GoldbergSteamId     = saved.GoldbergSteamId     ?? string.Empty;
            }
        }

        public void BeginEdit()
        {
            _editSnapshot = new BlankPluginSettingsSnapshot
            {
                ApiKey              = ApiKey,
                DownloadPath        = DownloadPath,
                MaxDownloads        = MaxDownloads,
                SteamUsername       = SteamUsername,
                SteamWebApiKey      = SteamWebApiKey,
                IgdbClientId        = IgdbClientId,
                IgdbClientSecret    = IgdbClientSecret,
                GoldbergFilesPath   = GoldbergFilesPath,
                GoldbergAccountName = GoldbergAccountName,
                GoldbergSteamId     = GoldbergSteamId
            };
        }

        public void CancelEdit()
        {
            if (_editSnapshot == null)
                return;

            ApiKey              = _editSnapshot.ApiKey;
            DownloadPath        = _editSnapshot.DownloadPath;
            MaxDownloads        = _editSnapshot.MaxDownloads;
            SteamUsername       = _editSnapshot.SteamUsername;
            SteamWebApiKey      = _editSnapshot.SteamWebApiKey;
            IgdbClientId        = _editSnapshot.IgdbClientId;
            IgdbClientSecret    = _editSnapshot.IgdbClientSecret;
            GoldbergFilesPath   = _editSnapshot.GoldbergFilesPath;
            GoldbergAccountName = _editSnapshot.GoldbergAccountName;
            GoldbergSteamId     = _editSnapshot.GoldbergSteamId;
            _editSnapshot       = null;
        }

        public void EndEdit()
        {
            _editSnapshot = null;
            if (_plugin != null)
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
