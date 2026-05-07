using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlankPlugin
{
    /// <summary>
    /// Persists Morrenus manifest ZIPs under the plugin user data directory.
    /// </summary>
    public static class ManifestCache
    {
        private static readonly ICoreLogger logger = CoreLogManager.GetLogger();
        private static readonly Regex SafeAppIdRegex = new Regex(@"^\d+$", RegexOptions.Compiled);

        public static string GetCacheDirectory(string pluginUserDataPath)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
                return null;
            var dir = Path.Combine(pluginUserDataPath, "manifest_cache");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                logger.Warn("ManifestCache: could not create directory: " + ex.Message);
            }
            return dir;
        }

        /// <summary>Returns null if appId is not a safe numeric Steam AppId for use in filenames.</summary>
        public static string SanitizeAppIdForFileName(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return null;
            var t = appId.Trim();
            return SafeAppIdRegex.IsMatch(t) ? t : null;
        }

        public static string GetCachedZipPath(string cacheDirectory, string appId)
        {
            var id = SanitizeAppIdForFileName(appId);
            if (id == null || string.IsNullOrEmpty(cacheDirectory))
                return null;
            return Path.Combine(cacheDirectory, id + ".zip");
        }

        public static string GetPartPath(string cacheDirectory, string appId)
        {
            var id = SanitizeAppIdForFileName(appId);
            if (id == null || string.IsNullOrEmpty(cacheDirectory))
                return null;
            return Path.Combine(cacheDirectory, id + ".zip.part");
        }

        public static string GetMetaPath(string cacheDirectory, string appId)
        {
            var id = SanitizeAppIdForFileName(appId);
            if (id == null || string.IsNullOrEmpty(cacheDirectory))
                return null;
            return Path.Combine(cacheDirectory, id + ".meta.json");
        }

        public static bool TryGetCachedZipPath(string cacheDirectory, string appId, out string zipPath)
        {
            zipPath = GetCachedZipPath(cacheDirectory, appId);
            if (string.IsNullOrEmpty(zipPath))
                return false;
            return File.Exists(zipPath);
        }

        public static void TryDeletePartFile(string partPath)
        {
            if (string.IsNullOrEmpty(partPath))
                return;
            try
            {
                if (File.Exists(partPath))
                    File.Delete(partPath);
            }
            catch (Exception ex)
            {
                logger.Debug("ManifestCache: could not delete part file: " + ex.Message);
            }
        }

        /// <summary>Replace <c>{id}.zip</c> with completed <c>{id}.zip.part</c>.</summary>
        public static void CommitPartToZip(string cacheDirectory, string appId)
        {
            var part = GetPartPath(cacheDirectory, appId);
            var final = GetCachedZipPath(cacheDirectory, appId);
            if (string.IsNullOrEmpty(part) || string.IsNullOrEmpty(final))
                throw new InvalidOperationException("Invalid manifest cache path.");
            if (!File.Exists(part))
                throw new FileNotFoundException("Download part file missing.", part);
            if (File.Exists(final))
                File.Delete(final);
            File.Move(part, final);
        }

        public static void WriteMeta(string cacheDirectory, GameData data)
        {
            if (data == null || string.IsNullOrEmpty(cacheDirectory))
                return;
            var metaPath = GetMetaPath(cacheDirectory, data.AppId);
            if (string.IsNullOrEmpty(metaPath))
                return;
            try
            {
                var dto = new ManifestCacheMetaDto
                {
                    GameName = data.GameName ?? "",
                    AppId = data.AppId ?? "",
                    UtcSaved = DateTime.UtcNow.ToString("o")
                };
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex)
            {
                logger.Warn("ManifestCache: could not write meta: " + ex.Message);
            }
        }

        public static bool TryReadDisplayName(string cacheDirectory, string appId, out string displayName)
        {
            displayName = null;
            var metaPath = GetMetaPath(cacheDirectory, appId);
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
                return false;
            try
            {
                var json = File.ReadAllText(metaPath);
                var dto = JsonConvert.DeserializeObject<ManifestCacheMetaDto>(json);
                if (dto != null && !string.IsNullOrWhiteSpace(dto.GameName))
                {
                    displayName = dto.GameName.Trim();
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Debug("ManifestCache: could not read meta: " + ex.Message);
            }
            return false;
        }

        /// <summary>Sorted by display name (then AppId). Skips invalid zip names.</summary>
        public static List<ManifestCacheEntry> EnumerateCached(string cacheDirectory)
        {
            var list = new List<ManifestCacheEntry>();
            if (string.IsNullOrEmpty(cacheDirectory) || !Directory.Exists(cacheDirectory))
                return list;

            foreach (var zip in Directory.GetFiles(cacheDirectory, "*.zip"))
            {
                var name = Path.GetFileNameWithoutExtension(zip);
                if (SanitizeAppIdForFileName(name) == null)
                    continue;
                var appId = name;
                TryReadDisplayName(cacheDirectory, appId, out var title);
                if (string.IsNullOrEmpty(title))
                    title = "App " + appId;
                DateTime? saved = null;
                var metaPath = GetMetaPath(cacheDirectory, appId);
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var dto = JsonConvert.DeserializeObject<ManifestCacheMetaDto>(File.ReadAllText(metaPath));
                        if (dto != null && DateTime.TryParse(dto.UtcSaved, out var u))
                            saved = u;
                    }
                    catch { /* skip */ }
                }
                list.Add(new ManifestCacheEntry
                {
                    AppId = appId,
                    DisplayName = title,
                    ZipPath = zip,
                    SavedUtc = saved
                });
            }
            return list
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.AppId, StringComparer.Ordinal)
                .ToList();
        }

        public static void DeleteCached(string cacheDirectory, string appId)
        {
            var zip = GetCachedZipPath(cacheDirectory, appId);
            var meta = GetMetaPath(cacheDirectory, appId);
            var part = GetPartPath(cacheDirectory, appId);
            TryDeletePartFile(part);
            try { if (!string.IsNullOrEmpty(zip) && File.Exists(zip)) File.Delete(zip); } catch { }
            try { if (!string.IsNullOrEmpty(meta) && File.Exists(meta)) File.Delete(meta); } catch { }
        }

        private class ManifestCacheMetaDto
        {
            public string GameName { get; set; }
            public string AppId { get; set; }
            public string UtcSaved { get; set; }
        }
    }

    public sealed class ManifestCacheEntry
    {
        public string AppId { get; set; }
        public string DisplayName { get; set; }
        public string ZipPath { get; set; }
        public DateTime? SavedUtc { get; set; }
    }
}
