using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class MetadataEngine
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task EnrichChannelAsync(Channel channel)
        {
            if (channel == null) return;

            string cat = (channel.Category ?? "").Trim().ToUpperInvariant();
            string group = (channel.GroupTitle ?? "").Trim().ToUpperInvariant();
            bool isMedia = cat == "FILM" || cat == "MOVIE" || cat == "DIZI" || cat == "SERIES" ||
                           group.Contains("FILM") || group.Contains("MOVIE") || group.Contains("SINEMA") ||
                           group.Contains("DIZI") || group.Contains("SERIES") ||
                           !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ||
                           channel.SeasonNumber > 0 || channel is SeriesGroup;

            if (!isMedia) return;

            // If already enriched with overview and backdrop/imdb, skip
            if (!string.IsNullOrWhiteSpace(channel.Overview) && (!string.IsNullOrWhiteSpace(channel.BackdropUrl) || !string.IsNullOrWhiteSpace(channel.ImdbId))) return;

            // 1. Determine clean search query
            string query = !string.IsNullOrWhiteSpace(channel.SeriesBaseName) ? channel.SeriesBaseName : channel.CleanName;
            if (string.IsNullOrWhiteSpace(query)) query = channel.PrimaryName ?? channel.Name;

            query = CleanQueryForMetadata(query);
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            try
            {
                // 2. Check local pool first (Cache Hit Check)
                var pooled = await _db.GetMetadataPoolForQueryAsync(query);
                if (pooled != null && pooled.Count > 0)
                {
                    if (pooled[0].ImdbId != "NOT_FOUND")
                    {
                        ApplyMetadata(channel, pooled[0]);
                        await _db.SaveChannelAsync(channel, notifyUpdated: false);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"MetadataEngine.EnrichChannelAsync pool lookup failed for '{query}'", ex);
            }
        }

        public static string CleanQueryForMetadata(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string cleaned = rawName;
            cleaned = Regex.Replace(cleaned, @"(?i)\b(1080p|720p|4k|2160p|hd|fhd|uhd|sd|hevc|x264|x265|web-dl|webrip|bluray|dvdrip)\b", "");
            cleaned = Regex.Replace(cleaned, @"(?i)\b(tr|eng|ger|fra|dublaj|altyazılı|altyazili|dual|multi)\b", "");
            cleaned = Regex.Replace(cleaned, @"\[.*?\]", "");
            cleaned = Regex.Replace(cleaned, @"(?i)s(\d+)\s?e(\d+)|(\d+)x(\d+)|sezon\s*\d+|bölüm\s*\d+", "");
            cleaned = Regex.Replace(cleaned, @"\((19\d{2}|20\d{2})\)", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', ':', '_', '.', ',');
            return cleaned;
        }

        private void ApplyMetadata(Channel target, MetadataResult source)
        {
            if (source == null || source.ImdbId == "NOT_FOUND") return;
            if (!string.IsNullOrWhiteSpace(source.Overview)) target.Overview = source.Overview;
            if (!string.IsNullOrWhiteSpace(source.BackdropUrl)) target.BackdropUrl = source.BackdropUrl;
            if (!string.IsNullOrWhiteSpace(source.PosterUrl) && (string.IsNullOrWhiteSpace(target.LogoUrl) || target.LogoUrl.Contains("resimlink") || !target.LogoUrl.StartsWith("http"))) target.LogoUrl = source.PosterUrl;
            if (!string.IsNullOrWhiteSpace(source.ImdbId)) target.ImdbId = source.ImdbId;
            if (!string.IsNullOrWhiteSpace(source.Cast)) target.Cast = source.Cast;

            if (target is SeriesGroup sg && sg.Episodes != null)
            {
                foreach (var ep in sg.Episodes)
                {
                    if (string.IsNullOrWhiteSpace(ep.Overview) && !string.IsNullOrWhiteSpace(source.Overview)) ep.Overview = source.Overview;
                    if (string.IsNullOrWhiteSpace(ep.BackdropUrl) && !string.IsNullOrWhiteSpace(source.BackdropUrl)) ep.BackdropUrl = source.BackdropUrl;
                    if (string.IsNullOrWhiteSpace(ep.Cast) && !string.IsNullOrWhiteSpace(source.Cast)) ep.Cast = source.Cast;
                }
            }
        }
    }
}
