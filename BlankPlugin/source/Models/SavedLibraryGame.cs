using System;

namespace BlankPlugin
{
    /// <summary>
    /// User-saved library bookmark (Search → Add to library). Persisted to library_games.json.
    /// Whether the title is installed comes from <see cref="InstalledGamesManager"/> + disk, not this file.
    /// </summary>
    public class SavedLibraryGame
    {
        public string AppId { get; set; }
        public string GameName { get; set; }
        public DateTime AddedDate { get; set; }
    }
}
