namespace BlankPlugin
{
    public interface IAppHost
    {
        void OpenDownloadForAppId(string appId, string name, string imageUrl = null);
        string UserDataPath { get; }
        void RemoveFromHostLibrary(string playniteGameId);
    }
}
