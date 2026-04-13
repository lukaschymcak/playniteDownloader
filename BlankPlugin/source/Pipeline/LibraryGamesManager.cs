using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlankPlugin
{
    /// <summary>
    /// Persisted bookmarks for the Library tab (games added from Search without installing).
    /// </summary>
    public class LibraryGamesManager
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string _dataFilePath;
        private List<SavedLibraryGame> _entries;
        private readonly object _lock = new object();

        public LibraryGamesManager(string pluginDataPath)
        {
            var dir = Path.Combine(pluginDataPath, "data");
            Directory.CreateDirectory(dir);
            _dataFilePath = Path.Combine(dir, "library_games.json");
            Load();
        }

        private void Load()
        {
            lock (_lock)
            {
                _entries = new List<SavedLibraryGame>();
                if (!File.Exists(_dataFilePath))
                    return;

                try
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var loaded = JsonConvert.DeserializeObject<List<SavedLibraryGame>>(json);
                    if (loaded != null)
                        _entries = loaded;
                }
                catch (Exception ex)
                {
                    logger.Warn("Could not load library games: " + ex.Message);
                }
            }
        }

        private void SaveToDisk()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                logger.Warn("Could not save library games: " + ex.Message);
            }
        }

        /// <summary>Adds or updates by AppId. Install state is not stored here.</summary>
        public void AddOrUpdate(string appId, string gameName)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            var name = string.IsNullOrWhiteSpace(gameName) ? appId : gameName.Trim();

            lock (_lock)
            {
                var existing = _entries.FirstOrDefault(e => e.AppId == appId);
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(name) && existing.GameName != name)
                        existing.GameName = name;
                }
                else
                {
                    _entries.Add(new SavedLibraryGame
                    {
                        AppId      = appId.Trim(),
                        GameName   = name,
                        AddedDate  = DateTime.UtcNow
                    });
                }

                SaveToDisk();
            }
        }

        public bool Contains(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return false;

            lock (_lock)
            {
                return _entries.Any(e => e.AppId == appId);
            }
        }

        public void Remove(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            lock (_lock)
            {
                if (_entries.RemoveAll(e => e.AppId == appId) > 0)
                    SaveToDisk();
            }
        }

        public List<SavedLibraryGame> GetAll()
        {
            lock (_lock)
            {
                return new List<SavedLibraryGame>(_entries);
            }
        }
    }
}
