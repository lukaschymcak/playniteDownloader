using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
        /// Re-reads the JSON file from disk, replacing the in-memory cache.
        /// Call before any operation that needs the freshest data (e.g. update checks).
        /// </summary>
        public void Reload() => Load();

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

        public ReconcileResult ReconcileWithSteamLibraries(IEnumerable<SavedLibraryGame> bookmarks, IEnumerable<Game> playniteGames)
        {
            lock (_lock)
            {
                var result = new ReconcileResult();
                var candidates = new HashSet<string>(StringComparer.Ordinal);

                foreach (var g in _games)
                {
                    if (!string.IsNullOrWhiteSpace(g.AppId))
                        candidates.Add(g.AppId.Trim());
                }

                if (bookmarks != null)
                {
                    foreach (var b in bookmarks)
                    {
                        if (!string.IsNullOrWhiteSpace(b?.AppId))
                            candidates.Add(b.AppId.Trim());
                    }
                }

                var steamPluginGuid = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");
                if (playniteGames != null)
                {
                    foreach (var pg in playniteGames)
                    {
                        if (pg == null || pg.PluginId != steamPluginGuid || string.IsNullOrWhiteSpace(pg.GameId))
                            continue;
                        candidates.Add(pg.GameId.Trim());
                    }
                }

                var steamByAppId = DiscoverSteamInstalls();
                var changed = false;

                foreach (var appId in candidates)
                {
                    if (!steamByAppId.TryGetValue(appId, out var discovered))
                        continue;

                    var existing = _games.FirstOrDefault(g => string.Equals(g.AppId, appId, StringComparison.Ordinal));
                    if (existing == null)
                    {
                        _games.Add(discovered);
                        result.Added++;
                        changed = true;
                        continue;
                    }

                    var updated = false;
                    if (!Directory.Exists(existing.InstallPath) || !string.Equals(existing.InstallPath, discovered.InstallPath, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.InstallPath = discovered.InstallPath;
                        updated = true;
                    }
                    if (string.IsNullOrWhiteSpace(existing.LibraryPath) || !string.Equals(existing.LibraryPath, discovered.LibraryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.LibraryPath = discovered.LibraryPath;
                        updated = true;
                    }
                    if (string.IsNullOrWhiteSpace(existing.InstallDir) || !string.Equals(existing.InstallDir, discovered.InstallDir, StringComparison.Ordinal))
                    {
                        existing.InstallDir = discovered.InstallDir;
                        updated = true;
                    }
                    if (string.IsNullOrWhiteSpace(existing.GameName) && !string.IsNullOrWhiteSpace(discovered.GameName))
                    {
                        existing.GameName = discovered.GameName;
                        updated = true;
                    }
                    if (existing.InstalledDate == default(DateTime))
                    {
                        existing.InstalledDate = discovered.InstalledDate;
                        updated = true;
                    }

                    if (updated)
                    {
                        result.Updated++;
                        changed = true;
                    }
                }

                var removed = new List<InstalledGame>();
                foreach (var game in _games.ToList())
                {
                    if (!Directory.Exists(game.InstallPath))
                    {
                        _games.Remove(game);
                        removed.Add(game);
                    }
                }
                if (removed.Count > 0)
                {
                    result.Removed = removed.Count;
                    changed = true;
                }

                if (changed)
                    SaveToDisk();

                return result;
            }
        }

        private static Dictionary<string, InstalledGame> DiscoverSteamInstalls()
        {
            var result = new Dictionary<string, InstalledGame>(StringComparer.Ordinal);
            var libs = SteamLibraryHelper.GetSteamLibraries();

            foreach (var lib in libs)
            {
                try
                {
                    var steamapps = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(steamapps))
                        continue;

                    foreach (var acf in Directory.GetFiles(steamapps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                    {
                        var match = Regex.Match(Path.GetFileName(acf) ?? "", @"^appmanifest_(\d+)\.acf$", RegexOptions.IgnoreCase);
                        if (!match.Success)
                            continue;
                        var appId = match.Groups[1].Value;

                        var content = File.ReadAllText(acf);
                        var installDir = ExtractAcfValue(content, "installdir");
                        if (string.IsNullOrWhiteSpace(installDir))
                            installDir = "App_" + appId;

                        var name = ExtractAcfValue(content, "name");
                        if (string.IsNullOrWhiteSpace(name))
                            name = "App_" + appId;

                        var installPath = Path.Combine(steamapps, "common", installDir);
                        if (!Directory.Exists(installPath))
                            continue;

                        long size = 0;
                        try
                        {
                            foreach (var f in Directory.GetFiles(installPath, "*", SearchOption.AllDirectories))
                            {
                                try { size += new FileInfo(f).Length; } catch { }
                            }
                        }
                        catch { }

                        if (!result.ContainsKey(appId))
                        {
                            result[appId] = new InstalledGame
                            {
                                AppId = appId,
                                GameName = name,
                                InstallDir = installDir,
                                InstallPath = installPath,
                                LibraryPath = lib,
                                InstalledDate = DateTime.UtcNow,
                                SizeOnDisk = size
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug("DiscoverSteamInstalls failed for library '" + lib + "': " + ex.Message);
                }
            }

            return result;
        }

        private static string ExtractAcfValue(string content, string key)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(key))
                return null;
            var m = Regex.Match(content, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    public sealed class ReconcileResult
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
    }
}
