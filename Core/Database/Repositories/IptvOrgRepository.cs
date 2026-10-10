using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Core.Media;

namespace StreamMesh.Core.Database.Repositories
{
    public class IptvOrgRepository
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock;

        public IptvOrgRepository(string connectionString, SemaphoreSlim dbLock)
        {
            _connectionString = connectionString;
            _dbLock = dbLock;
        }

        public async Task SaveBatchAsync(List<IptvOrgMetadataItem> items)
        {
            if (items == null || items.Count == 0) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    foreach (var item in items)
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = "INSERT INTO IptvOrgCache (Id, Name, Country, Languages, Categories, Logo, Website, EpgId, UpdatedAt) VALUES (@id, @name, @country, @langs, @cats, @logo, @web, @epg, @up) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Country=excluded.Country, Languages=excluded.Languages, Categories=excluded.Categories, Logo=excluded.Logo, Website=excluded.Website, EpgId=excluded.EpgId, UpdatedAt=excluded.UpdatedAt";
                        cmd.Parameters.AddWithValue("@id", item.Id ?? "");
                        cmd.Parameters.AddWithValue("@name", item.Name ?? "");
                        cmd.Parameters.AddWithValue("@country", item.Country ?? "");
                        cmd.Parameters.AddWithValue("@langs", item.Languages ?? "");
                        cmd.Parameters.AddWithValue("@cats", item.Categories ?? "");
                        cmd.Parameters.AddWithValue("@logo", item.Logo ?? "");
                        cmd.Parameters.AddWithValue("@web", item.Website ?? "");
                        cmd.Parameters.AddWithValue("@epg", item.EpgId ?? "");
                        cmd.Parameters.AddWithValue("@up", DateTime.UtcNow.ToString("o"));
                        await cmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task<IptvOrgMetadataItem?> FindMatchAsync(string channelName, string epgId, string channelLanguage = "")
        {
            if (string.IsNullOrWhiteSpace(channelName) && string.IsNullOrWhiteSpace(epgId)) return null;

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // 1. High confidence match: EpgId matches exactly and language aligns (if specified)
                if (!string.IsNullOrWhiteSpace(epgId))
                {
                    var cmdEpg = connection.CreateCommand();
                    cmdEpg.CommandText = "SELECT Id, Name, Country, Languages, Categories, Logo, Website, EpgId FROM IptvOrgCache WHERE EpgId = @epg LIMIT 1";
                    cmdEpg.Parameters.AddWithValue("@epg", epgId.Trim());
                    using var readerEpg = await cmdEpg.ExecuteReaderAsync();
                    if (await readerEpg.ReadAsync())
                    {
                        var item = MapReader(readerEpg);
                        if (string.IsNullOrWhiteSpace(channelLanguage) || channelLanguage == "und" || item.Languages.Contains(channelLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            return item;
                        }
                    }
                }

                // 2. Name / Normalized ID matching with strict ambiguity prevention
                if (!string.IsNullOrWhiteSpace(channelName))
                {
                    string cleanName = ChannelUtils.GetCleanName(channelName);
                    string normalizedId = cleanName.ToLowerInvariant().Replace(" ", "");

                    var cmdName = connection.CreateCommand();
                    cmdName.CommandText = "SELECT Id, Name, Country, Languages, Categories, Logo, Website, EpgId FROM IptvOrgCache WHERE LOWER(Id) = @id OR LOWER(Name) = LOWER(@name)";
                    cmdName.Parameters.AddWithValue("@id", normalizedId);
                    cmdName.Parameters.AddWithValue("@name", channelName.Trim());

                    var matchingItems = new List<IptvOrgMetadataItem>();
                    using (var readerName = await cmdName.ExecuteReaderAsync())
                    {
                        while (await readerName.ReadAsync())
                        {
                            matchingItems.Add(MapReader(readerName));
                        }
                    }

                    if (matchingItems.Count == 0) return null;

                    bool hasSpecificLanguage = !string.IsNullOrWhiteSpace(channelLanguage) && channelLanguage != "und";
                    if (hasSpecificLanguage)
                    {
                        var langMatches = matchingItems.Where(i => i.Languages.Contains(channelLanguage, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (langMatches.Count == 1)
                        {
                            return langMatches[0];
                        }
                        // Multiple or 0 matches for specific language -> ambiguous or non-matching, return null
                        return null;
                    }
                    else
                    {
                        // Language is empty or "und":
                        // Only match if there is EXACTLY ONE record in iptv-org cache for this name
                        if (matchingItems.Count == 1)
                        {
                            return matchingItems[0];
                        }
                        // Multiple records exist for this name across countries -> ambiguous, do not match randomly
                        return null;
                    }
                }
            }
            return null;
        }

        public async Task<List<IptvOrgMetadataItem>> SearchCacheAsync(string query, int limit = 100)
        {
            var list = new List<IptvOrgMetadataItem>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Country, Languages, Categories, Logo, Website, EpgId FROM IptvOrgCache WHERE Name LIKE @q OR Id LIKE @q LIMIT @limit";
                cmd.Parameters.AddWithValue("@q", $"%{query.Trim()}%");
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(MapReader(reader));
                }
            }
            return list;
        }

        public async Task LogAuditAsync(string channelName, string fieldName, string oldValue, string newValue)
        {
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO IptvOrgAuditLog (ChannelName, FieldName, OldValue, NewValue, CreatedAt) VALUES (@ch, @f, @old, @new, @dt)";
                    cmd.Parameters.AddWithValue("@ch", channelName ?? "");
                    cmd.Parameters.AddWithValue("@f", fieldName ?? "");
                    cmd.Parameters.AddWithValue("@old", oldValue ?? "");
                    cmd.Parameters.AddWithValue("@new", newValue ?? "");
                    cmd.Parameters.AddWithValue("@dt", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task<List<IptvOrgAuditLogItem>> GetAuditLogsAsync(string? channelFilter = null, string? fieldFilter = null, int limit = 200)
        {
            var list = new List<IptvOrgAuditLogItem>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    var clauses = new List<string>();
                    if (!string.IsNullOrWhiteSpace(channelFilter))
                    {
                        clauses.Add("ChannelName LIKE @ch");
                        cmd.Parameters.AddWithValue("@ch", $"%{channelFilter.Trim()}%");
                    }
                    if (!string.IsNullOrWhiteSpace(fieldFilter) && !fieldFilter.Equals("Tüm Alanlar", StringComparison.OrdinalIgnoreCase))
                    {
                        clauses.Add("FieldName = @f");
                        cmd.Parameters.AddWithValue("@f", fieldFilter.Trim());
                    }
                    string whereSql = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
                    cmd.CommandText = $"SELECT Id, ChannelName, FieldName, OldValue, NewValue, CreatedAt FROM IptvOrgAuditLog {whereSql} ORDER BY Id DESC LIMIT @limit";
                    cmd.Parameters.AddWithValue("@limit", limit);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string rawDt = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        string formattedDt = rawDt;
                        if (DateTime.TryParse(rawDt, out var dt))
                        {
                            formattedDt = dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                        }

                        list.Add(new IptvOrgAuditLogItem
                        {
                            Id = reader.GetInt32(0),
                            ChannelName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            FieldName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            OldValue = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            NewValue = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            CreatedAtFormatted = formattedDt,
                            Source = "iptv-org"
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        private IptvOrgMetadataItem MapReader(SqliteDataReader reader)
        {
            return new IptvOrgMetadataItem
            {
                Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Country = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Languages = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Categories = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Logo = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Website = reader.IsDBNull(6) ? "" : reader.GetString(6),
                EpgId = reader.IsDBNull(7) ? "" : reader.GetString(7)
            };
        }
    }
}
