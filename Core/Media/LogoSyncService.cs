using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class LogoSyncService
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();
        private static readonly HttpClient _httpClient = new HttpClient();

        static LogoSyncService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 StreamMesh/1.0");
        }

        public async Task SyncIfNecessaryAsync()
        {
            try
            {
                string lastSync = _db.GetSetting("LastLogoSyncDate", "");
                if (string.IsNullOrEmpty(lastSync) || (DateTime.TryParse(lastSync, out var date) && (DateTime.Now - date).TotalDays >= 30))
                {
                    await SyncNowAsync();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSyncService: SyncIfNecessaryAsync failed", ex);
            }
        }

        public async Task SyncNowAsync()
        {
            LogService.LogInfo("LogoSyncService: Starting logo synchronization from tv-logos GitHub...");
            try
            {
                var items = new List<(string key, string file)>();
                var countries = new[] { "tr", "de" };
                foreach (var country in countries)
                {
                    try
                    {
                        string url = $"https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/{country}/{country}.json";
                        var response = await _httpClient.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            url = $"https://raw.githubusercontent.com/tv-logo/tv-logos/master/countries/{country}/{country}.json";
                            response = await _httpClient.GetAsync(url);
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            var data = JObject.Parse(json);
                            if (data != null)
                            {
                                foreach (var prop in data.Properties())
                                {
                                    string key = prop.Name;
                                    string val = prop.Value?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                                    {
                                        items.Add((key, val));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogWarning($"LogoSyncService: Failed to sync logos for {country}: {ex.Message}");
                    }
                }

                if (items.Count > 0)
                {
                    _db.UpdateLogoIndex(items);
                    _db.SetSetting("LastLogoSyncDate", DateTime.Now.ToString("o"));
                    ChannelEnricher.InvalidateLogoCache();
                    LogService.LogInfo($"LogoSyncService: Successfully synchronized {items.Count} logos.");
                }
                else
                {
                    LogService.LogWarning("LogoSyncService: No logos retrieved during sync.");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("LogoSyncService: SyncNowAsync failed", ex);
            }
        }
    }
}
