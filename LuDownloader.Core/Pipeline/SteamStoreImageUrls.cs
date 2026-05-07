using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlankPlugin
{
    /// <summary>
    /// Ordered Steam CDN URLs for store-style cover art. Not all apps publish <c>header.jpg</c>;
    /// fallbacks match other common Steam asset names (e.g. library capsule / hero).
    /// </summary>
    public static class SteamStoreImageUrls
    {
        private const string CdnBase = "https://cdn.akamai.steamstatic.com/steam/apps/";

        private static readonly string[] CoverSuffixes =
        {
            "header.jpg",
            "library_600x900.jpg",
            "capsule_616x353.jpg",
            "library_hero.jpg"
        };

        /// <summary>Returns empty if <paramref name="appId"/> is missing or not a Steam numeric app id.</summary>
        public static IReadOnlyList<Uri> GetHeaderStyleCoverUris(string appId)
        {
            var list = new List<Uri>(CoverSuffixes.Length);
            if (string.IsNullOrWhiteSpace(appId))
                return list;

            var id = appId.Trim();
            if (id.Length == 0)
                return list;

            if (!uint.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return list;

            foreach (var suffix in CoverSuffixes)
                list.Add(new Uri(CdnBase + id + "/" + suffix, UriKind.Absolute));

            return list;
        }
    }
}
