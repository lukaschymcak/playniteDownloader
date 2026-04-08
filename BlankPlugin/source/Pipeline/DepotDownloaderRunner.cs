using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BlankPlugin
{
    /// <summary>
    /// Runs DepotDownloader.dll (via dotnet) for each selected depot.
    /// Modelled after ACCELA's DownloadDepotsTask:
    ///   - Reads output byte-by-byte, treating \r and \n both as line separators
    ///     so every in-place progress update is captured immediately.
    ///   - Applies identical parsing to stdout and stderr (DD may write to either).
    ///   - Computes weighted overall progress across multiple depots using depot sizes.
    ///   - Progress is deduplicated — only emits when the integer value changes.
    /// </summary>
    public class DepotDownloaderRunner
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string _dotnetPath;
        private readonly string _depotDownloaderDll;

        private Process _currentProcess;
        private volatile bool _stopRequested;

        public DepotDownloaderRunner()
        {
            _dotnetPath = FindDotnet();
            _depotDownloaderDll = Path.Combine(GetPluginDir(), "deps", "DepotDownloader.dll");
        }

        private string SteamKit2Dll =>
            Path.Combine(Path.GetDirectoryName(_depotDownloaderDll), "SteamKit2.dll");

        public bool IsReady =>
            !string.IsNullOrEmpty(_dotnetPath) &&
            File.Exists(_depotDownloaderDll) &&
            File.Exists(SteamKit2Dll);

        public string DotnetPath => _dotnetPath;

        public string ComputeDownloadDir(GameData data, string destPath)
            => Path.Combine(destPath, "steamapps", "common", AcfWriter.GetInstallFolderName(data));

        // ── Public entry point ────────────────────────────────────────────────────

        public void Run(
            GameData gameData,
            string destPath,
            Action<string> onLog,
            Action<int> onProgress,
            int maxDownloads = 20,
            Action<string> onStatus = null,
            string steamUsername = null)
        {
            _stopRequested = false;

            if (!IsReady)
            {
                string msg;
                if (string.IsNullOrEmpty(_dotnetPath))
                    msg = "dotnet runtime not found. Install .NET 9 runtime and ensure 'dotnet' is on PATH.";
                else if (!File.Exists(_depotDownloaderDll))
                    msg = "DepotDownloader.dll not found in plugin deps folder.";
                else
                    msg = "DepotDownloader dependencies are missing. Copy the full 'dotnet publish' " +
                          "output (not just DepotDownloader.dll) into the plugin's deps\\ folder.";
                onLog("ERROR: " + msg);
                return;
            }

            // Keys file
            var keysPath = Path.Combine(Path.GetTempPath(), "blankplugin_keys.vdf");
            using (var sw = new StreamWriter(keysPath))
            {
                foreach (var id in gameData.SelectedDepots)
                    if (gameData.Depots.TryGetValue(id, out var info))
                        sw.WriteLine(id + ";" + info.Key);
            }

            var downloadDir = ComputeDownloadDir(gameData, destPath);
            Directory.CreateDirectory(downloadDir);
            onLog("Download destination: " + downloadDir);

            // Weighted progress — mirrors ACCELA's per-depot size tracking
            long totalSize = 0;
            foreach (var id in gameData.SelectedDepots)
                if (gameData.Depots.TryGetValue(id, out var di)) totalSize += di.Size;

            long completedSoFar = 0;
            int totalDepots = gameData.SelectedDepots.Count;

            for (int i = 0; i < totalDepots; i++)
            {
                if (_stopRequested) break;

                var depotId = gameData.SelectedDepots[i];
                if (!gameData.Manifests.TryGetValue(depotId, out var manifestGid))
                {
                    onLog("WARNING: No manifest ID for depot " + depotId + " — skipping.");
                    continue;
                }

                long depotSize = gameData.Depots.TryGetValue(depotId, out var dinfo) ? dinfo.Size : 0L;
                var manifestFile = ZipProcessor.GetManifestFilePath(depotId, manifestGid);

                onLog(string.Format("--- Depot {0}/{1}: {2} ---", i + 1, totalDepots, depotId));

                var args = BuildArgs(gameData.AppId, depotId, manifestGid, manifestFile, keysPath, downloadDir, maxDownloads, steamUsername);
                bool ok = RunDepotProcess(args, totalSize, completedSoFar, depotSize, onLog, onProgress, onStatus);

                if (ok) completedSoFar += depotSize;
            }

            TryDelete(keysPath, onLog);
            onLog("Done.");
        }

        public void Stop()
        {
            _stopRequested = true;
            try { _currentProcess?.Kill(); } catch { }
        }

        /// <summary>
        /// Runs DepotDownloader in a visible console window so the user can complete
        /// Steam authentication (including Steam Guard if required). After this succeeds,
        /// the session token is saved by DD and only -username is needed on future runs.
        /// </summary>
        public void Authenticate(string username, string password, Action<string> onLog)
        {
            if (!IsReady)
            {
                onLog("ERROR: dotnet or DepotDownloader.dll not found.");
                return;
            }

            // Write a .bat so we avoid cmd.exe quoting issues with two quoted paths.
            // The trailing `pause` keeps the window open so the user can read any
            // Steam Guard prompt and type their code before the window closes.
            var batPath = Path.Combine(Path.GetTempPath(), "blankplugin_auth.bat");
            // Auth-only trick: DD must have -app/-depot/-manifest to proceed past arg
            // parsing, but it saves the session token BEFORE looking up any manifest.
            // We pin it to a single depot (228981, part of the public Steamworks
            // redistributables app) and pass manifest GID 0 which does not exist, so DD
            // exits immediately after auth without downloading anything.
            // A throw-away temp dir keeps any stray output off the Steam library.
            var authTempDir = Path.Combine(Path.GetTempPath(), "blankplugin_auth_tmp");
            File.WriteAllText(batPath, string.Format(
                "@echo off\r\n" +
                "echo Authenticating with Steam as {0}...\r\n" +
                "echo If Steam Guard is enabled, enter the code below when prompted.\r\n" +
                "echo.\r\n" +
                "\"{1}\" \"{2}\" -username {0} -password {3} -remember-password -app 228980 -depot 228981 -manifest 0 -dir \"{4}\"\r\n" +
                "echo.\r\n" +
                "echo Done. You can close this window.\r\n" +
                "pause\r\n",
                username, _dotnetPath, _depotDownloaderDll, password, authTempDir));

            onLog("Opening Steam authentication window...");
            onLog("Enter your Steam Guard code in the console if prompted, then close it.");

            var psi = new ProcessStartInfo("cmd.exe", "/C \"" + batPath + "\"")
            {
                UseShellExecute = true,
                CreateNoWindow  = false
            };

            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                proc.WaitForExit(300_000); // up to 5 minutes
            }

            TryDelete(batPath, onLog);
            try { if (Directory.Exists(authTempDir)) Directory.Delete(authTempDir, true); } catch { }
            onLog("Authentication window closed. If successful, future downloads will use your Steam account.");
        }

        // ── Per-depot process ─────────────────────────────────────────────────────

        private bool RunDepotProcess(
            string args,
            long totalSize, long completedSoFar, long depotSize,
            Action<string> onLog, Action<int> onProgress,
            Action<string> onStatus)
        {
            var psi = new ProcessStartInfo(_dotnetPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var pctRegex = new Regex(@"(\d{1,3}(?:\.\d{1,2})?)%");

            // Thread-safe progress dedup: both stdout and stderr threads write here.
            int lastPct = -1;
            var progressLock = new object();

            // CDN timeout counter — DD retries automatically; suppress per-chunk spam.
            int timeoutCount = 0;

            // Pre-allocation counter — DD prints one "Pre-allocating <path>" line per file.
            // For a large game this is thousands of lines that would flood the log and hide
            // all download output. We collapse them into a single summary instead.
            int preAllocCount = 0;

            // Validation phase tracking — if % lines arrive after validation started,
            // DD is re-downloading chunks that failed the checksum check.
            bool validationStarted = false;
            bool redownloadNotified = false;

            // Shared line handler — called from both stdout and stderr threads.
            Action<string> parseLine = line =>
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) return;

                // Pre-allocation lines: suppress per-file noise, show a single summary.
                if (line.IndexOf("pre-alloc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int n = Interlocked.Increment(ref preAllocCount);
                    if (n == 1)
                    {
                        onStatus?.Invoke("Pre-allocating...");
                        onLog("Pre-allocating files on disk...");
                    }
                    return;
                }

                // Validation phase: let the UI know so it can update its status label.
                // This is the main cause of the progress bar appearing frozen at the end.
                if (line.IndexOf("validating", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lock (progressLock) { validationStarted = true; }
                    onStatus?.Invoke("Validating...");
                    onLog(line);
                    return;
                }

                var m = pctRegex.Match(line);
                if (m.Success && float.TryParse(m.Groups[1].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var rawPct))
                {
                    // First real download % after pre-allocation: log the summary count.
                    int allocated = Interlocked.Exchange(ref preAllocCount, 0);
                    if (allocated > 0)
                        onLog("Pre-allocated " + allocated + " file(s). Downloading...");

                    int pct;
                    if (totalSize > 0)
                        pct = (int)Math.Min(100, (completedSoFar + rawPct / 100.0 * depotSize) / totalSize * 100);
                    else
                        pct = (int)rawPct;

                    // If % arrives after validation started, DD is re-downloading failed chunks.
                    bool shouldNotifyRedownload = false;
                    lock (progressLock)
                    {
                        if (validationStarted && !redownloadNotified)
                        {
                            redownloadNotified = true;
                            shouldNotifyRedownload = true;
                        }
                    }
                    if (shouldNotifyRedownload)
                    {
                        onStatus?.Invoke("Re-downloading failed chunks...");
                        onLog("Validation found corrupted chunks — re-downloading...");
                    }

                    // Thread-safe dedup — only emit when the integer value changes.
                    bool emit = false;
                    lock (progressLock)
                    {
                        if (pct != lastPct) { lastPct = pct; emit = true; }
                    }
                    if (emit) onProgress(pct);
                }
                else if (line.Contains("FileNotFoundException") && line.Contains("Could not load file or assembly"))
                {
                    onLog("ERROR: DepotDownloader is missing dependencies (SteamKit2 etc.). " +
                          "Copy the full 'dotnet publish' output into the plugin's deps\\ folder.");
                }
                else if (line.StartsWith("   at "))
                {
                    // suppress stack trace lines
                }
                else if (line.IndexOf("connection timeout", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         line.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // DD retries timed-out chunks automatically — suppress per-chunk noise,
                    // but surface a single "retrying..." hint so the user knows what's happening.
                    int n = Interlocked.Increment(ref timeoutCount);
                    if (n == 1)
                        onLog("CDN timeout on a chunk — DepotDownloader is retrying automatically...");
                }
                else
                {
                    onLog(line);
                }
            };

            using (_currentProcess = new Process { StartInfo = psi })
            {
                _currentProcess.Start();

                // Stderr on a dedicated thread — prevents buffer-full deadlock and
                // handles the case where DepotDownloader writes progress to stderr.
                var stderrThread = new Thread(() =>
                    ReadByteByByte(_currentProcess.StandardError.BaseStream, parseLine))
                { IsBackground = true };
                stderrThread.Start();

                // Stdout — byte-by-byte on this thread so \r updates arrive immediately.
                ReadByteByByte(_currentProcess.StandardOutput.BaseStream, parseLine);

                // Wait up to 2 hours. If DD hangs during validation or cleanup, kill it
                // rather than leaving the plugin thread blocked indefinitely.
                const int timeoutMs = 2 * 60 * 60 * 1000;
                bool exited = _currentProcess.WaitForExit(timeoutMs);
                if (!exited)
                {
                    onLog("WARNING: DepotDownloader did not exit within 2 hours — force-killing.");
                    try { _currentProcess.Kill(); } catch { }
                }

                // Give stderr time to fully drain its final lines (summaries, errors).
                stderrThread.Join(15000);

                int code = _currentProcess.ExitCode;
                if (code != 0)
                    onLog("WARNING: DepotDownloader exited with code " + code);

                return exited && code == 0;
            }
        }

        /// <summary>
        /// Reads a stream one byte at a time, treating \r and \n both as line
        /// terminators. This mirrors ACCELA's approach and ensures every in-place
        /// console update (\r-terminated) is captured and parsed immediately.
        /// </summary>
        private static void ReadByteByByte(Stream stream, Action<string> parseLine)
        {
            var buf = new List<byte>(256);
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\r' || b == '\n')
                {
                    if (buf.Count > 0)
                    {
                        parseLine(Encoding.UTF8.GetString(buf.ToArray()));
                        buf.Clear();
                    }
                }
                else
                {
                    buf.Add((byte)b);
                }
            }
            // Flush any partial line at EOF
            if (buf.Count > 0)
                parseLine(Encoding.UTF8.GetString(buf.ToArray()));
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private string BuildArgs(
            string appId, string depotId, string manifestGid,
            string manifestFile, string keysPath, string downloadDir,
            int maxDownloads, string steamUsername)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("\"{0}\"", _depotDownloaderDll);
            if (!string.IsNullOrWhiteSpace(steamUsername))
                sb.AppendFormat(" -username {0} -remember-password", steamUsername);
            sb.AppendFormat(
                " -app {0} -depot {1} -manifest {2} -manifestfile \"{3}\" -depotkeys \"{4}\" -max-downloads {5} -dir \"{6}\" -validate",
                appId, depotId, manifestGid, manifestFile, keysPath, maxDownloads, downloadDir);
            return sb.ToString();
        }

        private static string GetPluginDir()
            => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static string FindDotnet()
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var candidate = Path.Combine(dir.Trim(), "dotnet.exe");
                if (File.Exists(candidate)) return candidate;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDotnet = Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe");
            if (File.Exists(userDotnet)) return userDotnet;

            foreach (var pf in new[] { @"C:\Program Files\dotnet\dotnet.exe", @"C:\Program Files (x86)\dotnet\dotnet.exe" })
                if (File.Exists(pf)) return pf;

            return null;
        }

        private static void TryDelete(string path, Action<string> onLog)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { onLog("WARNING: Could not delete temp file " + path + ": " + ex.Message); }
        }
    }
}
