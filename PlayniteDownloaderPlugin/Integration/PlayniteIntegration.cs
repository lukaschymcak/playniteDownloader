using System.Collections.ObjectModel;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteDownloaderPlugin.Integration;

public class PlayniteIntegration
{
    private readonly IPlayniteAPI _api;

    public PlayniteIntegration(IPlayniteAPI api) => _api = api;

    public void MarkInstalled(string gameId, string installDirectory, string? executablePath)
    {
        if (!Guid.TryParse(gameId, out Guid guid)) return;

        Game? game = _api.Database.Games.Get(guid);
        if (game == null) return;

        game.IsInstalled = true;
        game.InstallDirectory = installDirectory;

        if (executablePath != null)
        {
            game.GameActions = new ObservableCollection<GameAction>
            {
                new GameAction
                {
                    Name = "Play",
                    Path = executablePath,
                    Type = GameActionType.File,
                    IsPlayAction = true
                }
            };
        }
        else
        {
            game.GameActions = new ObservableCollection<GameAction>();
        }

        _api.Database.Games.Update(game);
    }
}
