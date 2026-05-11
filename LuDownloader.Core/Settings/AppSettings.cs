namespace BlankPlugin
{
    public class AppSettings : ObservableBase
    {
        private string _apiKey = string.Empty;
        public string ApiKey
        {
            get => _apiKey;
            set => SetValue(ref _apiKey, value ?? string.Empty);
        }

        private string _downloadPath = string.Empty;
        public string DownloadPath
        {
            get => _downloadPath;
            set => SetValue(ref _downloadPath, value ?? string.Empty);
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

        public void CopyValuesTo(AppSettings target)
        {
            if (target == null)
                return;

            target.ApiKey = ApiKey;
            target.DownloadPath = DownloadPath;
            target.MaxDownloads = MaxDownloads;
            target.SteamUsername = SteamUsername;
            target.SteamWebApiKey = SteamWebApiKey;
            target.IgdbClientId = IgdbClientId;
            target.IgdbClientSecret = IgdbClientSecret;
            target.GoldbergFilesPath = GoldbergFilesPath;
            target.GoldbergAccountName = GoldbergAccountName;
            target.GoldbergSteamId = GoldbergSteamId;
        }
    }
}
