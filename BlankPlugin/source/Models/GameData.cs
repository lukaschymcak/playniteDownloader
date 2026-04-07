using System.Collections.Generic;

namespace BlankPlugin
{
    /// <summary>
    /// Holds all data parsed from the Morrenus manifest ZIP for a single game.
    /// Populated by ZipProcessor, consumed by DepotDownloader and post-processors.
    /// </summary>
    public class GameData
    {
        public string AppId { get; set; }
        public string GameName { get; set; }
        public string InstallDir { get; set; }
        public string BuildId { get; set; }
        public string HeaderUrl { get; set; }
        public string AppToken { get; set; }

        /// <summary>depot id → DepotInfo (key, desc, oslist, size)</summary>
        public Dictionary<string, DepotInfo> Depots { get; set; } = new Dictionary<string, DepotInfo>();

        /// <summary>depot id → manifest gid string</summary>
        public Dictionary<string, string> Manifests { get; set; } = new Dictionary<string, string>();

        /// <summary>dlc id → description (no key, not downloadable)</summary>
        public Dictionary<string, string> Dlcs { get; set; } = new Dictionary<string, string>();

        /// <summary>Set after user selects depots in the UI.</summary>
        public List<string> SelectedDepots { get; set; } = new List<string>();
    }

    public class DepotInfo
    {
        public string Key { get; set; }
        public string Description { get; set; }
        public string OsList { get; set; }
        public string Language { get; set; }
        public long Size { get; set; }
    }
}
