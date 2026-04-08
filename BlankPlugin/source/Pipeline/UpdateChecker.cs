using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlankPlugin
{
    /// <summary>
    /// Orchestrates update checking for all plugin-installed games.
    /// Runs ManifestChecker.exe to fetch current Steam manifest GIDs,
    /// compares them against saved GIDs in InstalledGame records,
    /// and fires status change events for the UI.
    /// </summary>
    public class UpdateChecker
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly ManifestCheckerRunner _runner;
        private readonly InstalledGamesManager _gamesManager;
        private readonly IPlayniteAPI _playniteApi;

        // In-memory cache: AppId -> status string
        // Status values: "up_to_date" | "update_available" | "cannot_determine" | "checking"
        private readonly Dictionary<string, string> _statusCache
            = new Dictionary<string, string>();

        private readonly SemaphoreSlim _runLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        public UpdateChecker(
            ManifestCheckerRunner runner,
            InstalledGamesManager gamesManager,
            IPlayniteAPI playniteApi)
        {
            _runner = runner;
            _gamesManager = gamesManager;
            _playniteApi = playniteApi;
        }

        public string GetStatus(string appId)
        {
            return _statusCache.TryGetValue(appId, out var status) ? status : null;
        }

        public Task RunAsync()
        {
            // If already running, drop this request silently
            if (!_runLock.Wait(0))
            {
                logger.Info("UpdateChecker.RunAsync: check already in progress, skipping.");
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            return Task.Run(() =>
            {
                try
                {
                    _gamesManager.Reload();
                    var games = _gamesManager.GetAll();

                    // Only check games that have saved manifest GIDs (i.e. were installed with this plugin version)
                    var checkable = games
                        .Where(g => g.ManifestGIDs != null && g.ManifestGIDs.Count > 0)
                        .ToList();

                    if (checkable.Count == 0)
                    {
                        logger.Info("UpdateChecker: no games with saved manifest GIDs to check.");
                        return;
                    }

                    logger.Info("UpdateChecker: checking " + checkable.Count + " game(s).");

                    // Mark all as "checking" so the UI can show the badge immediately
                    foreach (var game in checkable)
                    {
                        _statusCache[game.AppId] = "checking";
                    }

                    if (token.IsCancellationRequested) return;

                    // Run ManifestChecker.exe with all AppIDs
                    var appIds = checkable.Select(g => g.AppId).ToList();
                    var (results, error) = _runner.Run(appIds);

                    if (token.IsCancellationRequested) return;

                    if (results == null)
                    {
                        logger.Warn("UpdateChecker: ManifestCheckerRunner failed: " + error);

                        _playniteApi.Notifications.Add(new NotificationMessage(
                            "blankplugin_check_failed_" + DateTime.Now.Ticks,
                            "BlankPlugin: Update check failed — " + error,
                            NotificationType.Error));

                        // Mark all as cannot_determine
                        foreach (var game in checkable)
                        {
                            _statusCache[game.AppId] = "cannot_determine";
                        }
                        return;
                    }

                    // Group results by AppId for easy lookup: appId -> list of (depotId, manifestGid)
                    var resultsByApp = results
                        .GroupBy(r => r.AppId)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    var updatesAvailable = new List<string>();

                    foreach (var game in checkable)
                    {
                        if (token.IsCancellationRequested) break;

                        string status;

                        if (!resultsByApp.TryGetValue(game.AppId, out var steamDepots) || steamDepots.Count == 0)
                        {
                            status = "cannot_determine";
                        }
                        else
                        {
                            // Compare each saved depot manifest GID against the Steam result
                            bool anyChanged = false;
                            bool anyFound = false;

                            foreach (var kv in game.ManifestGIDs)
                            {
                                var savedDepotId = kv.Key;
                                var savedGid = kv.Value;

                                var steamDepot = steamDepots.FirstOrDefault(d => d.DepotId == savedDepotId);
                                if (steamDepot == null) continue;

                                anyFound = true;
                                if (steamDepot.ManifestGid != savedGid)
                                {
                                    anyChanged = true;
                                    logger.Info(string.Format(
                                        "Update detected for {0} depot {1}: saved={2} steam={3}",
                                        game.GameName, savedDepotId, savedGid, steamDepot.ManifestGid));
                                    break;
                                }
                            }

                            if (!anyFound)
                                status = "cannot_determine";
                            else if (anyChanged)
                                status = "update_available";
                            else
                                status = "up_to_date";
                        }

                        _statusCache[game.AppId] = status;

                        if (status == "update_available")
                            updatesAvailable.Add(game.GameName);
                    }

                    // Push a single grouped Playnite notification if any updates were found
                    if (updatesAvailable.Count > 0 && !token.IsCancellationRequested)
                    {
                        string message;
                        if (updatesAvailable.Count == 1)
                            message = "Update available for " + updatesAvailable[0];
                        else
                            message = "Updates available for " + updatesAvailable.Count + " games: "
                                      + string.Join(", ", updatesAvailable);

                        _playniteApi.Notifications.Add(new NotificationMessage(
                            "blankplugin_updates_" + DateTime.Now.Ticks,
                            message,
                            NotificationType.Info));
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("UpdateChecker.RunAsync failed: " + ex.Message);
                }
                finally
                {
                    _runLock.Release();
                }
            }, token);
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        /// <summary>
        /// Marks a game as up-to-date in the in-memory status cache.
        /// Called by UpdateWindow after a successful update download.
        /// </summary>
        public void MarkUpToDate(string appId)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                _statusCache[appId] = "up_to_date";
                logger.Info("UpdateChecker: Marked " + appId + " as up_to_date after successful update.");
            }
        }

    }
}
