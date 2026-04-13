using System;
using System.Collections.Generic;

namespace BlankPlugin
{
    /// <summary>
    /// Represents a game installed through BlankPlugin.
    /// Persisted to installed_games.json by InstalledGamesManager.
    /// </summary>
    public class InstalledGame
    {
        public string AppId { get; set; }
        public string GameName { get; set; }
        public string InstallPath { get; set; }
        public string LibraryPath { get; set; }
        public string InstallDir { get; set; }
        public DateTime InstalledDate { get; set; }
        public long SizeOnDisk { get; set; }

        /// <summary>Depot IDs that were selected for download.</summary>
        public List<string> SelectedDepots { get; set; } = new List<string>();

        /// <summary>Depot ID → manifest GID (for update checking).</summary>
        public Dictionary<string, string> ManifestGIDs { get; set; } = new Dictionary<string, string>();

        /// <summary>Playnite game GUID to link back to the library entry.</summary>
        public Guid PlayniteGameId { get; set; }

        /// <summary>Whether Steamless DRM removal was applied.</summary>
        public bool DrmStripped { get; set; }

        /// <summary>Whether the game was registered with Steam via ACF manifest.</summary>
        public bool RegisteredWithSteam { get; set; }

        /// <summary>Whether Goldberg GSE Saves files have been copied for this game.</summary>
        public bool GseSavesCopied { get; set; }
    }
}
