using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class IptvOrgAuditLogItem
    {
        public int Id { get; set; }
        public string ChannelName { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
        public string CreatedAtFormatted { get; set; } = "";
        public string Source { get; set; } = "iptv-org";

        public string OldDisplayValue => string.IsNullOrWhiteSpace(OldValue) ? "boş" : OldValue;
        public string NewDisplayValue => string.IsNullOrWhiteSpace(NewValue) ? "boş" : NewValue;
    }

    public class IptvOrgMetadataItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string AltNames { get; set; } = "";
        public string Network { get; set; } = "";
        public string Country { get; set; } = "";
        public string Languages { get; set; } = "";
        public string Categories { get; set; } = "";
        public string Logo { get; set; } = "";
        public string Website { get; set; } = "";
        public string EpgId { get; set; } = "";
    }

    public class IptvOrgEngine
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private const string ChannelsApiUrl = "https://iptv-org.github.io/api/channels.json";

        public async Task<List<IptvOrgMetadataItem>> FetchChannelsAsync()
        {
            var list = new List<IptvOrgMetadataItem>();
            try
            {
                var response = await _httpClient.GetAsync(ChannelsApiUrl);
                if (!response.IsSuccessStatusCode) return list;

                string json = await response.Content.ReadAsStringAsync();
                var arr = JArray.Parse(json);

                foreach (var item in arr)
                {
                    var ch = new IptvOrgMetadataItem
                    {
                        Id = item["id"]?.ToString() ?? "",
                        Name = item["name"]?.ToString() ?? "",
                        Network = item["network"]?.ToString() ?? "",
                        Country = item["country"]?.ToString() ?? "",
                        Languages = string.Join(",", item["languages"]?.ToObject<List<string>>() ?? new List<string>()),
                        Categories = string.Join(",", item["categories"]?.ToObject<List<string>>() ?? new List<string>()),
                        Logo = item["logo"]?.ToString() ?? "",
                        Website = item["website"]?.ToString() ?? "",
                        EpgId = item["broadcast_area"]?.ToString() ?? item["id"]?.ToString() ?? ""
                    };
                    if (!string.IsNullOrEmpty(ch.Id))
                    {
                        list.Add(ch);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("IptvOrgEngine.FetchChannelsAsync failed", ex);
            }
            return list;
        }
    }
}
