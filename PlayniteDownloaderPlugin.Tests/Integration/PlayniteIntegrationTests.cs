using Moq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteDownloaderPlugin.Integration;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Integration;

public class PlayniteIntegrationTests
{
    [Fact]
    public void MarkInstalled_SetsIsInstalledAndDirectory()
    {
        Game game = new Game { Id = Guid.NewGuid(), Name = "Test Game" };
        Mock<IGameDatabaseAPI> mockDb = new Mock<IGameDatabaseAPI>();
        mockDb.Setup(db => db.Games.Get(game.Id)).Returns(game);
        Mock<IPlayniteAPI> mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        PlayniteIntegration integration = new PlayniteIntegration(mockApi.Object);
        integration.MarkInstalled(game.Id.ToString(), @"C:\Games\TestGame", @"C:\Games\TestGame\game.exe");

        Assert.True(game.IsInstalled);
        Assert.Equal(@"C:\Games\TestGame", game.InstallDirectory);
        Assert.Single(game.GameActions);
        Assert.True(game.GameActions[0].IsPlayAction);
        Assert.Equal(@"C:\Games\TestGame\game.exe", game.GameActions[0].Path);
        mockDb.Verify(db => db.Games.Update(game), Times.Once);
    }

    [Fact]
    public void MarkInstalled_WithNoExe_SetsInstalledWithEmptyActions()
    {
        Game game = new Game { Id = Guid.NewGuid(), Name = "Test Game" };
        Mock<IGameDatabaseAPI> mockDb = new Mock<IGameDatabaseAPI>();
        mockDb.Setup(db => db.Games.Get(game.Id)).Returns(game);
        Mock<IPlayniteAPI> mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        PlayniteIntegration integration = new PlayniteIntegration(mockApi.Object);
        integration.MarkInstalled(game.Id.ToString(), @"C:\Games\TestGame", null);

        Assert.True(game.IsInstalled);
        Assert.Empty(game.GameActions);
        mockDb.Verify(db => db.Games.Update(game), Times.Once);
    }

    [Fact]
    public void MarkInstalled_DoesNothingWhenGameNotFound()
    {
        Mock<IGameDatabaseAPI> mockDb = new Mock<IGameDatabaseAPI>();
        mockDb.Setup(db => db.Games.Get(It.IsAny<Guid>())).Returns((Game?)null!);
        Mock<IPlayniteAPI> mockApi = new Mock<IPlayniteAPI>();
        mockApi.Setup(a => a.Database).Returns(mockDb.Object);

        PlayniteIntegration integration = new PlayniteIntegration(mockApi.Object);

        integration.MarkInstalled(Guid.NewGuid().ToString(), @"C:\Games\Game", null);

        mockDb.Verify(db => db.Games.Update(It.IsAny<Game>()), Times.Never);
    }
}
