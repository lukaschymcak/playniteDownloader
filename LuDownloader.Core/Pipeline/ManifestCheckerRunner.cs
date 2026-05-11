using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BlankPlugin
{
    public class ManifestCheckResult
    {
        public string AppId { get; set; }
        public string DepotId { get; set; }
        public string ManifestGid { get; set; }
        public string BuildId { get; set; }
    }

    /// <summary>
    /// Runs ManifestChecker.exe — a standalone .NET 9 console app that queries
    /// Steam anonymously via SteamKit2 for current manifest GIDs per depot.
    /// Mirrors the process-launching pattern of DepotDownloaderRunner.cs.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> blocks on process I/O and task <c>.Result</c>. Call from a background thread
    /// (e.g. <see cref="UpdateChecker"/>), not the Playnite UI dispatcher.
    /// </remarks>
    public class ManifestCheckerRunner
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();

        private readonly string _checkerExe;

        public ManifestCheckerRunner()
        {
            _checkerExe = Path.Combine(GetPluginDir(), "deps", "ManifestChecker.exe");
        }

        public bool IsReady => File.Exists(_checkerExe);

        /// <summary>
        /// Queries Steam for current manifest GIDs for the given AppIDs.
        /// Returns (results, null) on success or (null, errorMessage) on failure.
        /// </summary>
        public (List<ManifestCheckResult> results, string error) Run(
            IEnumerable<string> appIds,
            CancellationToken cancellationToken = default)
        {
            if (!IsReady)
                return (null, "ManifestChecker.exe not found in app deps folder.");

            if (cancellationToken.IsCancellationRequested)
                return (null, "ManifestChecker cancelled.");

            var normalizedIds = new List<string>();
            foreach (var raw in appIds)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return (null, "Invalid AppID for manifest check (empty entry).");

                var id = raw.Trim();
                if (id.Length == 0 || !uint.TryParse(id, out _))
                    return (null, "Invalid AppID for manifest check: " + raw);

                normalizedIds.Add(id);
            }

            if (normalizedIds.Count == 0)
                return (null, "No AppIDs provided.");

            var args = string.Join(" ", normalizedIds);

            // ManifestChecker.exe is a framework-dependent .NET 9 executable.
            // Run it directly — it does NOT need dotnet prefix.
            var psi = new ProcessStartInfo(_checkerExe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();

                    var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
                    var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

                    const int timeoutMs = 30_000;
                    var sw = Stopwatch.StartNew();
                    while (!proc.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            return (null, "ManifestChecker cancelled.");
                        }

                        if (sw.ElapsedMilliseconds >= timeoutMs)
                        {
                            try { proc.Kill(); } catch { }
                            return (null, "ManifestChecker.exe timed out after 30 seconds.");
                        }

                        proc.WaitForExit(250);
                    }

                    var stdout = stdoutTask.Result;
                    var stderr = stderrTask.Result;

                    if (proc.ExitCode != 0)
                    {
                        try
                        {
                            var errObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(stderr);
                            if (errObj != null && errObj.TryGetValue("error", out var errMsg))
                                return (null, errMsg);
                        }
                        catch (Exception ex)
                        {
                            logger.Debug("ManifestChecker stderr JSON parse failed: " + ex.Message);
                        }
                        return (null, "ManifestChecker.exe failed (exit code " + proc.ExitCode + "): " + stderr);
                    }

                    var results = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ManifestCheckResult>>(stdout);
                    return (results ?? new List<ManifestCheckResult>(), null);
                }
            }
            catch (Exception ex)
            {
                logger.Error("ManifestCheckerRunner failed: " + ex.Message);
                return (null, ex.Message);
            }
        }

        private static string GetPluginDir()
            => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
