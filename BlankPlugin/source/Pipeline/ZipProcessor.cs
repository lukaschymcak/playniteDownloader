using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BlankPlugin
{
    /// <summary>
    /// Parses the Morrenus manifest ZIP.
    ///
    /// ZIP structure:
    ///   - One .lua file containing addappid / addtoken / setManifestid calls
    ///   - One or more .manifest binary files named {depotId}_{manifestGid}.manifest
    ///
    /// Mirrors ACCELA's ProcessZipTask._parse_lua logic.
    /// </summary>
    public class ZipProcessor
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // Depot descriptions blacklisted by content keyword
        private static readonly HashSet<string> DescBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "soundtrack", "ost", "original soundtrack", "artbook",
            "graphic novel", "demo", "server", "dedicated server",
            "tool", "sdk", "3d print model"
        };

        private static readonly string ManifestTempDir =
            Path.Combine(Path.GetTempPath(), "blankplugin_manifests");

        public GameData Process(string zipPath)
        {
            logger.Info("Processing ZIP: " + zipPath);
            Directory.CreateDirectory(ManifestTempDir);

            var gameData = new GameData();

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                // ── Find and read the LUA file ──────────────────────────────────
                ZipArchiveEntry luaEntry = null;
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    {
                        luaEntry = entry;
                        break;
                    }
                }

                if (luaEntry == null)
                    throw new InvalidDataException("No .lua file found in the manifest ZIP.");

                string lua;
                using (var reader = new StreamReader(luaEntry.Open()))
                    lua = reader.ReadToEnd();

                // ── Extract manifest files to temp dir ──────────────────────────
                foreach (var entry in zip.Entries)
                {
                    if (!entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destPath = Path.Combine(ManifestTempDir, entry.Name);
                    entry.ExtractToFile(destPath, overwrite: true);
                    logger.Info("Extracted manifest: " + entry.Name);

                    // Parse depotId_manifestGid.manifest filename
                    var parts = Path.GetFileNameWithoutExtension(entry.Name).Split('_');
                    if (parts.Length == 2)
                        gameData.Manifests[parts[0]] = parts[1];
                }

                // ── Parse LUA ───────────────────────────────────────────────────
                ParseLua(lua, gameData);
            }

            if (string.IsNullOrEmpty(gameData.GameName))
                gameData.GameName = "App_" + gameData.AppId;

            logger.Info("ZIP processed. AppID=" + gameData.AppId
                + " Game=" + gameData.GameName
                + " Depots=" + gameData.Depots.Count);

            return gameData;
        }

        // ── LUA parsing ─────────────────────────────────────────────────────────

        private static void ParseLua(string lua, GameData data)
        {
            // addappid(<id>, <args...>) -- optional comment
            var addAppRegex = new Regex(
                @"addappid\((.*?)\)(.*)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // setManifestid(<depotId>, "<manifestId>", <size>)
            var manifestSizeRegex = new Regex(
                @"setManifestid\(\s*(\d+)\s*,\s*"".*?""\s*,\s*(\d+)\s*\)",
                RegexOptions.IgnoreCase);

            // addtoken(<appId>, "<token>")
            var tokenRegex = new Regex(
                @"addtoken\s*\(\s*\d+\s*,\s*""([^""]+)""\s*\)",
                RegexOptions.IgnoreCase);

            var allMatches = addAppRegex.Matches(lua);
            if (allMatches.Count == 0)
                throw new InvalidDataException("LUA file has no addappid entries.");

            // First addappid is the main app
            var firstArgs = allMatches[0].Groups[1].Value.Split(',');
            data.AppId = firstArgs[0].Trim();

            var nameMatch = Regex.Match(allMatches[0].Groups[2].Value, @"--\s*(.*)");
            if (nameMatch.Success)
                data.GameName = nameMatch.Groups[1].Value.Trim();

            // Remaining addappid entries are depots or DLCs
            for (int i = 1; i < allMatches.Count; i++)
            {
                var argsRaw = allMatches[i].Groups[1].Value;
                var args = argsRaw.Split(',');
                var id = args[0].Trim();

                var descMatch = Regex.Match(allMatches[i].Groups[2].Value, @"--\s*(.*)");
                var desc = descMatch.Success ? descMatch.Groups[1].Value.Trim() : "Depot " + id;

                // Has a key at args[2] → it's a downloadable depot
                var hasKey = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2].Trim().Trim('"'));
                if (hasKey)
                {
                    var key = args[2].Trim().Trim('"');
                    if (!IsBlacklisted(desc))
                    {
                        data.Depots[id] = new DepotInfo { Key = key, Description = desc };
                    }
                    else
                    {
                        logger.Info("Blacklisted depot skipped: " + id + " (" + desc + ")");
                    }
                }
                else
                {
                    data.Dlcs[id] = desc;
                }
            }

            // Manifest sizes from setManifestid
            foreach (Match m in manifestSizeRegex.Matches(lua))
            {
                var depotId = m.Groups[1].Value.Trim();
                if (long.TryParse(m.Groups[2].Value.Trim(), out var size) && data.Depots.ContainsKey(depotId))
                    data.Depots[depotId].Size = size;
            }

            // App token
            var tokenMatch = tokenRegex.Match(lua);
            if (tokenMatch.Success)
                data.AppToken = tokenMatch.Groups[1].Value;
        }

        private static bool IsBlacklisted(string desc)
        {
            var lower = desc.ToLowerInvariant();
            foreach (var kw in DescBlacklist)
            {
                if (Regex.IsMatch(lower, @"\b" + Regex.Escape(kw) + @"\b"))
                    return true;
            }
            return false;
        }

        public static string GetManifestFilePath(string depotId, string manifestGid)
            => Path.Combine(ManifestTempDir, depotId + "_" + manifestGid + ".manifest");
    }
}
