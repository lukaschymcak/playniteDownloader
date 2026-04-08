using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace BlankPlugin
{
    public class ManifestCheckResult
    {
        public string AppId { get; set; }
        public string DepotId { get; set; }
        public string ManifestGid { get; set; }
    }

    /// <summary>
    /// Runs ManifestChecker.exe — a standalone .NET 9 console app that queries
    /// Steam anonymously via SteamKit2 for current manifest GIDs per depot.
    /// Mirrors the process-launching pattern of DepotDownloaderRunner.cs.
    /// </summary>
    public class ManifestCheckerRunner
    {
        private static readonly ILogger logger = LogManager.GetLogger();

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
        public (List<ManifestCheckResult> results, string error) Run(IEnumerable<string> appIds)
        {
            if (!IsReady)
                return (null, "ManifestChecker.exe not found in plugin deps folder.");

            var args = string.Join(" ", appIds);
            if (string.IsNullOrWhiteSpace(args))
                return (null, "No AppIDs provided.");

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

                    bool exited = proc.WaitForExit(30_000);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        return (null, "ManifestChecker.exe timed out after 30 seconds.");
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
                        catch { }
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
