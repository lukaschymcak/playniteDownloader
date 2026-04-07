using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;

namespace BlankPlugin
{
    public class UpdateState
    {
        public string AppId { get; set; }
        public string GameName { get; set; }
        public string BuildId { get; set; }
        // depotId -> manifestGid (what was downloaded)
        public Dictionary<string, string> Manifests { get; set; }
    }

    public static class UpdateStateManager
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static string StateDir(string pluginDataPath)
            => Path.Combine(pluginDataPath, "state");

        private static string StateFile(string pluginDataPath, string appId)
            => Path.Combine(StateDir(pluginDataPath), appId + ".json");

        public static void SaveState(GameData data, string pluginDataPath)
        {
            try
            {
                Directory.CreateDirectory(StateDir(pluginDataPath));
                var state = new UpdateState
                {
                    AppId = data.AppId,
                    GameName = data.GameName,
                    BuildId = data.BuildId,
                    Manifests = new Dictionary<string, string>(data.Manifests)
                };
                File.WriteAllText(StateFile(pluginDataPath, data.AppId),
                    JsonConvert.SerializeObject(state, Formatting.Indented));
                logger.Info("Saved download state for AppID " + data.AppId);
            }
            catch (Exception ex)
            {
                logger.Warn("Could not save update state: " + ex.Message);
            }
        }

        public static UpdateState LoadState(string appId, string pluginDataPath)
        {
            var path = StateFile(pluginDataPath, appId);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<UpdateState>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                logger.Warn("Could not read update state: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Fetches the current manifest from Morrenus and compares against saved state.
        /// Returns "up_to_date", "update_available", or "cannot_determine".
        /// </summary>
        public static string CheckUpdate(
            string appId,
            string pluginDataPath,
            MorrenusClient client,
            Action<string> onLog,
            IProgress<int> progress = null)
        {
            var saved = LoadState(appId, pluginDataPath);
            if (saved == null || saved.Manifests == null || saved.Manifests.Count == 0)
            {
                onLog("No saved state for AppID " + appId + " — download the game first.");
                return "cannot_determine";
            }

            onLog("Fetching latest manifest from Morrenus...");
            string zipPath;
            try
            {
                zipPath = client.DownloadManifest(appId, progress);
            }
            catch (Exception ex)
            {
                onLog("ERROR fetching manifest: " + ex.Message);
                return "cannot_determine";
            }

            GameData fresh;
            try
            {
                fresh = new ZipProcessor().Process(zipPath);
            }
            catch (Exception ex)
            {
                onLog("ERROR parsing manifest ZIP: " + ex.Message);
                return "cannot_determine";
            }

            var changed = new List<string>();
            var unchanged = 0;

            foreach (var kv in saved.Manifests)
            {
                var depotId = kv.Key;
                var savedManifest = kv.Value;

                if (fresh.Manifests.TryGetValue(depotId, out var currentManifest))
                {
                    if (savedManifest != currentManifest)
                        changed.Add(string.Format("  Depot {0}: {1} → {2}", depotId, savedManifest, currentManifest));
                    else
                        unchanged++;
                }
            }

            if (changed.Count > 0)
            {
                onLog("UPDATE AVAILABLE — " + changed.Count + " depot(s) changed:");
                foreach (var line in changed) onLog(line);
                return "update_available";
            }
            else
            {
                onLog("Up to date. (" + unchanged + " depot(s) unchanged)");
                return "up_to_date";
            }
        }
    }
}
