using Playnite.SDK;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BlankPlugin
{
    /// <summary>
    /// Writes a Steam appmanifest .acf file so Steam recognises the game as installed.
    /// Optional — only called when the user enables "Register with Steam".
    /// Mirrors ACCELA's steam_manifest.write_acf_file logic.
    /// </summary>
    public static class AcfWriter
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public static string Write(GameData gameData, string steamLibraryPath, Action<string> onLog)
        {
            var installFolderName = GetInstallFolderName(gameData);
            var acfPath = Path.Combine(steamLibraryPath, "steamapps", "appmanifest_" + gameData.AppId + ".acf");
            Directory.CreateDirectory(Path.GetDirectoryName(acfPath));

            var sizeOnDisk = CalculateDirSize(
                Path.Combine(steamLibraryPath, "steamapps", "common", installFolderName));

            var content = BuildAcf(gameData, installFolderName, sizeOnDisk);
            File.WriteAllText(acfPath, content, Encoding.UTF8);

            onLog("Wrote ACF: " + acfPath);
            logger.Info("ACF written: " + acfPath);
            return acfPath;
        }

        private static string BuildAcf(GameData data, string installFolderName, long sizeOnDisk)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\"AppState\"");
            sb.AppendLine("{");
            sb.AppendLine("\t\"appid\"\t\t\"" + data.AppId + "\"");
            sb.AppendLine("\t\"Universe\"\t\t\"1\"");
            sb.AppendLine("\t\"name\"\t\t\"" + data.GameName + "\"");
            sb.AppendLine("\t\"StateFlags\"\t\t\"4\"");
            sb.AppendLine("\t\"installdir\"\t\t\"" + installFolderName + "\"");
            sb.AppendLine("\t\"SizeOnDisk\"\t\t\"" + sizeOnDisk + "\"");
            sb.AppendLine("\t\"buildid\"\t\t\"" + (data.BuildId ?? "0") + "\"");

            // InstalledDepots
            sb.AppendLine("\t\"InstalledDepots\"");
            sb.AppendLine("\t{");
            foreach (var depotId in data.SelectedDepots)
            {
                if (!data.Manifests.TryGetValue(depotId, out var manifestGid)) continue;
                var size = data.Depots.TryGetValue(depotId, out var info) ? info.Size : 0;
                sb.AppendLine("\t\t\"" + depotId + "\"");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\t\"manifest\"\t\t\"" + manifestGid + "\"");
                sb.AppendLine("\t\t\t\"size\"\t\t\"" + size + "\"");
                sb.AppendLine("\t\t}");
            }
            sb.AppendLine("\t}");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public static string GetInstallFolderName(GameData data)
        {
            if (!string.IsNullOrWhiteSpace(data.InstallDir)) return data.InstallDir;
            var safe = Regex.Replace(data.GameName ?? "", @"[^\w\s-]", "").Trim().Replace(" ", "_");
            return string.IsNullOrEmpty(safe) ? "App_" + data.AppId : safe;
        }

        private static long CalculateDirSize(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            long size = 0;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
            return size;
        }
    }
}
