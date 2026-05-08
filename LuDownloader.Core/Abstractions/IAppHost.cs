namespace BlankPlugin
{
    public interface IAppHost
    {
        void OpenDownloadForAppId(string appId, string name, string imageUrl = null);
        string UserDataPath { get; }
        InstalledGamesManager InstalledGames { get; }
        void RemoveFromHostLibrary(string playniteGameId);
        void ShowNotification(string message, bool isError = false);
        ReconcileResult ReconcileInstalledState();
    }
}
