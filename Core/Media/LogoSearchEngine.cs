using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSearchResult
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public static class LogoSearchEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly DatabaseEngine _db = new DatabaseEngine();

        public static async Task<List<LogoSearchResult>> SearchLogosAsync(string query, string sourceFilter = "ALL")
        {
            var results = new List<LogoSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            sourceFilter = sourceFilter?.ToUpperInvariant() ?? "ALL";

            // 1. Search in local tv-logos index (SQLite LogoIndex table)
            if (sourceFilter == "ALL" || sourceFilter == "TV_LOGOS")
            {
                try
                {
                    var localIndex = _db.GetAllLogoIndex();
                    string cleanQuery = ChannelUtils.GetCleanName(query).ToLowerInvariant().Replace(" ", "-");
                    foreach (var kvp in localIndex)
                    {
                        if (kvp.Key.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) || kvp.Value.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new LogoSearchResult
                            {
                                Name = kvp.Key,
                                Url = kvp.Value,
                                Source = "tv-logos (Yerel İndeks)"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"LogoSearchEngine: Local index search failed: {ex.Message}");
                }
            }

            // 2. Search in iptv-org cache
            if (sourceFilter == "ALL" || sourceFilter == "IPTV_ORG")
            {
                try
                {
                    var matches = await _db.SearchIptvOrgCacheAsync(query);
                    foreach (var m in matches)
                    {
                        if (!string.IsNullOrWhiteSpace(m.Logo))
                        {
                            results.Add(new LogoSearchResult
                            {
                                Name = m.Name ?? "",
                                Url = m.Logo ?? "",
                                Source = $"iptv-org ({m.Country ?? "Global"})"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"LogoSearchEngine: iptv-org search failed: {ex.Message}");
                }
            }

            // 3. Search in Clearbit API (for matching web domains or fallback)
            if (sourceFilter == "ALL" || sourceFilter == "CLEARBIT")
            {
                try
                {
                    string domain = query.Trim();
                    if (!domain.Contains(".") && !domain.Contains(" "))
                    {
                        domain += ".com";
                    }
                    if (domain.Contains(".") && !domain.Contains(" "))
                    {
                        string clearbitUrl = $"https://logo.clearbit.com/{domain}";
                        results.Add(new LogoSearchResult
                        {
                            Name = domain,
                            Url = clearbitUrl,
                            Source = "Clearbit (Web)"
                        });
                    }
                }
                catch { }
            }

            // Deduplicate by URL
            return results.GroupBy(r => r.Url.ToLowerInvariant()).Select(g => g.First()).ToList();
        }
    }
}
