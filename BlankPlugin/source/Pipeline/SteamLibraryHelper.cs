using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BlankPlugin
{
    /// <summary>
    /// Discovers Steam installation and library folders on Windows.
    /// Reads the registry for Steam path, then parses libraryfolders.vdf
    /// to find all configured Steam library drives.
    /// </summary>
    public static class SteamLibraryHelper
    {
        /// <summary>
        /// Reads the Steam install path from HKCU\Software\Valve\Steam\SteamPath.
        /// </summary>
        public static string GetSteamInstallPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        var path = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            return Path.GetFullPath(path);
                        }
                    }
                }
            }
            catch
            {
                // Registry read failed
            }
            return null;
        }

        /// <summary>
        /// Returns all Steam library paths found on this system.
        /// Includes the main Steam install and any additional library folders
        /// configured in libraryfolders.vdf.
        /// Each returned path has a verified "steamapps" subdirectory.
        /// </summary>
        public static List<string> GetSteamLibraries()
        {
            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var steamPath = GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamPath))
                return libraries;

            // Add main Steam directory
            var mainSteamapps = Path.Combine(steamPath, "steamapps");
            if (Directory.Exists(mainSteamapps))
            {
                var fullPath = Path.GetFullPath(steamPath);
                if (seen.Add(fullPath))
                    libraries.Add(fullPath);
            }

            // Parse libraryfolders.vdf for additional libraries
            var vdfPath = Path.Combine(mainSteamapps, "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                var additional = ParseLibraryFolders(vdfPath);
                foreach (var lib in additional)
                {
                    var fullPath = Path.GetFullPath(lib);
                    if (seen.Add(fullPath))
                        libraries.Add(fullPath);
                }
            }

            return libraries;
        }

        /// <summary>
        /// Parses a libraryfolders.vdf file and returns all library paths
        /// that have a valid "steamapps" subdirectory.
        /// Uses the same regex approach as ACCELA.
        /// </summary>
        public static List<string> ParseLibraryFolders(string vdfPath)
        {
            var paths = new List<string>();

            try
            {
                var content = File.ReadAllText(vdfPath);
                // Match lines like: "path"  "C:\SteamLibrary" or "1"  "D:\Games\Steam"
                var matches = Regex.Matches(content, @"^\s*""(?:path|\d+)""\s*""(.*?)""", RegexOptions.Multiline);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var path = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(Path.Combine(path, "steamapps")))
                        {
                            paths.Add(path);
                        }
                    }
                }
            }
            catch
            {
                // VDF parsing failed
            }

            return paths;
        }

        /// <summary>
        /// Gets the free disk space in bytes for a given path's drive.
        /// </summary>
        public static long GetFreeDiskSpace(string path)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path));
                if (drive.IsReady)
                    return drive.AvailableFreeSpace;
            }
            catch
            {
                // Drive info unavailable
            }
            return 0;
        }

        /// <summary>
        /// Formats bytes as a human-readable size string.
        /// </summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "Unknown";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }
    }
}
