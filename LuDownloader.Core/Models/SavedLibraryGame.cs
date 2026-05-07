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

        /// <summary>Optional; set when adding from Search (Steam <c>header_image</c> or store fallback). Library prefers this over guessed CDN URLs.</summary>
        public string HeaderImageUrl { get; set; }
    }
}
