using Playnite.SDK;
using System.Collections.Generic;

namespace BlankPlugin
{
    public class BlankPluginSettings : AppSettings, ISettings
    {
        private readonly BlankPlugin _plugin;
        private BlankPluginSettingsSnapshot _editSnapshot;

        private sealed class BlankPluginSettingsSnapshot
        {
            public string ApiKey, DownloadPath, SteamUsername, SteamWebApiKey;
            public int MaxDownloads;
            public string IgdbClientId, IgdbClientSecret;
            public string GoldbergFilesPath, GoldbergAccountName, GoldbergSteamId;
        }

        public BlankPluginSettings() { }

        public BlankPluginSettings(BlankPlugin plugin)
        {
            _plugin = plugin;
            var saved = plugin.LoadPluginSettings<BlankPluginSettings>();
            if (saved != null)
            {
                ApiKey              = saved.ApiKey;
                DownloadPath        = saved.DownloadPath;
                MaxDownloads        = saved.MaxDownloads > 0 ? saved.MaxDownloads : 20;
                SteamUsername       = saved.SteamUsername       ?? string.Empty;
                SteamWebApiKey      = saved.SteamWebApiKey      ?? string.Empty;
                IgdbClientId        = saved.IgdbClientId        ?? string.Empty;
                IgdbClientSecret    = saved.IgdbClientSecret    ?? string.Empty;
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
            if (_editSnapshot == null) return;
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
            _plugin?.SavePluginSettings(this);
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
