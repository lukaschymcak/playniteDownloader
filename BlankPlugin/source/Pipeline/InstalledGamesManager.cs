using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlankPlugin
{
    /// <summary>
    /// Manages the list of games installed through BlankPlugin.
    /// Persists to a single JSON file in the plugin data directory.
    /// </summary>
    public class InstalledGamesManager
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string _dataFilePath;
        private List<InstalledGame> _games;
        private readonly object _lock = new object();

        public InstalledGamesManager(string pluginDataPath)
        {
            var dir = Path.Combine(pluginDataPath, "data");
            Directory.CreateDirectory(dir);
            _dataFilePath = Path.Combine(dir, "installed_games.json");
            Load();
        }

        private void Load()
        {
            lock (_lock)
            {
                _games = new List<InstalledGame>();
                if (!File.Exists(_dataFilePath))
                    return;

                try
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var loaded = JsonConvert.DeserializeObject<List<InstalledGame>>(json);
                    if (loaded != null)
                        _games = loaded;
                }
                catch (Exception ex)
                {
                    logger.Warn("Could not load installed games: " + ex.Message);
                }
            }
        }

        private void SaveToDisk()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_games, Formatting.Indented);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                logger.Warn("Could not save installed games: " + ex.Message);
            }
        }

        /// <summary>
        /// Adds or updates an installed game entry and persists to disk.
        /// </summary>
        public void Save(InstalledGame game)
        {
            lock (_lock)
            {
                var existing = _games.FirstOrDefault(g => g.AppId == game.AppId);
                if (existing != null)
                    _games.Remove(existing);
                _games.Add(game);
                SaveToDisk();
            }
        }

        /// <summary>
        /// Removes an installed game entry by AppId.
        /// </summary>
        public void Remove(string appId)
        {
            lock (_lock)
            {
                _games.RemoveAll(g => g.AppId == appId);
                SaveToDisk();
            }
        }

        /// <summary>
        /// Returns all installed games.
        /// </summary>
        public List<InstalledGame> GetAll()
        {
            lock (_lock)
            {
                return new List<InstalledGame>(_games);
            }
        }

        /// <summary>
        /// Finds an installed game by Steam AppId. Returns null if not found.
        /// </summary>
        public InstalledGame FindByAppId(string appId)
        {
            lock (_lock)
            {
                return _games.FirstOrDefault(g => g.AppId == appId);
            }
        }

        /// <summary>
        /// Finds an installed game by Playnite game GUID. Returns null if not found.
        /// </summary>
        public InstalledGame FindByPlayniteId(Guid playniteGameId)
        {
            lock (_lock)
            {
                return _games.FirstOrDefault(g => g.PlayniteGameId == playniteGameId);
            }
        }

        /// <summary>
        /// Checks which installed games still exist on disk.
        /// Removes entries where the install path no longer exists.
        /// Returns the cleaned list.
        /// </summary>
        public List<InstalledGame> ScanLibrary()
        {
            lock (_lock)
            {
                var removed = new List<InstalledGame>();
                foreach (var game in _games.ToList())
                {
                    if (!Directory.Exists(game.InstallPath))
                    {
                        logger.Info("Removing stale entry: " + game.GameName + " (path gone)");
                        _games.Remove(game);
                        removed.Add(game);
                    }
                }

                if (removed.Count > 0)
                    SaveToDisk();

                return new List<InstalledGame>(_games);
            }
        }
    }
}
