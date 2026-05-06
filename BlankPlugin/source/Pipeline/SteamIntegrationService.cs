using Playnite.SDK;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace BlankPlugin
{
    /// <summary>
    /// Shared helper for Steam integration flows (Lua copy, restart, ACF write).
    /// </summary>
    public sealed class SteamIntegrationService
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public string GetSteamPath()
        {
            return SteamLibraryHelper.GetSteamInstallPath();
        }

        public string GetLuaTargetDir(string steamPath)
        {
            if (string.IsNullOrWhiteSpace(steamPath))
                return null;
            return Path.Combine(steamPath, "config", "lua");
        }

        public string ResolveManifestZip(string appId, string lastManifestZipPath, string manifestCacheRoot)
        {
            if (!string.IsNullOrWhiteSpace(lastManifestZipPath) && File.Exists(lastManifestZipPath))
                return lastManifestZipPath;

            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(manifestCacheRoot))
                return null;

            if (ManifestCache.TryGetCachedZipPath(manifestCacheRoot, appId.Trim(), out var cachedZip) && File.Exists(cachedZip))
                return cachedZip;

            return null;
        }

        public string ExtractSingleLua(string manifestZipPath)
        {
            if (string.IsNullOrWhiteSpace(manifestZipPath) || !File.Exists(manifestZipPath))
                throw new FileNotFoundException("Manifest ZIP not found.", manifestZipPath ?? "");

            using (var zip = ZipFile.OpenRead(manifestZipPath))
            {
                ZipArchiveEntry lua = null;
                foreach (var entry in zip.Entries)
                {
                    if (!entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (lua != null)
                        throw new InvalidOperationException("Multiple .lua files found in manifest ZIP.");
                    lua = entry;
                }

                if (lua == null)
                    throw new InvalidOperationException("No .lua file found in manifest ZIP.");

                var tempDir = Path.Combine(Path.GetTempPath(), "blankplugin_lua");
                Directory.CreateDirectory(tempDir);
                var outPath = Path.Combine(tempDir, lua.Name);
                lua.ExtractToFile(outPath, true);
                return outPath;
            }
        }

        public bool IsLuaPresentInSteamConfig(string steamPath, string luaFileName)
        {
            if (string.IsNullOrWhiteSpace(steamPath) || string.IsNullOrWhiteSpace(luaFileName))
                return false;

            var target = Path.Combine(GetLuaTargetDir(steamPath), luaFileName);
            return File.Exists(target);
        }

        public string CopyLuaToSteamConfig(string sourceLuaPath, string steamPath, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(sourceLuaPath) || !File.Exists(sourceLuaPath))
                throw new FileNotFoundException("Lua source file not found.", sourceLuaPath ?? "");
            if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                throw new InvalidOperationException("Steam install path could not be detected.");

            var targetDir = GetLuaTargetDir(steamPath);
            Directory.CreateDirectory(targetDir);
            var target = Path.Combine(targetDir, Path.GetFileName(sourceLuaPath));
            File.Copy(sourceLuaPath, target, overwrite);
            return target;
        }

        public void RestartSteam(string steamPath)
        {
            if (string.IsNullOrWhiteSpace(steamPath))
                throw new InvalidOperationException("Steam path is empty.");

            foreach (var proc in Process.GetProcessesByName("steam"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
                catch (Exception ex)
                {
                    logger.Debug("Steam process kill failed: " + ex.Message);
                }
                finally
                {
                    proc.Dispose();
                }
            }

            var steamExe = Path.Combine(steamPath, "steam.exe");
            if (!File.Exists(steamExe))
                throw new FileNotFoundException("steam.exe was not found.", steamExe);

            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                WorkingDirectory = steamPath,
                UseShellExecute = true
            });
        }

        public string WriteAcfForInstall(GameData data, string steamLibraryRoot, Action<string> onLog)
        {
            if (data == null)
                throw new InvalidOperationException("Missing game data.");
            if (string.IsNullOrWhiteSpace(steamLibraryRoot))
                throw new InvalidOperationException("Steam library root is empty.");
            return AcfWriter.Write(data, steamLibraryRoot, onLog ?? (_ => { }));
        }
    }
}
