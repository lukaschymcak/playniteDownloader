using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BlankPlugin
{
    public class GoldbergRunner
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string _goldbergRoot;
        private readonly string _genEmuConfigDir;
        private readonly string _genEmuConfigExe;

        public GoldbergRunner(string goldbergFilesPath)
        {
            _goldbergRoot     = goldbergFilesPath;
            _genEmuConfigDir  = Path.Combine(goldbergFilesPath, "generate_emu_config");
            _genEmuConfigExe  = Path.Combine(_genEmuConfigDir, "generate_emu_config.exe");
        }

        public bool IsReady => File.Exists(_genEmuConfigExe);

        /// <summary>
        /// Returns "x64", "x32", or null (both/neither — caller must ask user).
        /// Searches recursively for steam_api*.dll anywhere under gameDir.
        /// </summary>
        public static string DetectArch(string gameDir)
        {
            bool has64 = Directory.GetFiles(gameDir, "steam_api64.dll", SearchOption.AllDirectories).Length > 0;
            bool has32 = Directory.GetFiles(gameDir, "steam_api.dll",   SearchOption.AllDirectories).Length > 0;
            if (has64 && !has32) return "x64";
            if (has32 && !has64) return "x32";
            return null;
        }

        /// <summary>
        /// Returns all directories (under gameDir) that contain any of the steam DLL names.
        /// Used to know where to place Goldberg DLLs after backup.
        /// </summary>
        private static List<string> FindDllDirectories(string gameDir)
        {
            var dllNames = new[] { "steam_api.dll", "steam_api64.dll", "steamclient.dll", "steamclient64.dll" };
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in dllNames)
                foreach (var path in Directory.GetFiles(gameDir, name, SearchOption.AllDirectories))
                    dirs.Add(Path.GetDirectoryName(path));
            return new List<string>(dirs);
        }

        /// <summary>
        /// Runs Goldberg setup. Returns the absolute <c>_OUTPUT/{appId}</c> directory, or null if the tool is not ready.
        /// </summary>
        public string Run(string gameDir, string appId, GoldbergOptions options, BlankPluginSettings settings, Action<string> onLog, bool gseSavesCopied = false)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (!IsReady)
            {
                onLog("ERROR: generate_emu_config.exe not found at: " + _genEmuConfigExe);
                return null;
            }

            var arch = options.Arch;
            onLog("=== Goldberg Emulator Setup ===");
            onLog("Game dir: " + gameDir);
            onLog("AppId:    " + appId);
            onLog("Arch:     " + (arch ?? "auto"));
            onLog("Mode:     " + options.Mode);
            onLog("Copy:     " + (options.CopyFiles ? "yes (into game dir)" : "no (output path only)"));

            // Step 1 — Patch configs.user.ini
            if (!string.IsNullOrWhiteSpace(settings.GoldbergAccountName) ||
                !string.IsNullOrWhiteSpace(settings.GoldbergSteamId))
            {
                onLog("--- Step 1: Patching configs.user.ini ---");
                PatchUserConfig(
                    Path.Combine(_genEmuConfigDir, "_DEFAULT", "1", "steam_settings", "configs.user.ini"),
                    settings.GoldbergAccountName,
                    settings.GoldbergSteamId,
                    onLog);
                PatchUserConfig(
                    Path.Combine(_goldbergRoot, "0. Files to put into GSE Saves folder", "configs.user.ini"),
                    settings.GoldbergAccountName,
                    settings.GoldbergSteamId,
                    onLog);
            }
            else
            {
                onLog("--- Step 1: Skipping configs.user.ini (no account set) ---");
            }

            // Step 2 — Copy GSE Saves folder files (first run only)
            if (!gseSavesCopied)
            {
                onLog("--- Step 2: Copying GSE Saves folder (first run) ---");
                CopyGseSavesFolder(onLog);
            }
            else
            {
                onLog("--- Step 2: Skipping GSE Saves (already copied) ---");
            }

            // Step 3 — Copy my_login.txt
            onLog("--- Step 3: Copying my_login.txt ---");
            var loginSrc  = Path.Combine(_goldbergRoot, "my_login.txt");
            var loginDest = Path.Combine(_genEmuConfigDir, "my_login.txt");
            if (File.Exists(loginSrc))
            {
                File.Copy(loginSrc, loginDest, overwrite: true);
                onLog("Copied my_login.txt");
            }
            else
            {
                onLog("WARNING: my_login.txt not found, skipping.");
            }

            // Step 4 — Run generate_emu_config.exe
            onLog("--- Step 4: Running generate_emu_config -acw " + appId + " ---");
            RunGenerateEmuConfig(appId, onLog);

            var outputDir = Path.GetFullPath(Path.Combine(_genEmuConfigDir, "_OUTPUT", appId.Trim()));
            if (!options.CopyFiles)
            {
                onLog("Skipping file copy (user chose output only). Generated files under: " + outputDir);
                onLog("=== Goldberg setup complete (no copy) ===");
                return outputDir;
            }

            var dllDirs = FindDllDirectories(gameDir);
            if (dllDirs.Count == 0)
            {
                onLog("No steam DLLs found in game dir — will copy to game root.");
                dllDirs.Add(gameDir);
            }

            if (options.Mode == GoldbergRunMode.Full)
            {
                onLog("--- Step 5: Backing up original DLLs ---");
                BackupDlls(dllDirs, onLog);
                onLog("--- Step 6: Copying Goldberg files to game dir ---");
                CopyOutput(appId, gameDir, dllDirs, copyDlls: true, onLog);
            }
            else
            {
                onLog("--- Step 5: Skipping DLL backup (achievements-only mode) ---");
                onLog("--- Step 6: Copying steam_settings only ---");
                CopyOutput(appId, gameDir, dllDirs, copyDlls: false, onLog);
            }

            onLog("=== Goldberg setup complete ===");
            return outputDir;
        }

        private void PatchUserConfig(string path, string accountName, string steamId, Action<string> onLog)
        {
            if (!File.Exists(path))
            {
                onLog("WARNING: configs.user.ini not found: " + path);
                return;
            }

            var lines  = File.ReadAllLines(path);
            var output = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (!string.IsNullOrWhiteSpace(accountName) &&
                    trimmed.StartsWith("account_name=", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine("account_name=" + accountName);
                }
                else if (!string.IsNullOrWhiteSpace(steamId) &&
                         trimmed.StartsWith("account_steamid=", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine("account_steamid=" + steamId);
                }
                else
                {
                    output.AppendLine(line);
                }
            }

            File.WriteAllText(path, output.ToString(), Encoding.UTF8);
            onLog("Patched: " + Path.GetFileName(path));
        }

        private void CopyGseSavesFolder(Action<string> onLog)
        {
            var src  = Path.Combine(_goldbergRoot, "0. Files to put into GSE Saves folder");
            var dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GSE Saves");

            if (!Directory.Exists(src))
            {
                onLog("WARNING: GSE Saves source folder not found, skipping.");
                return;
            }

            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(src))
            {
                var destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
                onLog("GSE Saves: copied " + Path.GetFileName(file));
            }
        }

        private void RunGenerateEmuConfig(string appId, Action<string> onLog)
        {
            if (string.IsNullOrWhiteSpace(appId) || !uint.TryParse(appId.Trim(), out _))
            {
                onLog("ERROR: Invalid Steam AppId for generate_emu_config.");
                return;
            }

            var safeId = appId.Trim();
            var psi = new ProcessStartInfo(_genEmuConfigExe, "-acw \"" + safeId + "\"")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = _genEmuConfigDir
            };

            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    onLog(line);
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(stderr))
                    onLog("[stderr] " + stderr.Trim());
                if (proc.ExitCode != 0)
                    onLog("WARNING: generate_emu_config exited with code " + proc.ExitCode);
            }
        }

        private static void BackupDlls(List<string> dllDirs, Action<string> onLog)
        {
            var dlls = new[] { "steam_api.dll", "steam_api64.dll", "steamclient.dll", "steamclient64.dll" };
            foreach (var dir in dllDirs)
            {
                foreach (var dll in dlls)
                {
                    var src = Path.Combine(dir, dll);
                    var bak = src + ".BAK";
                    if (File.Exists(src) && !File.Exists(bak))
                    {
                        File.Move(src, bak);
                        onLog("Backed up: " + Path.Combine(dir, dll));
                    }
                }
            }
        }

        private void CopyOutput(string appId, string gameDir, List<string> dllDirs, bool copyDlls, Action<string> onLog)
        {
            var outputDir = Path.Combine(_genEmuConfigDir, "_OUTPUT", appId);
            if (!Directory.Exists(outputDir))
            {
                onLog("WARNING: _OUTPUT folder not found for appId " + appId + ". generate_emu_config may have failed.");
                return;
            }

            if (copyDlls)
            {
                var dllNames = new[] { "steam_api.dll", "steam_api64.dll", "steamclient.dll", "steamclient64.dll" };
                foreach (var dll in dllNames)
                {
                    var src = Path.Combine(outputDir, dll);
                    if (!File.Exists(src)) continue;
                    foreach (var dir in dllDirs)
                    {
                        File.Copy(src, Path.Combine(dir, dll), overwrite: true);
                        onLog("Copied: " + dll + " → " + dir);
                    }
                }
            }

            // Copy steam_settings/ to each DLL directory
            var settingsSrc = Path.Combine(outputDir, "steam_settings");
            if (Directory.Exists(settingsSrc))
            {
                foreach (var dir in dllDirs)
                {
                    var settingsDest = Path.Combine(dir, "steam_settings");
                    CopyDirectory(settingsSrc, settingsDest, onLog);
                    onLog("Copied: steam_settings/ \u2192 " + dir);
                }
            }
            else
            {
                onLog("WARNING: steam_settings folder not found in output.");
            }
        }

        private static void CopyDirectory(string src, string dest, Action<string> onLog)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), onLog);
        }
    }
}
